using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Формы субъекта, которых потоковый читатель не брал до O11: полиморфный
    /// тип и субъект-коллекция (PLAN.md §15, O11 а и б).
    ///
    /// <para>
    /// Модель своя, а не общая с лестницей, и это не удобство: добавить
    /// производный тип в <c>Order</c> значило бы поменять документ, которым
    /// меряются все прочие строки, - числа перестали бы соотноситься между
    /// собой. Здесь же вопрос не про скорость вовсе, а про то,
    /// <b>совпадаем ли мы с эталоном</b> на теле, которое обрывается где
    /// попало.
    /// </para>
    /// </summary>
    [JsonDerivedType(typeof(Circle), "circle")]
    [JsonDerivedType(typeof(Square), "square")]
    public class Shape
    {
        public int Id { get; set; }

        public string? Label { get; set; }
    }

    public class Circle : Shape
    {
        public double Radius { get; set; }
    }

    public class Square : Shape
    {
        public double Side { get; set; }

        public List<string>? Corners { get; set; }
    }

    /// <summary>Полиморфный тип на месте члена - вторая половина вопроса.</summary>
    public class Drawing
    {
        public int Id { get; set; }

        public Shape? Cover { get; set; }

        public List<Shape>? Shapes { get; set; }
    }

    /// <summary>
    /// Субъект-коллекция (§9.10, PLAN.md §15 O11 (б)) - обе формы, список и
    /// словарь.
    /// </summary>
    public class Palette : List<string>
    {
    }

    public class Weights : Dictionary<string, int>
    {
    }

    /// <summary>Держатель: до починки один такой член снимал обслуживание со всего графа над собой.</summary>
    public class Canvas
    {
        public int Id { get; set; }

        public Palette? Colors { get; set; }

        public Weights? Weights { get; set; }

        public Shape? Cover { get; set; }
    }
}
