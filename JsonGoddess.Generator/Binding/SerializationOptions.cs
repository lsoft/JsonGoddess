using System.Collections.Generic;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Model;
using JsonGoddess.Generator.Shared;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Настройки хоста, прочитанные из <c>[JsonSourceGenerationOptions]</c>.
    ///
    /// Атрибут взят чужой намеренно, и это продолжение решения, принятого ещё в
    /// фазе 2: состав членов, переименование и пропуск мы читаем из атрибутов
    /// <c>System.Text.Json</c>, а не из своих. Здесь у эталона та же задача, что
    /// у нас, - настроить генератор, у которого нет объекта опций, - и он её
    /// уже решил: атрибут на классе контекста плюс перечисление известных
    /// политик вместо экземпляра <c>JsonNamingPolicy</c>.
    ///
    /// Своё <c>[JsonNaming]</c> рядом с этим было бы вторым словарём для того же
    /// понятия.
    ///
    /// Цена решения - <b>обязательство разобрать каждое свойство</b>. Их
    /// двадцать семь, и молча пропустить хоть одно нельзя: <c>WriteIndented</c>
    /// или <c>DefaultIgnoreCondition</c>, оставленные без внимания, дали бы
    /// документ, которого эталон не выдаёт, и не сказали бы об этом. Поэтому
    /// всё, что не реализовано, - отказ <c>JGD028</c>, а список отказов и есть
    /// честный перечень того, чего мы пока не умеем.
    /// </summary>
    public sealed class SerializationOptions
    {
        public static readonly SerializationOptions Default = new SerializationOptions(
            JsonNamingStyle.None,
            JsonNamingStyle.None
            );

        public JsonNamingStyle PropertyNaming { get; }

        public JsonNamingStyle DictionaryKeyNaming { get; }

        public SerializationOptions(JsonNamingStyle propertyNaming, JsonNamingStyle dictionaryKeyNaming)
        {
            PropertyNaming = propertyNaming;
            DictionaryKeyNaming = dictionaryKeyNaming;
        }

        /// <summary>
        /// Свойства, которые мы исполняем.
        /// </summary>
        private const string PropertyNamingPolicy = "PropertyNamingPolicy";
        private const string DictionaryKeyPolicy = "DictionaryKeyPolicy";

        /// <summary>
        /// Свойство, которое можно не исполнять, ничего не нарушив: оно
        /// описывает, какой код порождать <b>их</b> генератору, и документа не
        /// касается вовсе.
        /// </summary>
        private const string GenerationMode = "GenerationMode";

        public static SerializationOptions Read(
            INamedTypeSymbol host,
            KnownSymbols known,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            if (known.JsonSourceGenerationOptions is null)
            {
                return Default;
            }

            var location = LocationInfo.From(host);

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonSourceGenerationOptions))
                {
                    continue;
                }

                //конструктор с JsonSerializerDefaults.Web включает разом
                //camelCase, нечувствительность к регистру имён и чтение чисел из
                //строк; принять его значило бы принять три решения вместо
                //одного, из которых два мы не исполняем
                if (attribute.ConstructorArguments.Length > 0)
                {
                    Refuse(diagnostics, location, host, "the JsonSerializerDefaults constructor",
                        "it turns on case-insensitive property matching and reading numbers from strings, "
                        + "neither of which JsonGoddess implements; set PropertyNamingPolicy explicitly instead",
                        ref failed);
                }

                var property = JsonNamingStyle.None;
                var dictionaryKey = JsonNamingStyle.None;

                foreach (var named in attribute.NamedArguments)
                {
                    switch (named.Key)
                    {
                        case PropertyNamingPolicy:
                            property = ReadStyle(named.Value);
                            break;

                        case DictionaryKeyPolicy:
                            dictionaryKey = ReadStyle(named.Value);
                            break;

                        case GenerationMode:
                            break;

                        default:
                            Refuse(diagnostics, location, host, "'" + named.Key + "'",
                                "JsonGoddess does not implement it yet, and applying it silently is not an option: "
                                + "the document would differ from the one System.Text.Json produces",
                                ref failed);
                            break;
                    }
                }

                return new SerializationOptions(property, dictionaryKey);
            }

            return Default;
        }

        private static JsonNamingStyle ReadStyle(TypedConstant value)
        {
            return value.Value is int number
                && number >= (int)JsonNamingStyle.None
                && number <= (int)JsonNamingStyle.KebabCaseUpper
                    ? (JsonNamingStyle)number
                    : JsonNamingStyle.None;
        }

        private static void Refuse(
            List<DiagnosticInfo> diagnostics,
            LocationInfo? location,
            INamedTypeSymbol host,
            string option,
            string reason,
            ref bool failed
            )
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    JsonGoddessDiagnostics.SerializationOptionIsNotSupportedId,
                    location,
                    option,
                    host.ToDisplayString(),
                    reason
                    )
                );
            failed = true;
        }
    }
}
