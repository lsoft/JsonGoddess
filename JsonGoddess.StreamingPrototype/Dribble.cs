using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Труба, отдающая ровно по одному байту за обращение.
    ///
    /// <para>
    /// Заведена потому, что обычный <see cref="Pipe"/> для этой проверки не
    /// годится: писатель в тесте обгоняет читателя, байты копятся в буфере, и
    /// «куски по 1» превращаются в окно на десятки килобайт. Настоящая проверка
    /// корректности - когда окно кончается <b>на каждом байте</b>, в том числе
    /// перед закрывающей кавычкой, посреди <c>é</c> и между запятой и
    /// следующим именем.
    /// </para>
    ///
    /// <para>
    /// <paramref name="perByteSegments"/> добавляет вторую злость: каждый байт
    /// становится своим сегментом, то есть окно всегда многосегментное и
    /// собирается заново на каждом обращении.
    /// </para>
    /// </summary>
    internal sealed class Dribble : PipeReader
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

        private readonly bool _perByteSegments;

        private readonly int _step;

        private int _start;

        private int _exposed;

        private ReadOnlySequence<byte> _last;

        internal Dribble(byte[] body, bool perByteSegments, int step = 1)
        {
            _body = body;
            _perByteSegments = perByteSegments;
            _step = step;
        }

        internal int Reads { get; private set; }

        /// <summary>Сколько байт документа уже потреблено - для разбора находок.</summary>
        internal int Start => _start;

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_exposed < _body.Length)
            {
                _exposed = Math.Min(_exposed + _step, _body.Length);
            }

            Reads++;
            _last = Build(_start, _exposed - _start);

            return new ValueTask<ReadResult>(
                new ReadResult(_last, isCanceled: false, isCompleted: _exposed >= _body.Length)
                );
        }

        private ReadOnlySequence<byte> Build(int start, int length)
        {
            if (!_perByteSegments || length <= 1)
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
            //Длиной среза, а не GetOffset: на последовательности над массивом
            //GetOffset считает от начала МАССИВА, а не от начала окна, и
            //ошибка эта тихая - окно уезжает на байт вперёд, и разбор ломается
            //далеко от места, где соврали.
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
}
