using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Настройки хоста, прочитанные из <c>[JsonFeature]</c> (§6.2 плана).
    ///
    /// Устроено тем же способом, что и <see cref="GuardOptions"/>: атрибут
    /// читается по имени и числом, без ссылки на рантайм-сборку из
    /// компилятора. Именованных аргументов у <c>JsonFeatureAttribute</c> нет
    /// вовсе (в отличие от <c>JsonGuardAttribute.MaxDepth</c>), поэтому вся
    /// работа - один обязательный аргумент конструктора.
    /// </summary>
    public sealed class FeatureOptions
    {
        public static readonly FeatureOptions Default = new FeatureOptions(JsonFeature.None);

        public JsonFeature Features { get; }

        private FeatureOptions(JsonFeature features)
        {
            Features = features;
        }

        public static FeatureOptions Read(INamedTypeSymbol host, KnownSymbols known)
        {
            if (known.Feature is null)
            {
                return Default;
            }

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Feature))
                {
                    continue;
                }

                var features = attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is int raw
                        ? (JsonFeature)raw
                        : JsonFeature.None;

                return new FeatureOptions(features);
            }

            return Default;
        }
    }
}
