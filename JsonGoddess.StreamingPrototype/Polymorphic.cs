using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Полиморфный корень и полиморфный член - потоком (PLAN.md §15, O11 (а)).
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
}
