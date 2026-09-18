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
    /// Условно опускаемые члены. Здесь ломается инвариант фазы 2 «разделители -
    /// константы»: пропуск члена меняет разметку соседей, чего в XML не бывает
    /// вовсе.
    ///
    /// Безусловные члены стоят и до, и после условных намеренно: эмиттер обязан
    /// платить за <c>needComma</c> только на том участке, где он действительно
    /// нужен, а не на всём методе.
    /// </summary>
    public class Sparse
    {
        public int First { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Count { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Guid Reference { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public List<int>? Values { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int? Maybe { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Always { get; set; }

        public int Last { get; set; }

        public static Sparse CreateEmpty() => new Sparse();

        public static Sparse CreateFull()
        {
            return new Sparse
            {
                First = 1,
                Name = "Ada",
                Count = 2,
                Reference = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
                Values = new List<int>(),
                Maybe = 0,
                Always = "here",
                Last = 3,
            };
        }
    }

    /// <summary>
    /// Все члены условные: фигурная скобка перестаёт склеиваться с именем
    /// первого члена, а документ может оказаться пустым объектом.
    /// </summary>
    public class AllSparse
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? A { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? B { get; set; }
    }

    /// <summary>
    /// Порядок членов задан явно и поверх иерархии. Эталон сортирует устойчиво
    /// и уже <b>после</b> того, как разложил наследование от производного к
    /// базовому.
    /// </summary>
    public class OrderedBase
    {
        public int BaseA { get; set; }

        [JsonPropertyOrder(-5)]
        public int BaseEarly { get; set; }
    }

    public class OrderedDerived : OrderedBase
    {
        public int DerivedA { get; set; }

        [JsonPropertyOrder(5)]
        public int Late { get; set; }

        [JsonPropertyOrder(5)]
        public int LateToo { get; set; }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Sparse), true)]
    [JsonSubject(typeof(AllSparse), true)]
    [JsonSubject(typeof(OrderedDerived), true)]
    public partial class SparseSerializer
    {
    }

    /// <summary>
    /// Enum по умолчанию - число подлежащего типа, и значение вне набора
    /// проезжает как есть. Так ведёт себя эталон, и отказывать здесь значило бы
    /// не прочитать документ, который он пишет.
    /// </summary>
    public enum Status
    {
        Draft,
        Sent,
        Archived = 40,
    }

    /// <summary>Подлежащий тип не <c>int</c>: значение обязано доехать целым.</summary>
    public enum Huge : ulong
    {
        Max = 18446744073709551615UL,
    }

    /// <summary>
    /// Строковый режим. Маркер - конвертер на самом типе; на члене конвертер мы
    /// отвергаем, потому что повторить произвольный конвертер нельзя.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Mode
    {
        Draft,
        Sent,

        [JsonStringEnumMemberName("sent-out")]
        Forwarded,
    }

    public class Marks
    {
        public Status Plain { get; set; }
        public Status? Maybe { get; set; }
        public Huge Big { get; set; }
        public Mode Mode { get; set; }
        public Mode? MaybeMode { get; set; }
        public List<Mode>? Modes { get; set; }
        public Dictionary<string, Status>? States { get; set; }

        public static Marks CreateSample()
        {
            return new Marks
            {
                Plain = Status.Archived,
                Maybe = null,
                Big = Huge.Max,
                Mode = Mode.Forwarded,
                MaybeMode = Mode.Sent,
                Modes = new List<Mode> { Mode.Draft, Mode.Forwarded, },
                States = new Dictionary<string, Status> { { "a", Status.Sent }, },
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Marks), true)]
    public partial class MarksSerializer
    {
    }

    /// <summary>
    /// Словари. Ключ здесь - не константа этапа компиляции, и этим он
    /// принципиально отличается от имени члена: он может требовать
    /// экранирования, приезжать экранированным и повторяться.
    ///
    /// В образце ключи намеренно неудобные: кириллица, кавычка внутри, пустая
    /// строка. Все три законны, и все три эталон пишет по-своему.
    /// </summary>
    public class Catalogue
    {
        public Dictionary<string, int>? Counts { get; set; }
        public Dictionary<string, Item>? Items { get; set; }
        public Dictionary<string, List<int>>? Series { get; set; }
        public Dictionary<string, Dictionary<string, string>>? Nested { get; set; }

        public static Catalogue CreateSample()
        {
            return new Catalogue
            {
                Counts = new Dictionary<string, int>
                {
                    { "b", 2 },
                    { "a", 1 },
                    { "имя", 3 },
                    { "a\"b", 4 },
                    { "", 5 },
                    { "tab\there", 6 },
                },
                Items = new Dictionary<string, Item>
                {
                    { "head", new Item { Sku = "BRK-0001", Quantity = 4, Price = 129.99m, Note = "back order", } },
                    { "missing", null! },
                },
                Series = new Dictionary<string, List<int>>
                {
                    { "x", new List<int> { 1, 2, } },
                    { "empty", new List<int>() },
                    { "none", null! },
                },
                Nested = new Dictionary<string, Dictionary<string, string>>
                {
                    { "outer", new Dictionary<string, string> { { "inner", "value" }, } },
                },
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Catalogue), true)]
    [JsonSubject(typeof(Item), false)]
    public partial class CatalogueSerializer
    {
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

    /// <summary>
    /// Структура-субъект. Три её члена подобраны по тому, что у структуры
    /// решается иначе, чем у класса:
    ///
    /// <list type="bullet">
    /// <item>обычное свойство - чтобы было что читать;</item>
    /// <item><c>[JsonInclude]</c>-поле - у структур поля идиоматичнее, чем у
    /// классов, и порядок «свойства раньше полей» обязан соблюдаться здесь
    /// так же (проверено прогоном эталона);</item>
    /// <item>get-only свойство - эталон его <b>пишет</b> и молча роняет на
    /// чтении; повторяется в точности.</item>
    /// </list>
    /// </summary>
    public struct Coordinate
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Sum => X + Y;

        [JsonInclude]
        public string? Label;

        public static Coordinate CreateSample()
        {
            return new Coordinate { X = 3, Y = 4, Label = "corner", };
        }
    }

    /// <summary>
    /// Позиционная <c>record struct</c>. Отдельного кода ей не нужно: у неё
    /// есть и конструктор без параметров (он есть у любой структуры), и
    /// setter'ы у позиционных свойств, - но убедиться в этом надо прогоном, а
    /// не рассуждением.
    /// </summary>
    public record struct Segment(int From, int To);

    /// <summary>
    /// Структура на месте члена во всех трёх видах: сама, под
    /// <c>Nullable&lt;&gt;</c> и внутри коллекций. Именно здесь ветка на
    /// <c>null</c> отличается от классовой: <c>Write_</c> у структуры
    /// принимает не-nullable, и снимать <c>null</c> приходится на месте члена.
    /// </summary>
    public class Route
    {
        public Coordinate Start { get; set; }

        public Coordinate? Finish { get; set; }

        public List<Coordinate>? Waypoints { get; set; }

        public Coordinate[]? Corners { get; set; }

        public Dictionary<string, Coordinate>? Named { get; set; }

        public Segment Leg { get; set; }

        public static Route CreateSample()
        {
            return new Route
            {
                Start = Coordinate.CreateSample(),
                Finish = null,
                Waypoints = new List<Coordinate> { Coordinate.CreateSample(), new Coordinate { X = 1, }, },
                Corners = new[] { new Coordinate { Y = 2, }, },
                Named = new Dictionary<string, Coordinate> { { "home", new Coordinate { X = 9, } }, },
                Leg = new Segment(1, 2),
            };
        }

        /// <summary>
        /// Второй образец: тот же тип, но <c>Nullable</c> заполнен, а коллекции
        /// пусты. Форма документа у него другая, и проверять надо обе.
        /// </summary>
        public static Route CreateFilled()
        {
            return new Route
            {
                Start = new Coordinate(),
                Finish = Coordinate.CreateSample(),
                Waypoints = new List<Coordinate>(),
                Corners = new Coordinate[0],
                Named = new Dictionary<string, Coordinate>(),
                Leg = new Segment(0, 0),
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Coordinate), true)]
    public partial class CoordinateSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Segment), true)]
    public partial class SegmentSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Route), true)]
    [JsonSubject(typeof(Coordinate), false)]
    [JsonSubject(typeof(Segment), false)]
    public partial class RouteSerializer
    {
    }

    /// <summary>
    /// Единственный параметризованный конструктор. Члены подобраны по тому, что
    /// в отложенной форме читателя решается по-разному:
    ///
    /// <list type="bullet">
    /// <item><c>Id</c> - аргумент без умолчания;</item>
    /// <item><c>Tier</c> - аргумент <b>с</b> умолчанием: его отсутствие в
    /// документе обязано дать 42, а не ноль;</item>
    /// <item><c>Tags</c> - аргумент составного типа;</item>
    /// <item><c>Note</c> - обычный член с инициализатором: его отсутствие в
    /// документе обязано <b>сохранить</b> инициализатор, и ради этого в
    /// порождаемом коде заводится флаг присутствия;</item>
    /// <item><c>Renamed</c> - аргумент, чьё свойство переименовано: в документе
    /// он ищется по JSON-имени, а с параметром связан по C#-имени.</item>
    /// </list>
    /// </summary>
    public class Ticket
    {
        public int Id { get; }

        public int Tier { get; }

        public List<string>? Tags { get; }

        public string? Note { get; set; } = "kept";

        [JsonPropertyName("renamed")]
        public int Renamed { get; }

        public Ticket(int id, List<string>? tags, int renamed, int tier = 42)
        {
            Id = id;
            Tags = tags;
            Renamed = renamed;
            Tier = tier;
        }

        public static Ticket CreateSample()
        {
            return new Ticket(7, new List<string> { "a", "b", }, 5) { Note = "written", };
        }
    }

    /// <summary>
    /// Два параметризованных конструктора: без <c>[JsonConstructor]</c> эталон
    /// падает в рантайме, поэтому выбор обязан быть явным.
    /// </summary>
    public class Choice
    {
        public int Alpha { get; }

        public int Beta { get; }

        public Choice(int alpha)
        {
            Alpha = alpha;
            Beta = -1;
        }

        [JsonConstructor]
        public Choice(int alpha, int beta)
        {
            Alpha = alpha;
            Beta = beta;
        }
    }

    /// <summary>
    /// Позиционный <c>record</c>-класс. Его <c>init</c>-свойства все до одного
    /// аргументы конструктора, поэтому отдельного кода ему не нужно - но
    /// убедиться в этом надо прогоном.
    /// </summary>
    public record Positional(int Alpha, string? Beta, List<int>? Items);

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Ticket), true)]
    public partial class TicketSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Choice), true)]
    public partial class ChoiceSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Positional), true)]
    public partial class PositionalSerializer
    {
    }

    /// <summary>
    /// Полиморфная база. <c>Cat</c> с числовым дискриминатором нарочно рядом со
    /// строковым: значение сравнивается сырым текстом, и эти два случая должны
    /// пройти одной веткой.
    ///
    /// Сама база производной от себя <b>не</b> объявлена - значит пишется без
    /// дискриминатора, как обычный объект (проверено прогоном эталона).
    /// </summary>
    [JsonDerivedType(typeof(Dog), "dog")]
    [JsonDerivedType(typeof(Cat), 7)]
    public class Animal
    {
        public string? Name { get; set; }
    }

    public class Dog : Animal
    {
        public bool Barks { get; set; }
    }

    public class Cat : Animal
    {
        public int Lives { get; set; }
    }

    /// <summary>Незарегистрированный потомок: запись обязана отказать.</summary>
    public class Poodle : Dog
    {
        public int Curls { get; set; }
    }

    /// <summary>
    /// Два уровня и своё имя дискриминатора. Набор производных не транзитивен:
    /// <c>Bottom</c> объявлен на <c>Middle</c>, и как <c>Top</c> он не
    /// пишется - у эталона это отказ.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Middle), "middle")]
    public class Top
    {
        public int A { get; set; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Bottom), "bottom")]
    public class Middle : Top
    {
        public int B { get; set; }
    }

    public class Bottom : Middle
    {
        public int C { get; set; }
    }

    /// <summary>Полиморфный тип на месте члена.</summary>
    public class Shelter
    {
        public Animal? Resident { get; set; }

        public List<Animal>? Residents { get; set; }

        public static Shelter CreateSample()
        {
            return new Shelter
            {
                Resident = new Cat { Name = "m", Lives = 9, },
                Residents = new List<Animal>
                {
                    new Dog { Name = "r", Barks = true, },
                    new Animal { Name = "plain", },
                },
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Animal), true)]
    [JsonSubject(typeof(Dog), false)]
    [JsonSubject(typeof(Cat), false)]
    public partial class AnimalSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Top), true)]
    [JsonSubject(typeof(Middle), false)]
    [JsonSubject(typeof(Bottom), false)]
    public partial class TopSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Shelter), true)]
    [JsonSubject(typeof(Animal), false)]
    [JsonSubject(typeof(Dog), false)]
    [JsonSubject(typeof(Cat), false)]
    public partial class ShelterSerializer
    {
    }

    // ---- Фаза 6, §9.10: субъект, который сам является коллекцией (часть 1) ----

    /// <summary>
    /// Простейшая форма из их корпуса (§11.1, §9.6): наследник
    /// <c>List&lt;T&gt;</c> без единого собственного члена.
    /// </summary>
    public class StringListWrapper : List<string>
    {
    }

    /// <summary>
    /// Тот же угол словарём: эталон смотрит на <c>IDictionary&lt;,&gt;</c>
    /// раньше, чем на <c>IEnumerable</c>, и пишет объект, а не массив -
    /// проверено пробой.
    /// </summary>
    public class IntDictionaryWrapper : Dictionary<string, int>
    {
    }

    /// <summary>
    /// Ручная реализация <c>ICollection&lt;T&gt;</c> - <c>Add</c> есть, но не
    /// унаследован от <c>List&lt;T&gt;</c>. Собственное свойство
    /// <see cref="Label"/> эталон молча теряет (проверено пробой: класс с
    /// <c>ICollection&lt;string&gt;</c> и property-членом пишется как массив
    /// без единого свойства), и мы теряем его так же - решение §9.10.
    /// </summary>
    public class CustomStringCollection : ICollection<string>
    {
        private readonly List<string> _items = new List<string>();

        public string? Label { get; set; }

        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(string item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(string item) => _items.Contains(item);
        public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public bool Remove(string item) => _items.Remove(item);
        public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Субъект-коллекция на месте члена другого субъекта: <c>null</c>,
    /// вложенность (список коллекций-субъектов) и обычный заполненный случай -
    /// три разных пути кода на одном типе.
    /// </summary>
    public class CollectionHost
    {
        public StringListWrapper? Wrapper { get; set; }
        public List<StringListWrapper>? Nested { get; set; }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(StringListWrapper), true)]
    [JsonSubject(typeof(IntDictionaryWrapper), true)]
    [JsonSubject(typeof(CustomStringCollection), true)]
    [JsonSubject(typeof(CollectionHost), true)]
    public partial class CollectionSubjectSerializer
    {
    }

    // ---- Фаза 6, §9.10: коллекционные интерфейсы на месте члена (часть 2) ----

    /// <summary>
    /// Все семь принятых интерфейсов разом, плюс <c>null</c> и пустая форма -
    /// см. <c>Forms</c>.
    /// </summary>
    public class IfaceCollections
    {
        public IList<int>? AsIList { get; set; }
        public ICollection<int>? AsICollection { get; set; }
        public IEnumerable<int>? AsIEnumerable { get; set; }
        public IReadOnlyList<int>? AsIReadOnlyList { get; set; }
        public IReadOnlyCollection<int>? AsIReadOnlyCollection { get; set; }
        public IDictionary<string, int>? AsIDictionary { get; set; }
        public IReadOnlyDictionary<string, int>? AsIReadOnlyDictionary { get; set; }

        public static IfaceCollections CreateSample()
        {
            return new IfaceCollections
            {
                AsIList = new List<int> { 1, 2, 3, },
                AsICollection = new List<int> { 4, 5, },
                AsIEnumerable = new List<int> { 6, },
                AsIReadOnlyList = new List<int> { 7, 8, },
                AsIReadOnlyCollection = new List<int> { 9, },
                AsIDictionary = new Dictionary<string, int> { ["a"] = 1, },
                AsIReadOnlyDictionary = new Dictionary<string, int> { ["b"] = 2, },
            };
        }

        public static IfaceCollections CreateEmpty()
        {
            return new IfaceCollections
            {
                AsIList = new List<int>(),
                AsICollection = new List<int>(),
                AsIEnumerable = new List<int>(),
                AsIReadOnlyList = new List<int>(),
                AsIReadOnlyCollection = new List<int>(),
                AsIDictionary = new Dictionary<string, int>(),
                AsIReadOnlyDictionary = new Dictionary<string, int>(),
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(IfaceCollections), true)]
    public partial class IfaceCollectionsSerializer
    {
    }

    /// <summary>
    /// Границы словесной формы диспетчера (§12.6.1 плана) в рантайме. Пять
    /// корзин по два члена: пять байт (короче слова), восемь (слово накрывает
    /// имя целиком), двенадцать (перекрытие в четыре байта), шестнадцать (стык
    /// без перекрытия - последняя полная длина) и семнадцать (между словами
    /// дыра, форма не печатается).
    ///
    /// Тип существует не ради round-trip'а, а ради <b>отрицательной</b>
    /// проверки: словесная форма сравнивает числовые константы, и перепутанная
    /// константа не падает - свойство молча уходит в пропуск, а документ
    /// читается с дырой. Это худший исход по §1, и ловится он только
    /// документом с чужими именами той же длины.
    /// </summary>
    public class WordEdges
    {
        public int Total { get; set; }
        public int Lines { get; set; }
        public int Quantity { get; set; }
        public int Currency { get; set; }
        public int DeliveryDate { get; set; }
        public int InvoicedDate { get; set; }
        public int DeliveryTimeslot { get; set; }
        public int CustomerCategory { get; set; }
        public int ShippingContainer { get; set; }
        public int PreferredLanguage { get; set; }

        public static WordEdges CreateSample()
        {
            return new WordEdges
            {
                Total = 1,
                Lines = 2,
                Quantity = 3,
                Currency = 4,
                DeliveryDate = 5,
                InvoicedDate = 6,
                DeliveryTimeslot = 7,
                CustomerCategory = 8,
                ShippingContainer = 9,
                PreferredLanguage = 10,
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(WordEdges), true)]
    public partial class WordEdgesSerializer
    {
    }

    /// <summary>
    /// Обязательные члены во всех формах, которые эталон различает
    /// (проверено пробой, scratchpad/ReqProbe): ключевое слово, атрибут
    /// <c>[JsonRequired]</c>, <c>init</c>-setter и переименованный член.
    ///
    /// Необязательный <c>Note</c> тут же не для полноты, а по делу: он
    /// доказывает, что маска присутствия считает только обязательных, а не
    /// «все имена подряд».
    /// </summary>
    public class Demanding
    {
        public required int Amount { get; set; }

        [JsonRequired]
        public int Count { get; set; }

        public required string Label { get; init; }

        [JsonPropertyName("amt")]
        public required int Renamed { get; set; }

        public string? Note { get; set; }

        public static Demanding CreateSample()
        {
            return new Demanding
            {
                Amount = 1,
                Count = 2,
                Label = "L",
                Renamed = 3,
                Note = "n",
            };
        }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Demanding), true)]
    public partial class DemandingSerializer
    {
    }

    /// <summary>
    /// Субъект под <c>[JsonFactory]</c>: читатель не создаёт его, а берёт у
    /// пула.
    /// </summary>
    public class Recycled
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>
    /// Фабрика плюс обязательный член. Комбинация не случайная: <c>new T()</c>
    /// у типа с <c>required</c>-членом - ошибка компиляции CS9035, а с
    /// фабрикой этого выражения в порождённом коде нет вовсе, значит и запрета
    /// нет. Проверка присутствия при этом обязана остаться.
    /// </summary>
    public class RecycledDemanding
    {
        public required int Id { get; set; }
    }

    /// <summary>
    /// Пул на два типа. Счётчик нужен тесту: он доказывает, что читатель
    /// действительно зовёт фабрику, а не создаёт объект сам.
    /// </summary>
    public static class RecyclePool
    {
        private static readonly Recycled _one = new Recycled();

        private static readonly RecycledDemanding _two = new RecycledDemanding { Id = 0, };

        public static int Calls
        {
            get;
            private set;
        }

        public static Recycled Reuse()
        {
            Calls++;
            return _one;
        }

        public static RecycledDemanding ReuseDemanding() => _two;
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Recycled), true)]
    [JsonSubject(typeof(RecycledDemanding), true)]
    [JsonFactory(typeof(Recycled), "global::JsonGoddess.Tests.Generated.RecyclePool.Reuse()")]
    [JsonFactory(typeof(RecycledDemanding), "global::JsonGoddess.Tests.Generated.RecyclePool.ReuseDemanding()")]
    public partial class RecycledSerializer
    {
    }
}
