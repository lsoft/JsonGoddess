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
