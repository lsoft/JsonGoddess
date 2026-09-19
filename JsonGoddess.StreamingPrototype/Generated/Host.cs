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
    //Ровно тот набор, что мост ставит своему хосту (CompatBinder.CompatGuards):
    //он подтянут к эталону, а не выбран по вкусу. Без него порождённый читатель
    //брал бы число нестрогой лексикой и принимал '01', который эталон
    //отвергает, - и замер сравнивал бы разные документы. Найдено подстановкой
    //порождённого читателя в проверки прототипа (пункт 6б).
    [JsonGuard(
        JsonGuard.TrailingContent
        | JsonGuard.ControlCharsInStrings
        | JsonGuard.StrictNumbers
        | JsonGuard.InvalidUtf8
        | JsonGuard.MaxDepth,
        MaxDepth = 64
        )]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    internal partial class OrderHost
    {
    }
}
