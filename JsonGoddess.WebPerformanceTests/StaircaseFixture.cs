using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Model;
using JsonGoddess.WebPerformanceTests.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Goddess = JsonGoddess.Compat.Interop.JsonGoddess;

namespace JsonGoddess.WebPerformanceTests
{
    /// <summary>
    /// Лестница: одна и та же полезная нагрузка, померенная на четырёх
    /// глубинах.
    ///
    /// <para>
    /// Одиночный замер «запрос к контроллеру» ответа не даёт. В минимальном
    /// приложении запрос стои́т десятки микросекунд, а выигрыш на сериализации -
    /// сотни наносекунд; он там утонет, и таблица покажет «разницы нет» - что
    /// будет неправдой про сериализацию и правдой про приложение. Одно число
    /// не умеет сказать обе эти вещи сразу, а четыре - умеют.
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>Голая сериализация на настроенных опциях</b> - той раковиной,
    /// которую потребитель отдал в <c>AddJsonOptions</c>. Ею MVC <b>читает</b>
    /// тело запроса, и ею же писал бы ответ, не вмешайся ступень 2. Здесь
    /// виден весь выигрыш моста.</item>
    /// <item><b>Голая сериализация на тех опциях, которыми ответ пишется на
    /// самом деле</b> - то есть копией предыдущих с подменённым энкодером.
    /// Разница между ступенями 1 и 2 - и есть главная находка этой лестницы,
    /// см. ниже.</item>
    /// <item><b>Форматтер</b> - <c>SystemTextJsonOutputFormatter</c>, взятый у
    /// приложения, а не построенный рядом. Цена слоя MVC вокруг записи.</item>
    /// <item><b>Весь конвейер</b> - запрос через <c>TestServer</c>:
    /// маршрутизация, фильтры, результат, тело до последнего байта.</item>
    /// </list>
    ///
    /// <para>
    /// <b>И то же самое на чтении.</b> Тело запроса MVC разбирает <b>третьей</b>
    /// раковиной опций - настроенной, без подменённого энкодера, - и другим
    /// путём внутри эталона. Померить запись и назвать результат «мост работает
    /// в ASP.NET» значило бы проверить половину и выдать её за целое, поэтому у
    /// лестницы есть голое чтение и <c>POST</c> через весь конвейер.
    /// </para>
    ///
    /// <para>
    /// <b>Что нашла эта лестница.</b> MVC не отдаёт форматтеру настроенную
    /// раковину опций, а <b>копирует</b> её и ставит в копии
    /// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> - всегда, если только
    /// потребитель не выставил энкодер сам; minimal API несёт такой же энкодер
    /// прямо в объявлении своих опций. Пока порождённый писатель нёс свой
    /// набор экранируемого, мост на такие опции не брался - и правильно делал,
    /// потому что выдал бы <b>другой документ</b>. Ступени 3 и 4 показывали
    /// тогда единицу: веб-профиль обслуживал у MVC чтение запроса и не
    /// обслуживал запись ответа.
    /// </para>
    ///
    /// <para>
    /// Починено не подгонкой набора, а отказом его иметь:
    /// <c>EncoderUtf8Exhauster</c> экранирует <b>тем самым энкодером</b>, что
    /// лежит в опциях. Ступени 1 и 2 остаются раздельными именно затем, чтобы
    /// это было видно числом: раковина записи отличается от настроенной одним
    /// свойством, и если мост когда-нибудь снова начнёт на нём отступать,
    /// вторая строка покажет единицу, а первая - нет.
    /// </para>
    ///
    /// <para>
    /// Minimal API стои́т рядом с четвёртой ступенью, а не над ней:
    /// это не более глубокий слой, а <b>другой</b> конвейер, с другой раковиной
    /// опций. Нужен он ради вопроса, на который иначе нет ответа: достаёт ли
    /// мост до обоих способов писать веб-метод.
    /// </para>
    ///
    /// <para>
    /// <b>Достаёт - до обоих.</b> Свежий
    /// <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> несёт
    /// <c>UnsafeRelaxedJsonEscaping</c> прямо в объявлении, то есть на записи
    /// ответа релаксированный энкодер стои́т у обоих способов писать веб-метод.
    /// Раковине это безразлично - она спрашивает энкодер, а не помнит его, -
    /// и minimal API обслуживается наравне с MVC. Строка в таблице остаётся
    /// затем, что «наравне» - утверждение, которое надо держать числом.
    /// </para>
    ///
    /// <para>
    /// Обе колонки в каждой категории считают один и тот же документ, и это
    /// проверяется побайтово в <see cref="Verify"/>. Без проверки таблица
    /// сравнивала бы не скорости, а две разные программы.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class StaircaseFixture
    {
        private Pipeline _reference = null!;
        private Pipeline _goddess = null!;

        /// <summary>
        /// Приёмник нижних двух ступеней. Один на все итерации и обнуляется
        /// длиной, а не пересоздаётся: иначе замер записи мерил бы заодно
        /// выделение буфера.
        /// </summary>
        private MemoryStream _sink = null!;

        private DefaultHttpContext _http = null!;

        /// <summary>
        /// Тела запросов - те же документы, что отдают GET-методы, и
        /// приготовлены эталоном. Общие на обе стороны: замер чтения не
        /// должен зависеть от того, кто писал запрос.
        /// </summary>
        private static readonly byte[] OneUtf8 =
            JsonSerializer.SerializeToUtf8Bytes(Payload.One, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        private static readonly byte[] ManyUtf8 =
            JsonSerializer.SerializeToUtf8Bytes(Payload.Many, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        /// <summary>
        /// То же тело, но потоком. Один на все итерации и перематывается, а не
        /// пересоздаётся: замер чтения не должен мерить заодно выделение
        /// четырёхсот килобайт.
        /// </summary>
        private MemoryStream _body = null!;

        /// <summary>То же для одного объекта: он заведомо влезает в первый кусок.</summary>
        private MemoryStream _oneBody = null!;

        /// <summary>
        /// Раковины опций с нарочно изменённым размером буфера. Отдельные, а
        /// не настроенные на месте: опции у эталона после первого же
        /// использования становятся неизменяемыми.
        /// </summary>
        private static readonly JsonSerializerOptions HugeBufferReference = Buffered(1 << 20, false);

        private static readonly JsonSerializerOptions HugeBufferGoddess = Buffered(1 << 20, true);

        private static readonly JsonSerializerOptions TinyBufferReference = Buffered(1024, false);

        private static readonly JsonSerializerOptions TinyBufferGoddess = Buffered(1024, true);

        private JsonSerializerOptions _hugeBufferReference = HugeBufferReference;

        private JsonSerializerOptions _hugeBufferGoddess = HugeBufferGoddess;

        private JsonSerializerOptions _tinyBufferReference = TinyBufferReference;

        private JsonSerializerOptions _tinyBufferGoddess = TinyBufferGoddess;

        private static JsonSerializerOptions Buffered(int size, bool goddess)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultBufferSize = size, };

            return goddess ? Goddess.UseJsonGoddess(options) : options;
        }

        [GlobalSetup]
        public void Setup()
        {
            SetupAsync().GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _goddess.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _reference.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private async Task SetupAsync()
        {
            _reference = await Pipeline.StartAsync(false);
            _goddess = await Pipeline.StartAsync(true);

            _sink = new MemoryStream(1 << 20);
            _body = new MemoryStream(ManyUtf8, writable: false);
            _oneBody = new MemoryStream(OneUtf8, writable: false);
            _http = new DefaultHttpContext();
            _http.Response.Body = _sink;

            await VerifyAsync();
        }

        /// <summary>
        /// Мост обязан отдавать те же байты, что эталон, на каждой ступени.
        /// </summary>
        private async Task VerifyAsync()
        {
            //Сначала - словами: за какие из трёх раковин опций мост берётся.
            //Там, где не берётся, строки таблицы сравнивают эталон с эталоном,
            //и «разница в пределах шума» означает не то, что подумает читатель
            Console.WriteLine("configured (mvc reads with these): " + Goddess.Explain(typeof(Order), _goddess.ConfiguredOptions));
            Console.WriteLine("mvc writes with these:             " + Goddess.Explain(typeof(Order), _goddess.MvcWriteOptions));
            Console.WriteLine("mvc reads with these:              " + Goddess.Explain(typeof(Order), _goddess.MvcReadOptions));
            Console.WriteLine("minimal api:                       " + Goddess.Explain(typeof(Order), _goddess.MinimalApiOptions));
            Console.WriteLine("served types: " + Goddess.ServedTypeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

            //Улика, ради которой строка и печатается. Настроенная раковина и
            //та, которой MVC пишет, различаются ОДНИМ свойством - энкодером, -
            //и его MVC подставляет сам. У minimal API такой же энкодер лежит
            //прямо в свежем Http.Json.JsonOptions, то есть подменять там нечего
            Console.WriteLine(
                "encoder: configured=" + Encoder(_goddess.ConfiguredOptions)
                + ", mvc writes with=" + Encoder(_goddess.MvcWriteOptions)
                + ", minimal=" + Encoder(_goddess.MinimalApiOptions)
                + ", fresh Http.Json=" + Encoder(new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions));

            SameBytes(
                "bare (configured), one",
                Bare(_reference.ConfiguredOptions, Payload.One),
                Bare(_goddess.ConfiguredOptions, Payload.One));
            SameBytes(
                "bare (configured), many",
                Bare(_reference.ConfiguredOptions, Payload.Many),
                Bare(_goddess.ConfiguredOptions, Payload.Many));
            SameBytes(
                "bare (as mvc writes), one",
                Bare(_reference.MvcWriteOptions, Payload.One),
                Bare(_goddess.MvcWriteOptions, Payload.One));
            SameBytes(
                "bare (as mvc writes), many",
                Bare(_reference.MvcWriteOptions, Payload.Many),
                Bare(_goddess.MvcWriteOptions, Payload.Many));

            SameBytes(
                "formatter, one",
                await Formatted(_reference.Formatter, typeof(Order), Payload.One),
                await Formatted(_goddess.Formatter, typeof(Order), Payload.One));
            SameBytes(
                "formatter, many",
                await Formatted(_reference.Formatter, typeof(Order[]), Payload.Many),
                await Formatted(_goddess.Formatter, typeof(Order[]), Payload.Many));

            foreach (var route in new[] { "/mvc/order", "/mvc/orders", "/minimal/order", "/minimal/orders", })
            {
                SameBytes(
                    "pipeline " + route,
                    await _reference.Client.GetByteArrayAsync(route),
                    await _goddess.Client.GetByteArrayAsync(route));
            }

            //Все три раковины - и та, которой MVC читает, и та, которой он
            //пишет, и раковина minimal API. Требовать этого стало можно
            //только с EncoderUtf8Exhauster: до него мост брался за первую и
            //отступал на двух остальных, потому что там релаксированный
            //энкодер
            //Чтение тела запроса - вторая половина работы, и проверяется она
            //не осмотром опций, а настоящим POST'ом через весь конвейер:
            //разбор идёт другой раковиной, и «обслуживается» тут значит
            //«ответ контроллера совпал», а не «резолвер сказал да»
            foreach (var route in new[] { "/mvc/order", "/mvc/orders", })
            {
                SameBytes(
                    "post " + route,
                    await Post(_reference, route),
                    await Post(_goddess, route));
            }

            ServedByTheBridge("configured", _goddess.ConfiguredOptions);
            ServedByTheBridge("mvc read", _goddess.MvcReadOptions);
            ServedByTheBridge("mvc write", _goddess.MvcWriteOptions);
            ServedByTheBridge("minimal api", _goddess.MinimalApiOptions);
        }

        /// <summary>
        /// Мост обязан взяться за <c>Order</c> на <b>каждой</b> раковине
        /// опций, которую строит ASP.NET Core, - и это утверждение, а не
        /// пожелание.
        ///
        /// <para>
        /// Разъедься однажды <c>JsonSerializerDefaults.Web</c> с нашим
        /// веб-профилем, мост отступит; всё останется зелёным, документы -
        /// правильными, и только быстрее ничего не станет. Отличить такой исход
        /// от «мы медленные» по одной таблице нельзя, а молчаливое отступление
        /// - ровно то, против чего весь этот слой и обвешан проверками.
        /// </para>
        /// </summary>
        private static void ServedByTheBridge(string what, JsonSerializerOptions options)
        {
            //Резолвер моста стои́т в цепочке. Одного Explain было бы мало: он
            //отвечает, ВЗЯЛСЯ БЫ ли мост за такие опции, и ответил бы «да» даже
            //там, где UseJsonGoddess забыли позвать
            var installed = false;
            foreach (var resolver in options.TypeInfoResolverChain)
            {
                installed |= resolver.GetType().Namespace == "JsonGoddess.Compat.Interop";
            }

            //...и он действительно забрал тип себе. JsonTypeInfoKind.None
            //означает «типом целиком владеет конвертер»; POCO, построенный
            //резолвером по умолчанию, дал бы Object
            var kind = options.GetTypeInfo(typeof(Order)).Kind;

            if (installed && kind == JsonTypeInfoKind.None)
            {
                return;
            }

            throw new InvalidOperationException(
                "the bridge does not serve Order with the '" + what + "' options of this ASP.NET Core application"
                + " (resolver installed: " + installed + ", type info kind: " + kind + "). "
                + Goddess.Explain(typeof(Order), options)
                + " The staircase would then compare System.Text.Json with itself.");
        }

        private static string Encoder(JsonSerializerOptions options)
        {
            var encoder = options.Encoder;

            if (encoder is null)
            {
                return "<none, i.e. the default>";
            }

            if (ReferenceEquals(encoder, JavaScriptEncoder.Default))
            {
                return "JavaScriptEncoder.Default";
            }

            if (ReferenceEquals(encoder, JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
            {
                return "JavaScriptEncoder.UnsafeRelaxedJsonEscaping";
            }

            return encoder.GetType().FullName ?? "<unknown>";
        }

        private byte[] Bare(JsonSerializerOptions options, object value)
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, value, value.GetType(), options);
            return _sink.ToArray();
        }

        private async Task<byte[]> Formatted(SystemTextJsonOutputFormatter formatter, Type type, object value)
        {
            _sink.SetLength(0);
            await formatter.WriteAsync(Context(type, value));
            return _sink.ToArray();
        }

        private static void SameBytes(string what, byte[] theirs, byte[] ours)
        {
            if (theirs.Length == ours.Length && new ReadOnlySpan<byte>(theirs).SequenceEqual(ours))
            {
                return;
            }

            throw new InvalidOperationException(
                what + ": documents differ (" + theirs.Length + " vs " + ours.Length + " bytes): "
                + Encoding.UTF8.GetString(theirs) + " / " + Encoding.UTF8.GetString(ours));
        }

        private OutputFormatterWriteContext Context(Type type, object value)
        {
            return new OutputFormatterWriteContext(
                _http,
                (stream, encoding) => new StreamWriter(stream, encoding),
                type,
                value);
        }

        // --- ступень 1: голая сериализация настроенной раковиной -------------
        //
        // Той самой, которую потребитель отдал в AddJsonOptions. Ею MVC читает
        // тело запроса - и ею же писал бы ответ, не подмени он энкодер.

        [BenchmarkCategory("one: bare, configured")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public long BareOneReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _reference.ConfiguredOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("one: bare, configured")]
        [Benchmark(Description = "+ JsonGoddess")]
        public long BareOneGoddess()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _goddess.ConfiguredOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many: bare, configured")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public long BareManyReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _reference.ConfiguredOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many: bare, configured")]
        [Benchmark(Description = "+ JsonGoddess")]
        public long BareManyGoddess()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _goddess.ConfiguredOptions);
            return _sink.Length;
        }

        // --- ступень 2: то же самое, но раковиной, которой ответ пишется ------
        //
        // Отличается от ступени 1 ровно одним свойством - энкодером, - и
        // ставит его не потребитель, а MVC. Здесь мост отступает, и обе колонки
        // считает эталон. Ступень стои́т в таблице затем, чтобы цена этого
        // одного свойства была видна числом.

        [BenchmarkCategory("one: bare, as mvc writes")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public long BareWriteOneReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _reference.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("one: bare, as mvc writes")]
        [Benchmark(Description = "+ JsonGoddess")]
        public long BareWriteOneGoddess()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.One, _goddess.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many: bare, as mvc writes")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public long BareWriteManyReference()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _reference.MvcWriteOptions);
            return _sink.Length;
        }

        [BenchmarkCategory("many: bare, as mvc writes")]
        [Benchmark(Description = "+ JsonGoddess")]
        public long BareWriteManyGoddess()
        {
            _sink.SetLength(0);
            JsonSerializer.Serialize(_sink, Payload.Many, _goddess.MvcWriteOptions);
            return _sink.Length;
        }

        // --- ступень 3: форматтер MVC ----------------------------------------

        [BenchmarkCategory("one: formatter")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<long> FormatterOneReference()
        {
            _sink.SetLength(0);
            await _reference.Formatter.WriteAsync(Context(typeof(Order), Payload.One));
            return _sink.Length;
        }

        [BenchmarkCategory("one: formatter")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<long> FormatterOneGoddess()
        {
            _sink.SetLength(0);
            await _goddess.Formatter.WriteAsync(Context(typeof(Order), Payload.One));
            return _sink.Length;
        }


        [BenchmarkCategory("many: formatter")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<long> FormatterManyReference()
        {
            _sink.SetLength(0);
            await _reference.Formatter.WriteAsync(Context(typeof(Order[]), Payload.Many));
            return _sink.Length;
        }

        [BenchmarkCategory("many: formatter")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<long> FormatterManyGoddess()
        {
            _sink.SetLength(0);
            await _goddess.Formatter.WriteAsync(Context(typeof(Order[]), Payload.Many));
            return _sink.Length;
        }


        // --- обратная сторона: чтение тела запроса ---------------------------
        //
        // Раковина здесь третья - настроенная, без подменённого энкодера, -
        // и путь внутри эталона другой. Мерить запись и называть результат
        // «мостом в ASP.NET» значило бы проверить половину и выдать её за
        // целое.

        [BenchmarkCategory("one: bare read")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public int BareReadOneReference()
        {
            return JsonSerializer.Deserialize<Order>(OneUtf8, _reference.MvcReadOptions)!.Id;
        }

        [BenchmarkCategory("one: bare read")]
        [Benchmark(Description = "+ JsonGoddess")]
        public int BareReadOneGoddess()
        {
            return JsonSerializer.Deserialize<Order>(OneUtf8, _goddess.MvcReadOptions)!.Id;
        }

        [BenchmarkCategory("many: bare read")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public int BareReadManyReference()
        {
            return JsonSerializer.Deserialize<Order[]>(ManyUtf8, _reference.MvcReadOptions)!.Length;
        }

        [BenchmarkCategory("many: bare read")]
        [Benchmark(Description = "+ JsonGoddess")]
        public int BareReadManyGoddess()
        {
            return JsonSerializer.Deserialize<Order[]>(ManyUtf8, _goddess.MvcReadOptions)!.Length;
        }

        /// <summary>
        /// То же чтение, но из <b>потока</b>, а не из готового массива байт.
        ///
        /// <para>
        /// Ступень заведена по случаю: голое чтение ускоряется, а <c>POST</c>
        /// через конвейер - нет. Различить «мост не позвали» и «мост позвали,
        /// но выигрыша здесь нет» можно только так. Эталон читает поток
        /// кусками по 16 КБ, то есть его <c>Utf8JsonReader</c> оказывается
        /// многосегментным, - а у моста на многосегментном читателе выигрыша
        /// не было и на стенде (PLAN.md §12.8): токен, разрезанный границей
        /// куска, приезжает без <c>ValueSpan</c>, и быстрый путь имён
        /// выключается.
        /// </para>
        /// </summary>
        [BenchmarkCategory("many: stream read")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<int> StreamReadManyReference()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _reference.MvcReadOptions);
            return orders!.Length;
        }

        [BenchmarkCategory("many: stream read")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<int> StreamReadManyGoddess()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _goddess.MvcReadOptions);
            return orders!.Length;
        }

        /// <summary>
        /// Один объект из потока - и это <b>различающий</b> замер, а не ещё
        /// одна строка для полноты.
        ///
        /// <para>
        /// Четыреста байт помещаются в первый же кусок, который читает
        /// <c>DeserializeAsync</c> (по умолчанию 16 КБ), то есть разрезанных
        /// токенов здесь нет <b>ни одного</b>. Если потеря на потоке объясняется
        /// разрезанными именами, то эта строка обязана дать то же отношение,
        /// что и чтение из массива байт. Если не даст - значит платим мы не за
        /// сегментацию, а за саму асинхронную машинерию вокруг конвертера, и
        /// объяснение надо менять.
        /// </para>
        /// </summary>
        [BenchmarkCategory("one: stream read")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<int> StreamReadOneReference()
        {
            _oneBody.Position = 0;
            var order = await JsonSerializer.DeserializeAsync<Order>(_oneBody, _reference.MvcReadOptions);
            return order!.Id;
        }

        [BenchmarkCategory("one: stream read")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<int> StreamReadOneGoddess()
        {
            _oneBody.Position = 0;
            var order = await JsonSerializer.DeserializeAsync<Order>(_oneBody, _goddess.MvcReadOptions);
            return order!.Id;
        }

        /// <summary>
        /// Тот же массив из потока, но буфером <b>больше всего тела</b>:
        /// границ кусков нет ни одной.
        ///
        /// <para>
        /// Вторая половина различающего опыта. Если потеря на потоке - это
        /// разрезанные токены, здесь она обязана исчезнуть и вернуть 0.67.
        /// Если останется - платим мы не за границы, и объяснение надо искать
        /// в другом месте.
        /// </para>
        /// </summary>
        [BenchmarkCategory("many: stream read, huge buffer")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<int> HugeBufferReadManyReference()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _hugeBufferReference);
            return orders!.Length;
        }

        [BenchmarkCategory("many: stream read, huge buffer")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<int> HugeBufferReadManyGoddess()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _hugeBufferGoddess);
            return orders!.Length;
        }

        /// <summary>
        /// И наоборот - буфер в килобайт: границ становится вчетверо сотен
        /// вместо двух десятков. Если дело в них, здесь обязано стать заметно
        /// хуже.
        /// </summary>
        [BenchmarkCategory("many: stream read, tiny buffer")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<int> TinyBufferReadManyReference()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _tinyBufferReference);
            return orders!.Length;
        }

        [BenchmarkCategory("many: stream read, tiny buffer")]
        [Benchmark(Description = "+ JsonGoddess")]
        public async Task<int> TinyBufferReadManyGoddess()
        {
            _body.Position = 0;
            var orders = await JsonSerializer.DeserializeAsync<Order[]>(_body, _tinyBufferGoddess);
            return orders!.Length;
        }

        [BenchmarkCategory("many: mvc post")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Task<byte[]> PostManyReference() => Post(_reference, "/mvc/orders");

        [BenchmarkCategory("many: mvc post")]
        [Benchmark(Description = "+ JsonGoddess")]
        public Task<byte[]> PostManyGoddess() => Post(_goddess, "/mvc/orders");

        // --- ступень 4: весь конвейер, MVC -----------------------------------

        [BenchmarkCategory("one: mvc request")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Task<long> MvcOneReference() => Request(_reference, "/mvc/order");

        [BenchmarkCategory("one: mvc request")]
        [Benchmark(Description = "+ JsonGoddess")]
        public Task<long> MvcOneGoddess() => Request(_goddess, "/mvc/order");

        [BenchmarkCategory("many: mvc request")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Task<long> MvcManyReference() => Request(_reference, "/mvc/orders");

        [BenchmarkCategory("many: mvc request")]
        [Benchmark(Description = "+ JsonGoddess")]
        public Task<long> MvcManyGoddess() => Request(_goddess, "/mvc/orders");


        // --- рядом с четвёртой: тот же конвейер, но minimal API --------------

        [BenchmarkCategory("one: minimal api")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Task<long> MinimalOneReference() => Request(_reference, "/minimal/order");

        [BenchmarkCategory("one: minimal api")]
        [Benchmark(Description = "+ JsonGoddess")]
        public Task<long> MinimalOneGoddess() => Request(_goddess, "/minimal/order");

        [BenchmarkCategory("many: minimal api")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Task<long> MinimalManyReference() => Request(_reference, "/minimal/orders");

        [BenchmarkCategory("many: minimal api")]
        [Benchmark(Description = "+ JsonGoddess")]
        public Task<long> MinimalManyGoddess() => Request(_goddess, "/minimal/orders");


        /// <summary>
        /// Запрос целиком: заголовки, тело, до последнего байта.
        ///
        /// <para>
        /// Тело сливается в тот же приёмник, что и у нижних ступеней, а не
        /// берётся <c>GetByteArrayAsync</c>. Четыреста килобайт на каждой
        /// итерации - это массив в LOH и сборка Gen2, то есть замер клиентской
        /// аллокации пополам с замером конвейера. Расход этот одинаков у обеих
        /// сторон и потому в отношении сокращается, но разброс от него
        /// остаётся, а разброс здесь и есть то, сквозь что надо разглядеть
        /// ответ.
        /// </para>
        /// </summary>
        /// <summary>
        /// Отправить документ и получить ответ контроллера.
        ///
        /// <para>
        /// Тело готовится <b>эталоном</b> и одно на обе стороны: мерить и
        /// сравнивать надо разбор, а не то, кто быстрее составил запрос.
        /// </para>
        /// </summary>
        private static async Task<byte[]> Post(Pipeline pipeline, string route)
        {
            var body = route.EndsWith("orders", StringComparison.Ordinal) ? ManyUtf8 : OneUtf8;

            using (var content = new ByteArrayContent(body))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using (var response = await pipeline.Client.PostAsync(route, content))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
        }

        private async Task<long> Request(Pipeline pipeline, string route)
        {
            using (var response = await pipeline.Client.GetAsync(route, HttpCompletionOption.ResponseHeadersRead))
            {
                _sink.SetLength(0);
                await response.Content.CopyToAsync(_sink);
                return _sink.Length;
            }
        }
    }
}
