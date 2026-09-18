namespace JsonGoddess.GeneratorTests.Harness
{
    /// <summary>
    /// Исходники-образцы. Вынесены сюда, чтобы тесты говорили о том, что
    /// проверяют, а не о том, как выглядит POCO.
    /// </summary>
    public static class Sources
    {
        /// <summary>
        /// Длины имён: Id=2, Paid=4, Total=5, Created=7, Customer=8,
        /// Reference=9. Ни одна корзина не дорастает до порога, поэтому
        /// диспетчер обязан выбрать цепочку сравнений.
        /// </summary>
        public const string DistinctLengths = @"
using System;
using JsonGoddess;

namespace Demo
{
    public class Order
    {
        public int Id { get; set; }
        public bool Paid { get; set; }
        public decimal Total { get; set; }
        public DateTime Created { get; set; }
        public string? Customer { get; set; }
        public Guid Reference { get; set; }
    }

    [JsonSubject(typeof(Order), true)]
    public partial class OrderSerializer
    {
    }
}
";

        /// <summary>
        /// Четыре корзины по два члена - по одной на каждую сторону границы
        /// словесной формы (§12.6.1 плана):
        /// <list type="bullet">
        /// <item>пять байт - короче восьми, слова не прочитать;</item>
        /// <item>восемь байт - одно слово накрывает имя целиком, второго не
        /// печатается;</item>
        /// <item>двенадцать байт - два слова с перекрытием в четыре байта;</item>
        /// <item>шестнадцать байт - слова стыкуются без перекрытия, это
        /// последняя длина, на которой форма ещё полна;</item>
        /// <item>семнадцать байт - между словами дыра, словесная форма
        /// перестаёт доказывать имя.</item>
        /// </list>
        /// </summary>
        public const string TwoPerBucket = @"
using JsonGoddess;

namespace Demo
{
    public class Edges
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
    }

    [JsonSubject(typeof(Edges), true)]
    public partial class EdgesSerializer
    {
    }
}
";

        /// <summary>
        /// Двадцать четыре имени длиной ровно семь байт. Корзина одна, она
        /// ровно на пороге (<c>NameDispatcher.KeySwitchThreshold</c>), и длина
        /// не отсекает ни одного кандидата - случай, в котором замер отдал
        /// победу ключу.
        ///
        /// Членов именно столько, сколько требует порог, а не «с запасом»:
        /// образец обязан ломаться, если порог поднимут, не тронув его.
        /// </summary>
        public const string SameLength = @"
using JsonGoddess;

namespace Demo
{
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
        public int Field10 { get; set; }
        public int Field11 { get; set; }
        public int Field12 { get; set; }
        public int Field13 { get; set; }
        public int Field14 { get; set; }
        public int Field15 { get; set; }
        public int Field16 { get; set; }
        public int Field17 { get; set; }
        public int Field18 { get; set; }
        public int Field19 { get; set; }
        public int Field20 { get; set; }
        public int Field21 { get; set; }
        public int Field22 { get; set; }
        public int Field23 { get; set; }
    }

    [JsonSubject(typeof(Wide), true)]
    public partial class WideSerializer
    {
    }
}
";

        /// <summary>
        /// Имена по десять байт с одинаковыми первыми семью: ключ у них
        /// один на всех, а двух case с одной константой в C# не бывает.
        ///
        /// Их двадцать четыре, потому что ветка с общим ключом печатается
        /// только там, где корзина дотянула до порога
        /// (<c>NameDispatcher.KeySwitchThreshold</c>); ниже порога печатается
        /// цепочка, и столкновению ключей просто негде случиться.
        /// </summary>
        public const string CollidingKeys = @"
using JsonGoddess;

namespace Demo
{
    public class Colliding
    {
        public int ReferenceA { get; set; }
        public int ReferenceB { get; set; }
        public int ReferenceC { get; set; }
        public int ReferenceD { get; set; }
        public int ReferenceE { get; set; }
        public int ReferenceF { get; set; }
        public int ReferenceG { get; set; }
        public int ReferenceH { get; set; }
        public int ReferenceI { get; set; }
        public int ReferenceJ { get; set; }
        public int ReferenceK { get; set; }
        public int ReferenceL { get; set; }
        public int ReferenceM { get; set; }
        public int ReferenceN { get; set; }
        public int ReferenceO { get; set; }
        public int ReferenceP { get; set; }
        public int ReferenceQ { get; set; }
        public int ReferenceR { get; set; }
        public int ReferenceS { get; set; }
        public int ReferenceT { get; set; }
        public int ReferenceU { get; set; }
        public int ReferenceV { get; set; }
        public int ReferenceW { get; set; }
        public int ReferenceX { get; set; }
    }

    [JsonSubject(typeof(Colliding), true)]
    public partial class CollidingSerializer
    {
    }
}
";

        /// <summary>
        /// Составные типы: вложенный субъект, две коллекции одного типа,
        /// коллекция коллекций, массив и <c>byte[]</c>, который коллекцией
        /// быть не должен.
        /// </summary>
        public const string Composite = @"
using System.Collections.Generic;
using JsonGoddess;

namespace Demo
{
    public class Line
    {
        public int Quantity { get; set; }
    }

    public class Basket
    {
        public Line? Head { get; set; }
        public List<Line>? Lines { get; set; }
        public List<Line>? Backorder { get; set; }
        public List<List<int>>? Matrix { get; set; }
        public int[]? Numbers { get; set; }
        public byte[]? Payload { get; set; }
    }

    [JsonSubject(typeof(Basket), true)]
    [JsonSubject(typeof(Line), false)]
    public partial class BasketSerializer
    {
    }
}
";

        /// <summary>
        /// Хост с <c>[JsonFactory]</c>: субъект, пул рядом и фабричное
        /// выражение, которое эмиттер обязан напечатать дословно.
        /// </summary>
        public static string Factory(string invocation)
        {
            return @"
using JsonGoddess;

namespace Demo
{
    public class Subject
    {
        public int Id { get; set; }
    }

    public static class Pool
    {
        private static readonly Subject _one = new Subject();

        public static Subject Reuse() => _one;
    }

    [JsonFactory(typeof(Demo.Subject), """ + invocation + @""")]
    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
";
        }

        public static string Host(string subjectMembers, string hostAttributes = "")
        {
            return @"
using System;
using JsonGoddess;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Demo
{
    public class Subject
    {
" + subjectMembers + @"
    }

    " + hostAttributes + @"
    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
";
        }
    }
}
