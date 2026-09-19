using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.WebPerformanceTests
{
    /// <summary>
    /// Что отдаёт веб-метод. Две формы, и обе выбраны не для красоты.
    ///
    /// <para>
    /// <b>Один объект</b> - худший случай для нас и самый частый в жизни:
    /// ответ весит четыре сотни байт, а запрос вокруг него стои́т десятки
    /// микросекунд. Если доля JSON где-то и утонет, то здесь.
    /// </para>
    ///
    /// <para>
    /// <b>Тысяча объектов</b> - тот ответ, ради которого вообще заходит речь
    /// о скорости сериализации: несколько сотен килобайт, и конвейер вокруг
    /// них уже не главный расход.
    /// </para>
    ///
    /// <para>
    /// Элементы различаются: тысяча ссылок на один объект померила бы заодно
    /// то, чего в жизни нет - тёплый кеш на одних и тех же строках.
    /// </para>
    /// </summary>
    internal static class Payload
    {
        internal const int ManyCount = 1000;

        internal static readonly Order One = Order.CreateSample();

        internal static readonly Order[] Many = BuildMany();

        private static Order[] BuildMany()
        {
            var many = new Order[ManyCount];

            for (var index = 0; index < ManyCount; index++)
            {
                var order = Order.CreateSample();
                order.Id += index;
                order.Customer = "Acme Industrial Supplies, branch " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                order.Total += index;
                many[index] = order;
            }

            return many;
        }
    }
}
