//PipeReader-перегрузки чтения появились у эталона в 9.0, и на net472 их нет
//вовсе - как нет там и самого ASP.NET Core, ради которого эта ветка и живёт.
#if NET9_0_OR_GREATER

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess.Compat.Interop;
using Xunit;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Чтение через <c>PipeReader</c> - то, как ASP.NET Core читает тело
    /// запроса начиная с .NET 10.
    ///
    /// <para>
    /// Заведён по находке пробы, а не для полноты. Значение, пересёкшее границу
    /// сегмента трубы, приезжает <c>ReadOnlySequence</c>'ом:
    /// <c>HasValueSequence</c> истинно, <c>ValueSpan</c> пуст, байты лежат в
    /// <c>ValueSequence</c>. Конвертер, читающий <c>ValueSpan</c> напрямую,
    /// получает пустоту - это и есть изменение, ломающее совместимость, о
    /// котором предупреждают заметки .NET 10.
    /// </para>
    ///
    /// <para>
    /// <b>А наши тесты с нарезкой потока этого не проверяли.</b> Замерено
    /// пробой: на потоке <c>HasValueSequence</c> не бывает вовсе - эталон
    /// копирует в один непрерывный буфер, - поэтому нарезка по три и по семь
    /// байт не порождает ни одной многосегментной лексемы. Порождает её только
    /// труба, и только она проверяет ту ветку мостового читателя, где имя или
    /// значение собирается из кусков.
    /// </para>
    /// </summary>
    public class PipeReaderFixture
    {
        private static readonly JsonSerializerOptions Bridge = new JsonSerializerOptions().UseJsonGoddess();

        /// <summary>
        /// Труба, отдающая по одному байту за обращение, каждый - своим
        /// сегментом. Злее для сборки лексем не бывает: граница приходится на
        /// каждый байт.
        /// </summary>
        private sealed class OneByteAtATime : PipeReader
        {
            private sealed class Segment : ReadOnlySequenceSegment<byte>
            {
                internal Segment(ReadOnlyMemory<byte> memory, Segment? previous)
                {
                    Memory = memory;

                    if (previous is not null)
                    {
                        previous.Next = this;
                        RunningIndex = previous.RunningIndex + previous.Memory.Length;
                    }
                }
            }

            private readonly byte[] _body;

            private int _start;

            private int _exposed;

            private ReadOnlySequence<byte> _last;

            internal OneByteAtATime(byte[] body) => _body = body;

            public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                if (_exposed < _body.Length)
                {
                    _exposed++;
                }

                _last = Build(_start, _exposed - _start);

                return new ValueTask<ReadResult>(
                    new ReadResult(_last, isCanceled: false, isCompleted: _exposed >= _body.Length)
                    );
            }

            private ReadOnlySequence<byte> Build(int start, int length)
            {
                if (length <= 1)
                {
                    return new ReadOnlySequence<byte>(_body, start, length);
                }

                Segment? first = null;
                Segment? last = null;

                for (var i = 0; i < length; i++)
                {
                    last = new Segment(new ReadOnlyMemory<byte>(_body, start + i, 1), last);
                    first ??= last;
                }

                return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
            }

            public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
            {
                //длиной среза, а не GetOffset: последний на последовательности
                //над массивом считает от начала МАССИВА, и ошибка эта тихая
                _start += (int)_last.Slice(0, consumed).Length;
            }

            public override void CancelPendingRead()
            {
            }

            public override void Complete(Exception? exception = null)
            {
            }

            public override bool TryRead(out ReadResult result)
            {
                result = default;
                return false;
            }
        }

        private static Order Awkward()
        {
            var order = Order.CreateSample();

            //всё, что собирается из кусков иначе, чем читается целиком:
            //экранирование, не-ASCII, суррогатная пара, управляющий символ
            order.Customer = "кавычка \" слэш \\ юникод é中😀 таб \t конец";

            return order;
        }

        [Fact]
        public async Task The_bridge_reads_the_same_values_through_a_pipe()
        {
            var json = Reference.SerializeToUtf8Bytes(Awkward());

            var theirs = await Reference.DeserializeAsync<Order>(new OneByteAtATime(json));
            var ours = await Reference.DeserializeAsync<Order>(new OneByteAtATime(json), Bridge);

            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        [Fact]
        public async Task A_nested_root_read_through_a_pipe_agrees_too()
        {
            var batch = new[] { Awkward(), Order.CreateSample(), Awkward(), };
            var json = Reference.SerializeToUtf8Bytes(batch);

            var theirs = await Reference.DeserializeAsync<Order[]>(new OneByteAtATime(json));
            var ours = await Reference.DeserializeAsync<Order[]>(new OneByteAtATime(json), Bridge);

            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        /// <summary>
        /// Проверка самой проверки: труба обязана действительно порождать
        /// многосегментные лексемы, иначе тест зелёный и бесполезен - ровно
        /// как наши тесты с нарезкой потока.
        /// </summary>
        [Fact]
        public async Task The_pipe_really_does_split_values_across_segments()
        {
            var json = Reference.SerializeToUtf8Bytes(Awkward());
            var pipe = new OneByteAtATime(json);
            var sequences = 0;

            while (true)
            {
                var read = await pipe.ReadAsync();
                var reader = new Utf8JsonReader(read.Buffer, read.IsCompleted, default);

                while (reader.Read())
                {
                    if (reader.HasValueSequence)
                    {
                        sequences++;
                    }
                }

                if (read.IsCompleted)
                {
                    pipe.AdvanceTo(read.Buffer.End);
                    break;
                }

                pipe.AdvanceTo(read.Buffer.Start, read.Buffer.End);
            }

            Assert.True(sequences > 0, "труба не разрезала ни одной лексемы - проверка ничего не проверяет");
        }
    }
}

#endif
