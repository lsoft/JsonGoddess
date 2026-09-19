using System.Text.Json.Serialization;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype.Generated
{
    /// <summary>
    /// Порождённый читатель того же типа и с теми же правилами имён, что у
    /// ручного прототипа: camelCase и нечувствительность к регистру, то есть
    /// веб-профиль.
    ///
    /// <para>
    /// Он здесь ровно для одного вопроса: <b>не съела ли Try-форма счастливый
    /// путь</b>. Сравнивать ручной читатель с эталоном мало - так не отличить
    /// «схема дешёвая» от «прототип случайно оказался быстрее порождённого
    /// кода».
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonFeature(JsonFeature.CaseInsensitiveNames)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    internal partial class OrderHost
    {
    }
}
