using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Model;
using JsonGoddess.WebPerformanceTests.Generated;
using JsonGoddess.WebPerformanceTests.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace JsonGoddess.WebPerformanceTests
{
    /// <summary>
    /// Путь байта до тела ответа, разобранный по звеньям.
    ///
    /// <para>
    /// Лестница (<see cref="StaircaseFixture"/>) отвечает на вопрос «какую долю
    /// запроса занимает JSON». Здесь вопрос другой и более узкий: <b>что из
    /// измеренного времени записи тратит наш код, а что - обёртка эталона</b>.
    /// Ответ нужен до того, как писать свой выходной форматтер, а не после:
    /// работа эта обозримая, но если обёртка стои́т три процента, делать её
    /// незачем.
    /// </para>
    ///
    /// <para>
    /// Сегодня ответ пишется через три буфера:
    /// </para>
    ///
    /// <list type="number">
    /// <item>порождённый писатель кладёт документ в экзостер - арендованный
    /// буфер в памяти;</item>
    /// <item>конвертер моста отдаёт его эталону одним <c>WriteRawValue</c>, и
    /// <c>Utf8JsonWriter</c> <b>копирует</b> его в свой буфер;</item>
    /// <item><c>Utf8JsonWriter</c> сливает свой буфер в поток ответа.</item>
    /// </list>
    ///
    /// <para>
    /// Свой выходной форматтер писал бы экзостером прямо в
    /// <c>HttpResponse.BodyWriter</c>: звено 2 исчезло бы целиком, звено 1
    /// слилось бы со звеном 3. Цена звена 2 - это и есть то, что здесь
    /// меряется, и меряется она вычитанием, а не догадкой.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>эталон, в поток</b> - опорная строка;</item>
    /// <item><b>мост, в поток</b> - то, что происходит сейчас: все три
    /// звена;</item>
    /// <item><b>экзостер + копия в поток</b> - звенья 1 и 3, без
    /// <c>Utf8JsonWriter</c>. Копия здесь нарочно самая грубая
    /// (<c>Stream.Write</c> по готовому спану), то есть это <b>верхняя</b>
    /// оценка того, во что обойдётся свой форматтер;</item>
    /// <item><b>экзостер</b> - одно звено 1: чистая запись, ниже которой не
    /// опустится никакой форматтер.</item>
    /// </list>
    ///
    /// <para>
    /// Две последние строки - не «наш код в вакууме»: это ровно тот код,
    /// который зовёт мост, тем же профилем и тем же экзостером. Совпадение
    /// документов проверяется побайтово до первого замера
    /// (<see cref="VerifyAsync"/>), иначе таблица сравнивала бы разные
    /// программы.
    /// </para>
    ///
    /// <para>
    /// Форматтер приложения стои́т здесь же, последней парой строк, чтобы
    /// связать эту таблицу с лестницей в одном прогоне: сравнивать числа между
    /// прогонами - ровно то, ради чего заведён <c>run-benchmarks.bat</c>.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class WritePathFixture
    {
        private Pipeline _reference = null!;

        private Pipeline _goddess = null!;

        /// <summary>
        /// Приёмник. Один на все итерации и обнуляется длиной, а не
        /// пересоздаётся: иначе замер записи мерил бы заодно выделение буфера.
        /// </summary>
        private MemoryStream _sink = null!;

        /// <summary>
        /// Экзостер. Тоже один на все итерации и сбрасывается
        /// <c>Reset</c>'ом - арендованный буфер остаётся при нём. Пересоздавать
        /// его на каждой итерации значило бы мерить аренду из пула, а не запись;
        /// именно так живёт и мост, который держит экзостер в конвертере.
        /// </summary>
        private Compat.EncoderUtf8Exhauster _exhauster = null!;

        private DefaultHttpContext _http = null!;

        [GlobalSetup]
        public void Setup()
        {
            SetupAsync().GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _exhauster.Dispose();
            _goddess.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _reference.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private async Task SetupAsync()
        {
            _reference = await Pipeline.StartAsync(false);
            _goddess = await Pipeline.StartAsync(true);

            _sink = new MemoryStream(1 << 20);
            _http = new DefaultHttpContext();
            _http.Response.Body = _sink;

            //Энкодер берётся у тех опций, которыми MVC пишет ответ, а не
            //назначается. Он там UnsafeRelaxedJsonEscaping, и знать это
            //наизусть не надо: экзостер спрашивает, а стенд - тем более.
            var encoder = _goddess.MvcWriteOptions.Encoder ?? JavaScriptEncoder.Default;

            _exhauster = new Compat.EncoderUtf8Exhauster(encoder, 1 << 20);

            await VerifyAsync();
        }

        /// <summary>
        /// Все четыре звена обязаны давать один документ. Без этой проверки
        /// таблица сравнивала бы скорости двух разных программ - и та, что
        /// пишет меньше, выигрывала бы честно и бессмысленно.
        /// </summary>
        private async Task VerifyAsync()
        {
            Console.WriteLine(
                "mvc writes with encoder: "
                + (_goddess.MvcWriteOptions.Encoder?.GetType().Name ?? "<none, i.e. the default>"));

            SameBytes("one", Reference(Payload.One), Bridge(Payload.One), Sink(Payload.One));
            SameBytes("many", Reference(Payload.Many), Bridge(Payload.Many), Sink(Payload.Many));

            SameBytes(
                "one, formatter",
                Reference(Payload.One),
                await Formatted(_reference.Formatter, typeof(Order), Payload.One),
                await Formatted(_goddess.Formatter, typeof(Order), Payload.One));
            SameBytes(
                "many, formatter",
                Reference(Payload.Many),
                await Formatted(_reference.Formatter, typeof(Order[]), Payload.Many),
                await Formatted(_goddess.Formatter, typeof(Order[]), Payload.Many));

            Console.WriteLine("write path: every link produces the same document.");
        }

        private byte[] Reference(object value)
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, value, value.GetType(), _reference.MvcWriteOptions);
            return _sink.ToArray();
        }

        private byte[] Bridge(object value)
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, value, value.GetType(), _goddess.MvcWriteOptions);
            return _sink.ToArray();
        }

        private byte[] Sink(object value)
        {
            _exhauster.Reset();

            if (value is Order[] many)
            {
                WriteMany(many);
            }
            else
            {
                WriteHost.Serialize(_exhauster, (Order)value);
            }

            return _exhauster.ToArray();
        }

        /// <summary>
        /// Массив в корне, написанный поэлементно.
        ///
        /// <para>
        /// Скобки и запятая идут <c>AppendRaw</c>'ом, потому что структура
        /// документа - константа этапа компиляции, и порождённый писатель
        /// коллекции печатает ровно это же. Причина, по которой здесь стои́т
        /// цикл, а не вызов порождённого писателя, отдельная и стои́т
        /// внимания: <c>[JsonSubject(typeof(Order[]), true)]</c> точки входа
        /// <b>не даёт</b> - корень-массив генератор принимает молча и ничего
        /// для него не печатает.
        /// </para>
        ///
        /// <para>
        /// Замеру это ничего не портит: сравниваются те же байты той же
        /// экзостером, и три <c>AppendRaw</c> на тысячу объектов - не расход.
        /// Важно другое: <b>мост на массиве вызывается поэлементно</b>. Скобки
        /// и запятые пишет <c>Utf8JsonWriter</c>, а наш конвертер зовётся
        /// тысячу раз, и <c>WriteRawValue</c> - тоже тысячу. Поэтому строка
        /// «экзостер» здесь меряет не то же самое, что мост, а то, во что
        /// превратился бы массив у своего выходного форматтера.
        /// </para>
        /// </summary>
        private void WriteMany(Order[] many)
        {
            _exhauster.AppendRaw("["u8);

            for (var index = 0; index < many.Length; index++)
            {
                if (index > 0)
                {
                    _exhauster.AppendRaw(","u8);
                }

                WriteHost.Serialize(_exhauster, many[index]);
            }

            _exhauster.AppendRaw("]"u8);
        }

        private async Task<byte[]> Formatted(SystemTextJsonOutputFormatter formatter, Type type, object value)
        {
            _sink.SetLength(0);
            await formatter.WriteAsync(
                new OutputFormatterWriteContext(
                    _http,
                    (stream, encoding) => new StreamWriter(stream, encoding),
                    type,
                    value));
            return _sink.ToArray();
        }

        private static void SameBytes(string what, params byte[][] documents)
        {
            for (var index = 1; index < documents.Length; index++)
            {
                if (documents[0].Length == documents[index].Length
                    && new ReadOnlySpan<byte>(documents[0]).SequenceEqual(documents[index]))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    what + ": link " + index + " writes a different document ("
                    + documents[0].Length + " vs " + documents[index].Length + " bytes): "
                    + Encoding.UTF8.GetString(documents[0]) + " / "
                    + Encoding.UTF8.GetString(documents[index]));
            }
        }

        // --- звено за звеном, один объект ------------------------------------

        [BenchmarkCategory("one")]
        [Benchmark(Baseline = true, Description = "System.Text.Json, to a stream")]
        public long OneReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _reference.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("one")]
        [Benchmark(Description = "bridge, to a stream (all three links)")]
        public long OneBridge()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _goddess.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("one")]
        [Benchmark(Description = "sink + copy to a stream (no Utf8JsonWriter)")]
        public long OneSinkAndCopy()
        {
            _exhauster.Reset();
            WriteHost.Serialize(_exhauster, Payload.One);

            _sink.SetLength(0);
            _sink.Write(_exhauster.WrittenSpan);
            return _sink.Length;
        }

        [BenchmarkCategory("one")]
        [Benchmark(Description = "sink alone (the floor)")]
        public int OneSink()
        {
            _exhauster.Reset();
            WriteHost.Serialize(_exhauster, Payload.One);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("one")]
        [Benchmark(Description = "formatter: System.Text.Json")]
        public async Task<long> OneFormatterReference()
        {
            _sink.SetLength(0);
            await _reference.Formatter.WriteAsync(Context(typeof(Order), Payload.One));
            return _sink.Length;
        }

        [BenchmarkCategory("one")]
        [Benchmark(Description = "formatter: + bridge")]
        public async Task<long> OneFormatterGoddess()
        {
            _sink.SetLength(0);
            await _goddess.Formatter.WriteAsync(Context(typeof(Order), Payload.One));
            return _sink.Length;
        }

        // --- то же самое, тысяча объектов ------------------------------------

        [BenchmarkCategory("many")]
        [Benchmark(Baseline = true, Description = "System.Text.Json, to a stream")]
        public long ManyReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _reference.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many")]
        [Benchmark(Description = "bridge, to a stream (all three links)")]
        public long ManyBridge()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _goddess.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many")]
        [Benchmark(Description = "sink + copy to a stream (no Utf8JsonWriter)")]
        public long ManySinkAndCopy()
        {
            _exhauster.Reset();
            WriteMany(Payload.Many);

            _sink.SetLength(0);
            _sink.Write(_exhauster.WrittenSpan);
            return _sink.Length;
        }

        [BenchmarkCategory("many")]
        [Benchmark(Description = "sink alone (the floor)")]
        public int ManySink()
        {
            _exhauster.Reset();
            WriteMany(Payload.Many);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("many")]
        [Benchmark(Description = "formatter: System.Text.Json")]
        public async Task<long> ManyFormatterReference()
        {
            _sink.SetLength(0);
            await _reference.Formatter.WriteAsync(Context(typeof(Order[]), Payload.Many));
            return _sink.Length;
        }

        [BenchmarkCategory("many")]
        [Benchmark(Description = "formatter: + bridge")]
        public async Task<long> ManyFormatterGoddess()
        {
            _sink.SetLength(0);
            await _goddess.Formatter.WriteAsync(Context(typeof(Order[]), Payload.Many));
            return _sink.Length;
        }

        private OutputFormatterWriteContext Context(Type type, object value)
        {
            return new OutputFormatterWriteContext(
                _http,
                (stream, encoding) => new StreamWriter(stream, encoding),
                type,
                value);
        }
    }
}
