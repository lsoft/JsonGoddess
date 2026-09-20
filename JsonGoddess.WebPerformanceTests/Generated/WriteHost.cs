using System.Text.Json.Serialization;
using JsonGoddess.Compat;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.WebPerformanceTests.Generated
{
    /// <summary>
    /// Хост ради одного вопроса: <b>сколько стои́т обёртка эталона на записи
    /// ответа</b>.
    ///
    /// <para>
    /// Сегодня ответ пишется так: порождённый писатель кладёт документ в
    /// экзостер целиком, мост отдаёт его эталону одним <c>WriteRawValue</c>,
    /// <c>Utf8JsonWriter</c> копирует его к себе, и уже оттуда байты уезжают в
    /// поток. То есть поверх нашей записи стоя́т ещё две копии и весь автомат
    /// <c>Utf8JsonWriter</c>. Свой выходной форматтер писал бы прямо в
    /// <c>PipeWriter</c>, и обе копии исчезли бы.
    /// </para>
    ///
    /// <para>
    /// Стои́т ли эта работа своих денег - вопрос к числу, а не ко мне. Этот
    /// хост даёт третью и четвёртую строки таблицы: ту же запись <b>без</b>
    /// эталона вовсе. Разница между мостом и ими и есть цена обёртки, и
    /// меряется она в одном прогоне с остальными.
    /// </para>
    ///
    /// <para>
    /// Профиль обязан совпасть с тем, которым MVC пишет ответ, иначе строки
    /// сравнивали бы разные документы: camelCase на именах и
    /// <see cref="EncoderUtf8Exhauster"/>, спрашивающий энкодер у опций, - а
    /// MVC кладёт туда <c>UnsafeRelaxedJsonEscaping</c>. Совпадение не
    /// объявлено, а проверено побайтово в <see cref="WritePathFixture"/> до
    /// первого замера.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonExhauster(typeof(EncoderUtf8Exhauster))]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    internal partial class WriteHost
    {
    }
}
