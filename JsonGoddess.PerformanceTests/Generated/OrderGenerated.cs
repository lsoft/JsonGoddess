using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// REGULAR на генераторе. До фазы 4 этой строки в таблице быть не могло:
    /// <c>Order.Lines</c> - это <c>List&lt;OrderLine&gt;</c>, и генератор
    /// честно отвергал такой член с JGD022.
    ///
    /// Рядом лежит рукописный макет <see cref="OrderSerializer"/> той же формы,
    /// и смысл его прежний: он отвечает не на вопрос «какую форму диспетчера
    /// выбрать» - на него ответили ещё в фазе 1, - а на вопрос <b>не хуже ли
    /// порождённый код лучшей рукописной формы</b>. На REGULAR длины имён
    /// разводят членов по корзинам, и эмиттер обязан выбрать цепочку
    /// сравнений - ту форму, которая на этой форме документа выиграла 13.5%.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class OrderGenerated
    {
    }
}
