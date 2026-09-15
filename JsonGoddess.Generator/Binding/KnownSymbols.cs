using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Типы, известные генератору по именам.
    ///
    /// Атрибуты <c>System.Text.Json</c> могут отсутствовать вовсе - на них никто
    /// не обязан ссылаться, - поэтому каждый из них nullable, и отсутствие
    /// означает «такого атрибута в этой компиляции не бывает», а не ошибку.
    /// </summary>
    public sealed class KnownSymbols
    {
        public const string SubjectAttribute = "JsonGoddess.JsonSubjectAttribute";
        public const string ExhausterAttribute = "JsonGoddess.JsonExhausterAttribute";
        public const string InjectorAttribute = "JsonGoddess.JsonInjectorAttribute";
        public const string ExhausterBaseName = "JsonGoddess.ExhausterBase";
        public const string InjectorBaseName = "JsonGoddess.InjectorBase";

        private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";
        private const string JsonIncludeAttributeName = "System.Text.Json.Serialization.JsonIncludeAttribute";
        private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
        private const string JsonConverterAttributeName = "System.Text.Json.Serialization.JsonConverterAttribute";
        private const string JsonStringEnumMemberNameAttributeName = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";
        private const string JsonStringEnumConverterName = "System.Text.Json.Serialization.JsonStringEnumConverter";
        private const string JsonStringEnumConverterGenericName = "System.Text.Json.Serialization.JsonStringEnumConverter`1";
        private const string JsonPropertyOrderAttributeName = "System.Text.Json.Serialization.JsonPropertyOrderAttribute";
        private const string JsonSourceGenerationOptionsAttributeName = "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";
        private const string FlagsAttributeName = "System.FlagsAttribute";

        public readonly INamedTypeSymbol? Subject;
        public readonly INamedTypeSymbol? Exhauster;
        public readonly INamedTypeSymbol? Injector;
        public readonly INamedTypeSymbol? ExhausterBase;
        public readonly INamedTypeSymbol? InjectorBase;
        public readonly INamedTypeSymbol? JsonIgnore;
        public readonly INamedTypeSymbol? JsonInclude;
        public readonly INamedTypeSymbol? JsonPropertyName;
        public readonly INamedTypeSymbol? JsonConverter;
        public readonly INamedTypeSymbol? JsonStringEnumMemberName;
        public readonly INamedTypeSymbol? JsonStringEnumConverter;
        public readonly INamedTypeSymbol? JsonStringEnumConverterGeneric;
        public readonly INamedTypeSymbol? JsonPropertyOrder;
        public readonly INamedTypeSymbol? JsonSourceGenerationOptions;
        public readonly INamedTypeSymbol? Flags;

        public KnownSymbols(Compilation compilation)
        {
            Subject = compilation.GetTypeByMetadataName(SubjectAttribute);
            Exhauster = compilation.GetTypeByMetadataName(ExhausterAttribute);
            Injector = compilation.GetTypeByMetadataName(InjectorAttribute);
            ExhausterBase = compilation.GetTypeByMetadataName(ExhausterBaseName);
            InjectorBase = compilation.GetTypeByMetadataName(InjectorBaseName);
            JsonIgnore = compilation.GetTypeByMetadataName(JsonIgnoreAttributeName);
            JsonInclude = compilation.GetTypeByMetadataName(JsonIncludeAttributeName);
            JsonPropertyName = compilation.GetTypeByMetadataName(JsonPropertyNameAttributeName);
            JsonConverter = compilation.GetTypeByMetadataName(JsonConverterAttributeName);
            JsonStringEnumMemberName = compilation.GetTypeByMetadataName(JsonStringEnumMemberNameAttributeName);
            JsonStringEnumConverter = compilation.GetTypeByMetadataName(JsonStringEnumConverterName);
            JsonStringEnumConverterGeneric = compilation.GetTypeByMetadataName(JsonStringEnumConverterGenericName);
            JsonPropertyOrder = compilation.GetTypeByMetadataName(JsonPropertyOrderAttributeName);
            JsonSourceGenerationOptions = compilation.GetTypeByMetadataName(JsonSourceGenerationOptionsAttributeName);
            Flags = compilation.GetTypeByMetadataName(FlagsAttributeName);
        }

        public bool Has(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
            {
                return false;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Первый строковый аргумент атрибута, если атрибут есть. Так читаются и
        /// <c>[JsonPropertyName]</c>, и <c>[JsonStringEnumMemberName]</c>: у
        /// обоих ровно один аргумент и ровно один смысл.
        /// </summary>
        public string? ReadStringArgument(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
            {
                return null;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is string name)
                {
                    return name;
                }
            }

            return null;
        }

        public int ReadInt32Argument(ISymbol symbol, INamedTypeSymbol? attributeType, int fallback)
        {
            if (attributeType is null)
            {
                return fallback;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType)
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is int order)
                {
                    return order;
                }
            }

            return fallback;
        }

        /// <summary>
        /// <c>[JsonIgnore]</c> и его <c>Condition</c>. Возвращает <c>false</c>,
        /// если атрибута нет вовсе; значение <c>Condition</c> - в
        /// <paramref name="condition"/>, и отсутствие именованного аргумента
        /// означает <c>Always</c>, как и в самом атрибуте.
        /// </summary>
        public bool TryReadIgnore(ISymbol symbol, out int condition)
        {
            condition = JsonIgnoreConditionAlways;

            if (JsonIgnore is null)
            {
                return false;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, JsonIgnore))
                {
                    continue;
                }

                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "Condition" && named.Value.Value is int value)
                    {
                        condition = value;
                    }
                }

                return true;
            }

            return false;
        }

        //значения JsonIgnoreCondition; читаются из метаданных числом, потому что
        //enum-константа чужой сборки приезжает сюда именно числом
        public const int JsonIgnoreConditionNever = 0;
        public const int JsonIgnoreConditionAlways = 1;
        public const int JsonIgnoreConditionWhenWritingDefault = 2;
        public const int JsonIgnoreConditionWhenWritingNull = 3;
    }
}
