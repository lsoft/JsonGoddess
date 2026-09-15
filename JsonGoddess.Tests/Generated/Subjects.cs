using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Плоский POCO со <b>всеми</b> builtin'ами, которые умеют sink'и, - тот
    /// самый критерий готовности фазы 2. Ни одного типа сверх того, что есть в
    /// <c>IExhauster</c>/<c>IInjector</c>: остальные должны отвергаться
    /// генератором, и это проверяется отдельно.
    ///
    /// Имена намеренно разной длины: на этой форме замер отдал победу цепочке
    /// сравнений, и именно её здесь и порождает эмиттер.
    /// </summary>
    public class Flat
    {
        public bool Flag { get; set; }
        public sbyte Tiny { get; set; }
        public byte Octet { get; set; }
        public short Small { get; set; }
        public ushort Positive { get; set; }
        public int Id { get; set; }
        public uint Ordinal { get; set; }
        public long Big { get; set; }
        public ulong Huge { get; set; }
        public float Ratio { get; set; }
        public double Precise { get; set; }
        public decimal Total { get; set; }
        public char Marker { get; set; }
        public string? Customer { get; set; }
        public DateTime Created { get; set; }
        public DateTimeOffset Stamped { get; set; }
        public TimeSpan Elapsed { get; set; }
        public Guid Reference { get; set; }
        public byte[]? Payload { get; set; }

        public int? MaybeCount { get; set; }
        public Guid? MaybeReference { get; set; }
        public DateTime? MaybeMoment { get; set; }

        public static Flat CreateSample()
        {
            return new Flat
            {
                Flag = true,
                Tiny = -42,
                Octet = 200,
                Small = -30000,
                Positive = 60000,
                Id = 123456,
                Ordinal = 4000000000,
                Big = -9007199254740993L,
                Huge = 18446744073709551615UL,
                Ratio = 0.1f,
                Precise = 1.0 / 3.0,
                Total = 129.99m,
                Marker = 'X',
                Customer = "Ada \"the first\" Lovelace\nand a tab\there",
                Created = new DateTime(2026, 9, 14, 3, 4, 5, DateTimeKind.Utc),
                Stamped = new DateTimeOffset(2026, 9, 14, 3, 4, 5, TimeSpan.FromHours(3)),
                Elapsed = new TimeSpan(1, 2, 3, 4, 5),
                Reference = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
                Payload = new byte[] { 1, 2, 3, 250, 251, 252, },
                MaybeCount = 7,
                MaybeReference = null,
                MaybeMoment = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
            };
        }
    }

    /// <summary>
    /// Тридцать имён ровно по семь байт: одна корзина, длина не отсекает
    /// никого, и эмиттер обязан выбрать switch по ключу. Семь байт - случай,
    /// в котором ключ полон и сравнения спанов не остаётся вовсе.
    /// </summary>
    public class Wide
    {
        public int Field00 { get; set; }
        public int Field01 { get; set; }
        public int Field02 { get; set; }
        public int Field03 { get; set; }
        public int Field04 { get; set; }
        public int Field05 { get; set; }
        public int Field06 { get; set; }
        public int Field07 { get; set; }
        public int Field08 { get; set; }
        public int Field09 { get; set; }

        public static Wide CreateSample()
        {
            return new Wide
            {
                Field00 = 0, Field01 = 11, Field02 = 22, Field03 = 33, Field04 = 44,
                Field05 = 55, Field06 = 66, Field07 = 77, Field08 = 88, Field09 = 99,
            };
        }
    }

    /// <summary>
    /// Четыре имени по десять байт с общими первыми семью: ключ у всех один,
    /// и разделить их может только сравнение внутри общей ветки.
    /// </summary>
    public class Colliding
    {
        public int ReferenceA { get; set; }
        public int ReferenceB { get; set; }
        public int ReferenceC { get; set; }
        public int ReferenceD { get; set; }

        public static Colliding CreateSample()
        {
            return new Colliding { ReferenceA = 1, ReferenceB = 2, ReferenceC = 3, ReferenceD = 4, };
        }
    }

    /// <summary>
    /// Переименование и исключение членов: и то, и другое существует ради
    /// того, чтобы документ совпал с тем, что напишет System.Text.Json.
    /// </summary>
    public class Renamed
    {
        [JsonPropertyName("id")]
        public int Identifier { get; set; }

        [JsonPropertyName("full_name")]
        public string? Name { get; set; }

        [JsonIgnore]
        public string? Secret { get; set; }

        public static Renamed CreateSample()
        {
            return new Renamed { Identifier = 5, Name = "Grace Hopper", Secret = "not in the document", };
        }
    }

    /// <summary>
    /// Имя свойства вне ASCII. Экранирования в JSON оно не требует, поэтому
    /// генератор его принимает; но энкодер <c>System.Text.Json</c> по умолчанию
    /// всё равно развернёт его в <c>\uXXXX</c> - и вот тогда документ приедет в
    /// форме, которую наш диспетчер не опознаёт.
    /// </summary>
    public class Unicode
    {
        [JsonPropertyName("имя")]
        public string? Name { get; set; }

        public static Unicode CreateSample()
        {
            return new Unicode { Name = "Ada", };
        }
    }

    /// <summary>
    /// Составные типы фазы 4 в одном месте: вложенный субъект, коллекция
    /// субъектов, массив builtin'ов, коллекция строк и коллекция коллекций.
    ///
    /// <c>byte[]</c> здесь же и намеренно: он единственный массив, который
    /// обязан остаться base64-строкой, а не превратиться в список чисел, - и
    /// отличить одно от другого может только документ, сверенный с эталоном.
    /// </summary>
    public class Basket
    {
        public string? Owner { get; set; }
        public Item? Head { get; set; }
        public List<Item>? Lines { get; set; }
        public int[]? Numbers { get; set; }
        public List<string>? Tags { get; set; }
        public List<List<int>>? Matrix { get; set; }
        public byte[]? Payload { get; set; }

        public static Basket CreateSample()
        {
            return new Basket
            {
                Owner = "Acme",
                Head = new Item { Sku = "BRK-0001", Quantity = 4, Price = 129.99m, Note = "back order", },
                Lines = new List<Item>
                {
                    new Item { Sku = "CLM-0342", Quantity = 1, Price = 899.00m, Note = null, },
                    new Item { Sku = "WSH-9910", Quantity = 32, Price = 2.50m, Note = "bulk pack", },
                },
                Numbers = new[] { 1, 2, 3, 4, 5, },
                Tags = new List<string> { "urgent", "b2b", },
                Matrix = new List<List<int>>
                {
                    new List<int> { 1, 2, },
                    new List<int>(),
                },
                Payload = new byte[] { 1, 2, 3, 250, 251, 252, },
            };
        }
    }

    public class Item
    {
        public string? Sku { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>
    /// Тип, ссылающийся сам на себя через коллекцию: читатель и писатель
    /// получаются взаимно рекурсивными, и никакого особого случая для этого в
    /// генераторе быть не должно.
    /// </summary>
    public class Node
    {
        public int Id { get; set; }
        public List<Node>? Children { get; set; }

        public static Node CreateSample()
        {
            return new Node
            {
                Id = 1,
                Children = new List<Node>
                {
                    new Node { Id = 2, Children = new List<Node>(), },
                    new Node { Id = 3, Children = new List<Node> { new Node { Id = 4, }, }, },
                },
            };
        }
    }

    public class BaseEntity
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    public class MiddleEntity : BaseEntity
    {
        public int M { get; set; }
    }

    /// <summary>
    /// Три уровня наследования. Порядок членов в документе - предмет
    /// отдельного утверждения: <c>System.Text.Json</c> печатает самый
    /// производный тип первым, и совпадение здесь проверяется не с нашим
    /// представлением о правильном, а с его документом.
    /// </summary>
    public class DerivedEntity : MiddleEntity
    {
        public int Z { get; set; }

        public static DerivedEntity CreateSample()
        {
            return new DerivedEntity { A = 1, B = 2, M = 3, Z = 4, };
        }
    }

    /// <summary>
    /// Коллекция без setter'а. Соблазн «раз объект уже есть, наполним его»
    /// здесь обманчив: <c>System.Text.Json</c> с опциями по умолчанию такой
    /// член на чтении <b>пропускает</b> - наполнение существующей коллекции у
    /// него opt-in (<c>JsonObjectCreationHandling.Populate</c>). Пропускаем и
    /// мы, и это утверждение проверяется прогоном эталона, а не памятью о том,
    /// как он устроен.
    /// </summary>
    public class Fixed
    {
        public List<int> Tags { get; } = new List<int>();
        public int Scalar { get; } = 7;
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Fixed), true)]
    public partial class FixedSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Basket), true)]
    [JsonSubject(typeof(Item), false)]
    public partial class BasketSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Node), true)]
    public partial class NodeSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(DerivedEntity), true)]
    public partial class DerivedEntitySerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Flat), true)]
    public partial class FlatSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Unicode), true)]
    public partial class UnicodeSerializer
    {
    }

    /// <summary>
    /// Имя свойства вне BMP: четыре байта UTF-8, которые в экранированной
    /// форме приезжают суррогатной парой из двух <c>\uXXXX</c>.
    /// </summary>
    public class Astral
    {
        [JsonPropertyName("\U0001F600")]
        public string? Name { get; set; }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Astral), true)]
    public partial class AstralSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Wide), true)]
    public partial class WideSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Colliding), true)]
    public partial class CollidingSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Renamed), true)]
    public partial class RenamedSerializer
    {
    }

    /// <summary>
    /// Хост без зарегистрированных sink'ов: перегрузка порождается под базовые
    /// классы, и это должно работать так же, только через виртуальный вызов.
    /// </summary>
    [JsonSubject(typeof(Renamed), true)]
    public partial class RenamedBaseSerializer
    {
    }
}
