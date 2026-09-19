using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// Тот же REGULAR, но на compat-раковине. Хост нужен маршруту B (§10):
    /// мост отдаёт эталону сырой кусок документа, и кусок этот обязан быть
    /// байт в байт тем, что эталон написал бы сам - значит, писать его должен
    /// <c>CompatUtf8Exhauster</c>, а не обычный.
    ///
    /// <para>
    /// На форме <c>Order</c> оба набора экранирования дают один документ: в
    /// именах членов нечего экранировать, строковых констант нет. Это не
    /// делает выбор раковины формальностью - на других формах разойдутся, -
    /// но означает, что замер сравнивает мост с эталоном, а не два разных
    /// документа. Байтовое совпадение проверяется в <c>GlobalSetup</c>.
    /// </para>
    /// </summary>
    [JsonExhauster(typeof(CompatUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class OrderBridgeHost
    {
    }
}
