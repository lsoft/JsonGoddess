using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// PREFIX на настоящем генераторе: то, что получит человек, подключивший
    /// пакет. Рядом лежит <see cref="PrefixSerializer"/> с четырьмя
    /// рукописными формами конфликтной ветки - сравнивать надо именно с ними,
    /// и в одном round-robin.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Prefix), true)]
    public partial class PrefixGenerated
    {
    }

    /// <summary>
    /// Корзины под порог <c>KeySwitchThreshold</c>. Порог сейчас равен
    /// четырём, поэтому генератор печатает для двойки цепочку, а для четвёрки
    /// и восьмёрки - <c>switch</c> по ключу; рукописные формы в
    /// <see cref="BucketSerializer"/> дают обе для каждого размера, и вопрос
    /// «в правильном ли месте стоит четвёрка» становится вопросом к числам.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Bucket2), true)]
    public partial class Bucket2Generated
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Bucket4), true)]
    public partial class Bucket4Generated
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Bucket8), true)]
    public partial class Bucket8Generated
    {
    }

    /// <summary>
    /// Тот же тип с включённым <c>CaseInsensitiveNames</c> - то, что эмиттер
    /// печатает сегодня по вопросу O5: цепочка <c>EqualsIgnoreCase</c> вместо
    /// <c>switch</c> по ключу, вне зависимости от размера корзины. Пара к
    /// рукописному <c>FoldKey8</c>, который делает то же самое свёрнутым
    /// ключом.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonFeature(JsonFeature.CaseInsensitiveNames)]
    [JsonSubject(typeof(Bucket8), true)]
    public partial class Bucket8FoldGenerated
    {
    }
}
