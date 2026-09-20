using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Граф, который перехват обязан обойти сам: ни одного
    /// <c>[JsonSubject]</c> здесь нет и быть не должно - в этом весь смысл
    /// маршрута A.
    /// </summary>
    public class Order
    {
        public int Id
        {
            get;
            set;
        }

        public string? Customer
        {
            get;
            set;
        }

        public Address? ShipTo
        {
            get;
            set;
        }

        public List<Line>? Lines
        {
            get;
            set;
        }

        public Dictionary<string, int>? Counters
        {
            get;
            set;
        }

        public Status State
        {
            get;
            set;
        }

        public static Order CreateSample()
        {
            return new Order
            {
                Id = 42,
                Customer = "Пётр",
                ShipTo = new Address { City = "Псков", Zip = "180000", },
                Lines = new List<Line>
                {
                    new Line { Sku = "A-1", Quantity = 2, Price = 19.99m, },
                    new Line { Sku = "B-2", Quantity = 1, Price = 5m, },
                },
                Counters = new Dictionary<string, int> { { "views", 7 }, { "carts", 1 }, },
                State = Status.Shipped,
            };
        }
    }

    public class Address
    {
        public string? City
        {
            get;
            set;
        }

        public string? Zip
        {
            get;
            set;
        }
    }

    public class Line
    {
        public string? Sku
        {
            get;
            set;
        }

        public int Quantity
        {
            get;
            set;
        }

        public decimal Price
        {
            get;
            set;
        }
    }

    public enum Status
    {
        New = 0,
        Shipped = 1,
    }

    /// <summary>
    /// Всё, что экранируется по-разному у нас и у эталона, - в одном типе.
    ///
    /// <para>
    /// Имя свойства не-ASCII, имя из <c>[JsonPropertyName]</c> с
    /// HTML-значимыми символами, имя члена строкового enum'а, ключ словаря,
    /// значения со всем сразу - включая символ вне BMP и непарный суррогат.
    /// Каждая из этих пяти дорог до документа своя: три первых - константы,
    /// печатаемые генератором, две последних - работа sink'а в рантайме.
    /// </para>
    /// </summary>
    public class Tricky
    {
        public string? Обычное
        {
            get;
            set;
        }

        [JsonPropertyName("a<b>c&d'e+f`g")]
        public string? Html
        {
            get;
            set;
        }

        public Mood Mood
        {
            get;
            set;
        }

        public Dictionary<string, string>? Keys
        {
            get;
            set;
        }

        public static Tricky CreateSample()
        {
            return new Tricky
            {
                Обычное = "Привет, мир",
                Html = "<b>жирный</b> & 'кавычки' + плюс",
                Mood = Mood.Delight,
                Keys = new Dictionary<string, string>
                {
                    { "ключ", "значение" },
                    { "эмодзи", "\U0001F600 и обрывок \uD800 после него" },
                },
            };
        }
    }

    /// <summary>
    /// Имя члена строкового enum'а взято ASCII-шное, но с экранируемыми
    /// символами: не-ASCII имя enum'а у нас и так отказ (эталон сличает такие
    /// имена без учёта регистра по Unicode, мы - по ASCII), и проверять на нём
    /// экранирование было бы проверкой отказа.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Mood
    {
        [JsonStringEnumMemberName("a<b>&c")]
        Delight = 0,

        Sorrow = 1,
    }

    /// <summary>
    /// Полиморфный граф: база, два производных и держатель - одиночным членом
    /// и списком (PLAN.md §15, O11 а).
    ///
    /// <para>
    /// Дискриминаторы нарочно разного рода - строка и число: связыватель
    /// принимает оба, и сравниваются они у моста по-разному
    /// (<c>ValueTextEquals</c> против <c>TryGetInt32</c>).
    /// </para>
    /// </summary>
    [JsonDerivedType(typeof(Dog), "dog")]
    [JsonDerivedType(typeof(Cat), 7)]
    public class Animal
    {
        public int Id
        {
            get;
            set;
        }

        public string? Name
        {
            get;
            set;
        }
    }

    public class Dog : Animal
    {
        public bool Barks
        {
            get;
            set;
        }
    }

    public class Cat : Animal
    {
        public int Lives
        {
            get;
            set;
        }
    }

    public class Shelter
    {
        public int Id
        {
            get;
            set;
        }

        public Animal? Star
        {
            get;
            set;
        }

        public List<Animal>? Residents
        {
            get;
            set;
        }

        public static Shelter CreateSample()
        {
            return new Shelter
            {
                Id = 3,
                Star = new Dog { Id = 1, Name = "Бим", Barks = true, },
                Residents = new List<Animal>
                {
                    new Cat { Id = 2, Name = "Мурка", Lives = 9, },
                    new Animal { Id = 3, Name = "просто зверь", },
                },
            };
        }
    }

    /// <summary>
    /// Тип, который обслужить нельзя: член несёт чужой конвертер, и
    /// воспроизвести его мы не можем. Обход графа обязан отступить именно
    /// здесь и именно с <c>JGD001</c>, а не уронить сборку.
    /// </summary>
    public class WithConverter
    {
        public int Id
        {
            get;
            set;
        }

        [JsonConverter(typeof(OwnConverter))]
        public string? Weird
        {
            get;
            set;
        }
    }

    public sealed class OwnConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string Read(
            ref System.Text.Json.Utf8JsonReader reader,
            System.Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options
            )
        {
            return reader.GetString() ?? string.Empty;
        }

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            string value,
            System.Text.Json.JsonSerializerOptions options
            )
        {
            writer.WriteStringValue(value);
        }
    }
}
