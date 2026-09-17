namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Субъект для ladder-фикстур <c>JsonFeature</c> (§6.2 плана), по образцу
    /// <c>GuardSubject</c>: один тип на все фичи, скаляры разных лексических
    /// семейств сразу - числа, строка, float/double/decimal.
    /// </summary>
    public class FeatureSubject
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public float F { get; set; }

        public double D { get; set; }

        public decimal M { get; set; }
    }

    /// <summary>Хост без единой фичи - "как сейчас".</summary>
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class PlainFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.Comments)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class CommentsFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.TrailingCommas)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class TrailingCommasFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.NamedFloatingPointLiterals)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class NamedFloatingPointLiteralsFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.NumbersFromStrings)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class NumbersFromStringsFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.NamedFloatingPointLiterals | JsonFeature.NumbersFromStrings)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class NamedFloatAndFromStringFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.CaseInsensitiveNames)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class CaseInsensitiveNamesFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.SystemTextJsonCompatible)]
    [JsonSubject(typeof(FeatureSubject), true)]
    public partial class CompatibleFeatureSerializer
    {
    }

    /// <summary>
    /// Субъект-массив/словарь для ladder-фикстур <c>TrailingCommas</c>:
    /// проверить хвостовую запятую нужно не только у объекта, но и у
    /// массива/словаря - у них отдельные читатели (<c>EmitCollectionReader</c>).
    /// </summary>
    public class FeatureCollectionSubject
    {
        public System.Collections.Generic.List<int>? Numbers { get; set; }

        public System.Collections.Generic.Dictionary<string, int>? Map { get; set; }
    }

    [JsonSubject(typeof(FeatureCollectionSubject), true)]
    public partial class PlainFeatureCollectionSerializer
    {
    }

    [JsonFeature(JsonFeature.TrailingCommas)]
    [JsonSubject(typeof(FeatureCollectionSubject), true)]
    public partial class TrailingCommasCollectionFeatureSerializer
    {
    }

    [JsonFeature(JsonFeature.Comments)]
    [JsonSubject(typeof(FeatureCollectionSubject), true)]
    public partial class CommentsCollectionFeatureSerializer
    {
    }
}
