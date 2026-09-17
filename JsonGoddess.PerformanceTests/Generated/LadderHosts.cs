using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// <b>Лестница флагов</b>: один и тот же субъект (REGULAR, <see cref="Order"/>)
    /// на тринадцати хостах - голом и по одному на каждый включённый флаг.
    ///
    /// Требование плана к фазе 6 звучит буквально: «ни один флаг без своей
    /// строки». Утверждение «выключённый флаг стоит ноль» уже держится тестом
    /// на текст - порождаемый код хоста без флагов не меняется ни на байт
    /// (§9.11, §9.12). Здесь меряется <b>другое</b>: сколько стоит флаг
    /// <b>включённый</b>, и это число ни из чего не выводится.
    ///
    /// Форма лестницы выбрана так, чтобы разница была видна: все хосты читают
    /// один и тот же документ, отличается только код, который для них
    /// напечатан. Baseline - <see cref="LadderBase"/>; его <c>Ratio</c> и есть
    /// цена флага.
    ///
    /// Чего здесь намеренно нет: <c>JsonGuard.MaxDepth</c> с нестандартным
    /// пределом и композитов <c>SystemTextJsonCompatible</c> по отдельности -
    /// первый меряется тем же кодом, что и стандартный, а вторые суть
    /// суммы уже померенных строк.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderBase
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.DuplicateProperties)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderDuplicateProperties
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.TrailingContent)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderTrailingContent
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.ControlCharsInStrings)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderControlChars
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.StrictNumbers)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderStrictNumbers
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.InvalidUtf8)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderInvalidUtf8
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.MaxDepth)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderMaxDepth
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonGuard(JsonGuard.UnknownProperties)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderUnknownProperties
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.Comments)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderComments
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.TrailingCommas)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderTrailingCommas
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.NamedFloatingPointLiterals)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderNamedFloats
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.NumbersFromStrings)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderNumbersFromStrings
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.CaseInsensitiveNames)]
    [JsonSubject(typeof(Order), true)]
    [JsonSubject(typeof(OrderLine), false)]
    public partial class LadderCaseInsensitive
    {
    }
}
