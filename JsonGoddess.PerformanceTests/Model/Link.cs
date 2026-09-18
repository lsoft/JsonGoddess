namespace JsonGoddess.PerformanceTests.Model
{
    /// <summary>
    /// LINK: форма под дешёвое звено цепочки и под <b>калибровку стенда</b>
    /// (§12.5 плана).
    ///
    /// Восемь членов, все длины <b>разные</b> и все лежат в 8…15 байт. Это
    /// сделано нарочно и сразу под две цели.
    /// <list type="number">
    /// <item>Разные длины - значит в каждой корзине ровно один член, цепочки
    /// нет вовсе, и на каждое прочитанное свойство приходится <b>ровно одно</b>
    /// сравнение имени. Это форма обычного типа (как REGULAR), а не вырожденная
    /// (как PREFIX), - то есть та, ради которой библиотека и существует.</item>
    /// <item>Длины 8…15 - значит имя целиком накрывается двумя перекрывающимися
    /// восьмибайтовыми чтениями, и дешёвое звено применимо к каждому члену без
    /// исключений. Мерить размен «вызов против двух сравнений» можно в чистом
    /// виде.</item>
    /// </list>
    /// Все члены - <c>int</c>, по той же причине, что и в <see cref="Prefix"/>:
    /// чем дешевле разбор значения, тем большую долю занимает сопоставление
    /// имени, а мерится здесь именно оно.
    /// </summary>
    public sealed class Link
    {
        /// <summary>Восемь байт: одно чтение <c>ulong</c> накрывает имя ровно.</summary>
        public int Quantity { get; set; }

        /// <summary>Девять байт: два чтения перекрываются на семь байт.</summary>
        public int Reference { get; set; }

        public int CustomerId { get; set; }

        public int Description { get; set; }

        public int DeliveryDate { get; set; }

        public int InvoiceNumber { get; set; }

        public int ShippingMethod { get; set; }

        /// <summary>Пятнадцать байт: два чтения перекрываются на один байт.</summary>
        public int PaymentProvider { get; set; }

        public static Link CreateSample()
        {
            return new Link
            {
                Quantity = 4,
                Reference = 1042,
                CustomerId = 77,
                Description = -3,
                DeliveryDate = 20260918,
                InvoiceNumber = 900001,
                ShippingMethod = 2,
                PaymentProvider = 11,
            };
        }
    }
}
