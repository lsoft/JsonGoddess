using System.Text.Json.Serialization;

namespace JsonGoddess.PerformanceTests.Model
{
    /// <summary>
    /// Source-gen режим <c>System.Text.Json</c>. Это и есть настоящий
    /// противник: сравнение только с рефлексивным режимом было бы подтасовкой -
    /// всякий, кому важна скорость, включит именно этот.
    /// </summary>
    [JsonSerializable(typeof(Order))]
    [JsonSerializable(typeof(Order[]))]
    [JsonSerializable(typeof(OrderLine))]
    [JsonSerializable(typeof(Wide))]
    public partial class OrderJsonContext : JsonSerializerContext
    {
    }
}
