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
        /// Шесть имён длиной ровно семь байт. Корзина одна, она выше порога,
        /// и длина не отсекает ни одного кандидата - случай, в котором замер
        /// отдал победу ключу.
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
    }

    [JsonSubject(typeof(Wide), true)]
    public partial class WideSerializer
    {
    }
}
";

        /// <summary>
        /// Два имени по девять байт с одинаковыми первыми семью: ключ у них
        /// один, а двух case с одной константой в C# не бывает.
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

        public static string Host(string subjectMembers, string hostAttributes = "")
        {
            return @"
using System;
using JsonGoddess;
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
