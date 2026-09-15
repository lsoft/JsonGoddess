using System;
using System.Collections.Generic;
using System.Globalization;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// <c>[JsonDerivedType]</c> и <c>[JsonPolymorphic]</c>.
    ///
    /// Правила сняты прогоном эталона, и три из них неочевидны настолько, что
    /// на догадке здесь ошибиться было бы легко:
    ///
    /// <list type="number">
    /// <item><b>Совпадение по точному типу, а не по <c>is</c>.</b>
    /// Незарегистрированный потомок зарегистрированного потомка
    /// (<c>Poodle : Dog</c>) - это <c>NotSupportedException</c>, а не запись
    /// как <c>Dog</c>. Значит диспетчер на записи обязан сравнивать
    /// <c>GetType()</c>, а не примерять <c>is</c>.</item>
    /// <item><b>Набор производных не транзитивен.</b> <c>Bottom</c>,
    /// объявленный на <c>Middle</c>, при записи как <c>Top</c> - отказ, хотя
    /// <c>Middle</c> на <c>Top</c> объявлен. Каждая база знает ровно свой
    /// список.</item>
    /// <item><b>Дискриминатор пишется только тогда, когда статический тип -
    /// база.</b> <c>Serialize(new Dog(...))</c> с <c>T = Dog</c> даёт
    /// <c>{"Barks":...}</c> без него: у <c>Dog</c> своих производных нет,
    /// и полиморфным он не является.</item>
    /// </list>
    ///
    /// Плюс: база получает дискриминатор, только если объявлена производной от
    /// самой себя; иначе пишется как обычный объект. Значение дискриминатора
    /// бывает строкой и числом. Читается дискриминатор <b>только первым
    /// свойством</b> - у эталона иначе <c>JsonException</c>, и это его
    /// требование мы повторяем, а не изобретаем.
    /// </summary>
    public static class PolymorphismBinder
    {
        public const string DefaultDiscriminatorName = "$type";

        private const string TypeDiscriminatorPropertyName = "TypeDiscriminatorPropertyName";

        public static bool TryBind(
            INamedTypeSymbol subject,
            IReadOnlyDictionary<ISymbol, string> subjects,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            out IReadOnlyList<DerivedTypeModel> derived,
            out string discriminatorName
            )
        {
            derived = Array.Empty<DerivedTypeModel>();
            discriminatorName = DefaultDiscriminatorName;

            if (known.JsonDerivedType is null)
            {
                return true;
            }

            var list = new List<DerivedTypeModel>();

            foreach (var attribute in subject.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonDerivedType))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 0
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol derivedType)
                {
                    Refuse(subject, location, diagnostics, "a [JsonDerivedType] attribute has no type argument");
                    return false;
                }

                if (!subjects.TryGetValue(derivedType, out var suffix))
                {
                    Refuse(subject, location, diagnostics,
                        "derived type '" + derivedType.Name + "' is declared by [JsonDerivedType] but is not registered; "
                        + "add [JsonSubject(typeof(" + derivedType.Name + "), false)] to the host");
                    return false;
                }

                if (!IsSameOrDerivedFrom(derivedType, subject))
                {
                    Refuse(subject, location, diagnostics,
                        "type '" + derivedType.Name + "' is declared by [JsonDerivedType] but does not derive from it");
                    return false;
                }

                if (!TryDiscriminator(attribute, subject, location, diagnostics, out var literal))
                {
                    return false;
                }

                list.Add(
                    new DerivedTypeModel(
                        derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        suffix,
                        literal
                        )
                    );
            }

            if (list.Count == 0)
            {
                return true;
            }

            if (!TryReadPolymorphicOptions(subject, known, location, diagnostics, ref discriminatorName))
            {
                return false;
            }

            //дискриминатор сравнивается с сырыми байтами документа, как и имена
            //членов, поэтому к нему та же мерка: имя, которое пришлось бы
            //экранировать, приезжало бы в двух видах
            if (!JsonNameUtf8.TryEncode(discriminatorName, out _))
            {
                Refuse(subject, location, diagnostics,
                    "the type discriminator property name '" + discriminatorName
                    + "' contains a character that has to be escaped in JSON");
                return false;
            }

            derived = list;
            return true;
        }

        /// <summary>
        /// Значение дискриминатора, готовое к печати в документ.
        ///
        /// <c>null</c> - законный случай: <c>[JsonDerivedType(typeof(D))]</c>
        /// без значения эталон пишет без дискриминатора вовсе, то есть
        /// односторонне. Повторяем это, а не отказываем: документ выходит тот
        /// же.
        /// </summary>
        private static bool TryDiscriminator(
            AttributeData attribute,
            INamedTypeSymbol subject,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            out string? literal
            )
        {
            literal = null;

            if (attribute.ConstructorArguments.Length < 2)
            {
                return true;
            }

            var value = attribute.ConstructorArguments[1].Value;

            switch (value)
            {
                case string text:
                {
                    if (!JsonNameUtf8.TryEncode(text, out _))
                    {
                        Refuse(subject, location, diagnostics,
                            "type discriminator '" + text + "' contains a character that has to be escaped in JSON");
                        return false;
                    }

                    literal = "\"" + text + "\"";
                    return true;
                }

                case int number:
                {
                    literal = number.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                default:
                {
                    Refuse(subject, location, diagnostics,
                        "type discriminators are only supported as a string or an int");
                    return false;
                }
            }
        }

        private static bool TryReadPolymorphicOptions(
            INamedTypeSymbol subject,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref string discriminatorName
            )
        {
            if (known.JsonPolymorphic is null)
            {
                return true;
            }

            foreach (var attribute in subject.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonPolymorphic))
                {
                    continue;
                }

                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == TypeDiscriminatorPropertyName)
                    {
                        if (named.Value.Value is string name && name.Length > 0)
                        {
                            discriminatorName = name;
                        }

                        continue;
                    }

                    //то же правило, что и у [JsonSourceGenerationOptions]:
                    //свойство, которое мы не исполняем, - отказ, а не пропуск
                    Refuse(subject, location, diagnostics,
                        "[JsonPolymorphic] property '" + named.Key + "' is not implemented, and applying it silently "
                        + "is not an option: the document would differ from the one System.Text.Json produces");
                    return false;
                }
            }

            return true;
        }

        private static bool IsSameOrDerivedFrom(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
        {
            for (var current = candidate; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Refuse(
            INamedTypeSymbol subject,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            string reason
            )
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    JsonGoddessDiagnostics.SubjectIsNotSupportedId,
                    location,
                    subject.ToDisplayString(),
                    reason
                    )
                );
        }
    }
}
