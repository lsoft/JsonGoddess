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

    /// <summary>
    /// Тот же тип и те же имена, но <b>без единого стража</b>.
    ///
    /// <para>
    /// Заведён ради одного числа и только его: <b>сколько стои́т согласие с
    /// эталоном</b>. Строгая лексика чисел, проверка управляющих байтов,
    /// проверка UTF-8 и счётчик глубины - это не украшение, а то, чем мы
    /// отвергаем ровно те документы, которые отвергает
    /// <c>System.Text.Json</c>. Цена у этого есть, и называть её надо числом,
    /// а не словами.
    /// </para>
    ///
    /// <para>
    /// Меряется в <b>одном</b> прогоне с остальными строками таблицы: сравнение
    /// чисел между прогонами - ровно то, ради чего заведён
    /// <c>run-benchmarks.bat</c>, и делать его руками нельзя.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonFeature(JsonFeature.CaseInsensitiveNames)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    internal partial class OrderHostWithoutGuards
    {
    }
}
