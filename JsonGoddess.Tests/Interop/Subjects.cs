using System;
using System.Collections.Generic;

namespace JsonGoddess.Tests.Interop
{
    /// <summary>
    /// POCO, которых не хватало среди уже существующих: они добавлены под
    /// дыры в матрице харнесса, а не продублированы ради круглого числа.
    /// Всё, что уже описано субъектами из <c>Generated</c>, харнесс
    /// переиспользует.
    /// </summary>
    public class Extremes
    {
        public sbyte SByteMin { get; set; }
        public sbyte SByteMax { get; set; }
        public byte ByteMax { get; set; }
        public short ShortMin { get; set; }
        public short ShortMax { get; set; }
        public ushort UShortMax { get; set; }
        public int IntMin { get; set; }
        public int IntMax { get; set; }
        public uint UIntMax { get; set; }
        public long LongMin { get; set; }
        public long LongMax { get; set; }
        public ulong ULongMax { get; set; }
        public decimal DecimalMin { get; set; }
        public decimal DecimalMax { get; set; }
        public double DoubleMax { get; set; }
        public double DoubleEpsilon { get; set; }
        public double Third { get; set; }
        public float FloatMax { get; set; }
        public float FloatTenth { get; set; }

        /// <summary>
        /// Границы подлежащих типов и три числа, на которых видно, что
        /// форматирование кратчайшее и обратимое, а не «как получилось».
        /// </summary>
        public static Extremes CreateSample()
        {
            return new Extremes
            {
                SByteMin = sbyte.MinValue,
                SByteMax = sbyte.MaxValue,
                ByteMax = byte.MaxValue,
                ShortMin = short.MinValue,
                ShortMax = short.MaxValue,
                UShortMax = ushort.MaxValue,
                IntMin = int.MinValue,
                IntMax = int.MaxValue,
                UIntMax = uint.MaxValue,
                LongMin = long.MinValue,
                LongMax = long.MaxValue,
                ULongMax = ulong.MaxValue,
                DecimalMin = decimal.MinValue,
                DecimalMax = decimal.MaxValue,
                DoubleMax = double.MaxValue,
                DoubleEpsilon = double.Epsilon,
                Third = 1.0 / 3.0,
                FloatMax = float.MaxValue,
                FloatTenth = 0.1f,
            };
        }
    }

    /// <summary>
    /// Строки во всех видах, которые требуют решения на записи: то, что
    /// экранировать обязаны по RFC 8259 §7, то, что экранирует энкодер эталона
    /// по умолчанию, и то, что не экранирует никто.
    /// </summary>
    public class Escapes
    {
        public string? Quote { get; set; }
        public string? Backslash { get; set; }
        public string? Control { get; set; }
        public string? Newline { get; set; }
        public string? Htmlish { get; set; }
        public string? NonAscii { get; set; }
        public string? Astral { get; set; }
        public string? Empty { get; set; }
        public string? Null { get; set; }

        public static Escapes CreateSample()
        {
            return new Escapes
            {
                Quote = "she said \"no\"",
                Backslash = "C:\\path\\to",
                Control = "bell\u0007 and null\u0000 and unit\u001F",
                Newline = "line\r\nnext\ttabbed",
                Htmlish = "<a href='x'>&amp;</a>",
                NonAscii = "Ада Лавлейс — первая",

                //вне BMP - отдельная форма харнесса: там документы обязаны
                //разойтись, и смешивать это с остальным экранированием значило
                //бы утопить одно утверждение в другом
                Astral = null,
                Empty = string.Empty,
                Null = null,
            };
        }
    }

    /// <summary>
    /// Даты и длительности. Лексика у них своя у каждой, и расхождение здесь
    /// незаметно глазом: <c>DateTimeKind</c> меняет суффикс, а не значение.
    /// </summary>
    public class Moments
    {
        public DateTime Utc { get; set; }
        public DateTime Local { get; set; }
        public DateTime Unspecified { get; set; }
        public DateTime MinValue { get; set; }
        public DateTime MaxValue { get; set; }
        public DateTimeOffset Positive { get; set; }
        public DateTimeOffset Negative { get; set; }
        public DateTimeOffset Zero { get; set; }
        public TimeSpan Long { get; set; }
        public TimeSpan Negative2 { get; set; }
        public TimeSpan Zero2 { get; set; }
        public Guid Empty { get; set; }
        public Guid Reference { get; set; }

        public static Moments CreateSample()
        {
            return new Moments
            {
                Utc = new DateTime(2026, 9, 15, 3, 4, 5, DateTimeKind.Utc),
                Local = new DateTime(2026, 9, 15, 3, 4, 5, DateTimeKind.Local),
                Unspecified = new DateTime(2026, 9, 15, 3, 4, 5, DateTimeKind.Unspecified),
                MinValue = DateTime.MinValue,
                MaxValue = DateTime.MaxValue,
                Positive = new DateTimeOffset(2026, 9, 15, 3, 4, 5, TimeSpan.FromHours(5.5)),
                Negative = new DateTimeOffset(2026, 9, 15, 3, 4, 5, TimeSpan.FromHours(-8)),
                Zero = new DateTimeOffset(2026, 9, 15, 3, 4, 5, TimeSpan.Zero),
                Long = new TimeSpan(10675199, 2, 48, 5, 477),
                Negative2 = new TimeSpan(-1, -2, -3, -4, -5),
                Zero2 = TimeSpan.Zero,
                Empty = Guid.Empty,
                Reference = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
            };
        }
    }

    /// <summary>
    /// Поля, а не свойства: <c>System.Text.Json</c> берёт их только с
    /// <c>[JsonInclude]</c>, и повторить это надо в точности - иначе документ
    /// отличается составом, а не порядком.
    /// </summary>
    public class Fields
    {
        [System.Text.Json.Serialization.JsonInclude]
        public int Included;

        public int Bare;

        [System.Text.Json.Serialization.JsonInclude]
        public List<int>? IncludedList;

        public int Property { get; set; }

        public static Fields CreateSample()
        {
            return new Fields
            {
                Included = 1,
                Bare = 2,
                IncludedList = new List<int> { 3, 4, },
                Property = 5,
            };
        }
    }

    /// <summary>
    /// Имена членов, на которых политика именования нетривиальна: пробег
    /// заглавных, цифра внутри, аббревиатура целиком и явное переименование,
    /// которое политика трогать не должна.
    /// </summary>
    public class Named
    {
        public int OrderId { get; set; }
        public string? CustomerName { get; set; }
        public int HTTPResponseCode { get; set; }
        public int X509Certificate { get; set; }
        public int ID { get; set; }
        public Dictionary<string, int>? SomeMap { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("kept-as-is")]
        public int Explicit { get; set; }

        public static Named CreateSample()
        {
            return new Named
            {
                OrderId = 1,
                CustomerName = "Acme",
                HTTPResponseCode = 200,
                X509Certificate = 2,
                ID = 3,
                SomeMap = new Dictionary<string, int> { { "SomeKey", 4 }, { "already_snake", 5 }, },
                Explicit = 6,
            };
        }
    }

    /// <summary>
    /// Настройки читаются из <b>их</b> атрибута, а не из своего: у эталона тут
    /// та же задача - настроить генератор, у которого нет объекта опций, - и
    /// он её уже решил.
    /// </summary>
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Named), true)]
    public partial class CamelSerializer
    {
    }

    /// <summary>
    /// Ключи словаря - отдельная политика, и она применяется <b>только на
    /// записи</b>: на чтении ключ приезжает как есть, обратного преобразования
    /// эталон не делает. Проверено прогоном.
    /// </summary>
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Named), true)]
    public partial class SnakeSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Extremes), true)]
    [JsonSubject(typeof(Escapes), true)]
    [JsonSubject(typeof(Moments), true)]
    [JsonSubject(typeof(Fields), true)]
    public partial class InteropSerializer
    {
    }
}
