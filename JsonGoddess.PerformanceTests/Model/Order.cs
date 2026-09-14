using System;
using System.Collections.Generic;

namespace JsonGoddess.PerformanceTests.Model
{
    /// <summary>
    /// REGULAR: форма обычного сообщения. Шесть скаляров разных лексических
    /// семейств (целое, строка, дата, decimal, bool, GUID) плюс коллекция из
    /// трёх элементов по четыре члена - двадцать значений и две формы объекта.
    ///
    /// Имена членов оставлены как объявлены, и опции BCL нигде не меняются:
    /// сравнивать надо на одном и том же документе, а всякая настройка
    /// naming policy у эталона - это ещё и работа, которой у нас нет по
    /// построению. Байтовое совпадение документов проверяется в GlobalSetup.
    /// </summary>
    public sealed class Order
    {
        public int Id { get; set; }

        public string? Customer { get; set; }

        public DateTime Created { get; set; }

        public decimal Total { get; set; }

        public bool Paid { get; set; }

        public Guid Reference { get; set; }

        public List<OrderLine>? Lines { get; set; }

        public static Order CreateSample()
        {
            return new Order
            {
                Id = 1042,
                Customer = "Acme Industrial Supplies",
                Created = new DateTime(2026, 9, 14, 18, 57, 54, DateTimeKind.Utc),
                Total = 1499.95m,
                Paid = true,
                Reference = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
                Lines = new List<OrderLine>
                {
                    new OrderLine { Sku = "BRK-0001", Quantity = 4, Price = 129.99m, Note = "back order", },
                    new OrderLine { Sku = "CLM-0342", Quantity = 1, Price = 899.00m, Note = null, },
                    new OrderLine { Sku = "WSH-9910", Quantity = 32, Price = 2.50m, Note = "bulk pack", },
                },
            };
        }
    }

    public sealed class OrderLine
    {
        public string? Sku { get; set; }

        public int Quantity { get; set; }

        public decimal Price { get; set; }

        public string? Note { get; set; }
    }
}
