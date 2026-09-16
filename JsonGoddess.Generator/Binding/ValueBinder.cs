using System.Collections.Generic;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Тип члена - в <see cref="ValueModel"/>. Здесь и проходит граница фазы:
    /// всё, что не builtin, не зарегистрированный субъект и не
    /// <c>List&lt;T&gt;</c>/<c>T[]</c>, отвергается с объяснением, а не
    /// обслуживается приблизительно.
    ///
    /// Рекурсия по элементу коллекции - не украшение: <c>List&lt;List&lt;int&gt;&gt;</c>
    /// в JSON обычная форма, и поддерживать её отдельным случаем значило бы
    /// поддерживать ровно два уровня вложенности.
    /// </summary>
    public static class ValueBinder
    {
        private const string ListMetadataName = "List`1";
        private const string DictionaryMetadataName = "Dictionary`2";

        /// <summary>
        /// Класс, зарегистрированный <c>[JsonSubject]</c>, опознаётся по карте,
        /// а не по форме: регистрация - это явное решение автора, и подменять
        /// его догадкой «раз это класс, значит обслужим» нельзя. Незнакомый
        /// класс - отказ с указанием, что именно дописать.
        /// </summary>
        public static bool TryBind(
            ITypeSymbol type,
            IReadOnlyDictionary<ISymbol, string> subjects,
            KnownSymbols known,
            out ValueModel? value,
            out string refusal
            )
        {
            value = null;
            refusal = string.Empty;

            var isNullableValueType = false;
            if (type is INamedTypeSymbol { IsGenericType: true } nullable
                && nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            {
                isNullableValueType = true;
                type = nullable.TypeArguments[0];
            }

            if (BuiltinTypes.TryBind(type, out var kind, out var isReferenceType))
            {
                value = new ValueModel(
                    ValueForm.Builtin,
                    kind,
                    BuiltinTypes.GetTypeName(kind),
                    kind.ToString(),
                    null,
                    isNullableValueType || isReferenceType
                    );
                return true;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                if (!TryBindEnum((INamedTypeSymbol)type, known, out var enumModel, out var isStringEnum, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.Enum,
                    enumModel!.Underlying,
                    enumModel.FullName,
                    enumModel.MethodSuffix,
                    null,
                    isNullableValueType,
                    enumModel,
                    isStringEnum
                    );
                return true;
            }

            //Субъект стоит раньше отказа по Nullable<>, потому что субъектом
            //может быть структура, и тогда Nullable<> над ним - обычное дело.
            //У класса IsNullable всегда true: ссылка описывает null сама.
            if (subjects.TryGetValue(type, out var subjectSuffix))
            {
                value = new ValueModel(
                    ValueForm.Subject,
                    default,
                    known.FullName(type),
                    subjectSuffix,
                    null,
                    type.IsReferenceType || isNullableValueType,
                    isValueType: type.IsValueType
                    );
                return true;
            }

            //Nullable<T> над остальным не бывает: коллекции ссылочные, и null у
            //них выражается самим типом
            if (isNullableValueType)
            {
                refusal = "Nullable<> is only supported over builtin value types, enums and registered struct subjects";
                return false;
            }

            if (type is IArrayTypeSymbol array)
            {
                if (!array.IsSZArray)
                {
                    refusal = "multidimensional arrays have no JSON form";
                    return false;
                }

                if (!TryBind(array.ElementType, subjects, known, out var arrayElement, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.Array,
                    default,
                    known.FullName(type),
                    "ArrayOf_" + arrayElement!.MethodSuffix,
                    arrayElement,
                    true
                    );
                return true;
            }

            if (IsDictionary(type, out var keyType, out var valueType))
            {
                if (keyType!.SpecialType != SpecialType.System_String)
                {
                    refusal = "only Dictionary<string, V> is supported; other key types arrive later";
                    return false;
                }

                if (!TryBind(valueType!, subjects, known, out var dictionaryValue, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.Dictionary,
                    default,
                    known.FullName(type),
                    "MapOf_" + dictionaryValue!.MethodSuffix,
                    dictionaryValue,
                    true
                    );
                return true;
            }

            if (IsList(type, out var listElementType))
            {
                if (!TryBind(listElementType!, subjects, known, out var listElement, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.List,
                    default,
                    known.FullName(type),
                    "ListOf_" + listElement!.MethodSuffix,
                    listElement,
                    true
                    );
                return true;
            }

            refusal = type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct
                ? "the type is not registered; add [JsonSubject(typeof(" + type.Name + "), false)] to the host, or mark the member [JsonIgnore]"
                : "only builtin types, registered [JsonSubject] classes, List<T>, T[] and Dictionary<string, V> are supported; interfaces and other collections arrive later";
            return false;
        }

        /// <summary>
        /// Enum. Числовое представление - по умолчанию, как у эталона; строковое -
        /// при <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> на самом
        /// типе, в любой из двух форм конвертера.
        ///
        /// Два отказа здесь не от лени. <c>[Flags]</c> у эталона в строковом
        /// режиме даёт <c>"Read, Write"</c> - комбинирование, у которого своя
        /// грамматика, и повторять её вслепую нельзя. Имя члена вне ASCII он
        /// сворачивает по регистру средствами Unicode, а мы сворачиваем по
        /// ASCII, и на таком имени совпадение зависело бы от алфавита.
        /// </summary>
        private static bool TryBindEnum(
            INamedTypeSymbol type,
            KnownSymbols known,
            out EnumModel? model,
            out bool isStringEnum,
            out string refusal
            )
        {
            model = null;
            isStringEnum = false;
            refusal = string.Empty;

            if (!BuiltinTypes.TryBind(type.EnumUnderlyingType!, out var underlying, out _))
            {
                refusal = "the underlying type of the enum is not supported";
                return false;
            }

            isStringEnum = HasStringEnumConverter(type, known, out var unsupportedConverter);
            if (unsupportedConverter is not null)
            {
                refusal = "type '" + unsupportedConverter
                    + "' is registered as a converter, and JsonGoddess cannot reproduce an arbitrary JsonConverter";
                return false;
            }

            if (isStringEnum && known.Has(type, known.Flags))
            {
                refusal = "[Flags] enums in string form are not supported: System.Text.Json combines them into "
                    + "\"A, B\", and that grammar is not reproduced here";
                return false;
            }

            var members = new List<EnumMemberModel>();
            var byValue = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var member in type.GetMembers())
            {
                if (member is not IFieldSymbol { IsConst: true, HasConstantValue: true } field)
                {
                    continue;
                }

                var custom = known.ReadStringArgument(field, known.JsonStringEnumMemberName);
                var jsonName = custom ?? field.Name;

                if (isStringEnum && !IsAscii(jsonName))
                {
                    refusal = "member '" + field.Name + "' maps to name '" + jsonName
                        + "', which is not ASCII; System.Text.Json matches such names ignoring case by Unicode rules, "
                        + "and JsonGoddess folds case by ASCII rules only";
                    return false;
                }

                //Два имени на одно значение в строковом режиме - вопрос без
                //ответа: какое из них напишет эталон, не определено и в самом
                //BCL (Enum.GetName не обещает постоянства). Отказ здесь честнее
                //выбора наугад, который выглядел бы как совместимость.
                if (isStringEnum && !byValue.Add(field.ConstantValue?.ToString() ?? string.Empty))
                {
                    refusal = "members '" + field.Name + "' and an earlier one share the underlying value "
                        + field.ConstantValue + "; in string form there is no defined answer to which name is written, "
                        + "so JsonGoddess refuses instead of guessing";
                    return false;
                }

                members.Add(new EnumMemberModel(field.Name, jsonName, custom is not null));
            }

            model = new EnumModel(
                known.FullName(type),
                type.ToDisplayString().Replace('.', '_'),
                underlying,
                members
                );
            return true;
        }

        private static bool HasStringEnumConverter(ITypeSymbol type, KnownSymbols known, out string? unsupported)
        {
            unsupported = null;

            if (known.JsonConverter is null)
            {
                return false;
            }

            foreach (var attribute in type.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonConverter)
                    || attribute.ConstructorArguments.Length < 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol converter)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(converter, known.JsonStringEnumConverter)
                    || SymbolEqualityComparer.Default.Equals(
                        converter.OriginalDefinition,
                        known.JsonStringEnumConverterGeneric))
                {
                    return true;
                }

                unsupported = converter.ToDisplayString();
                return false;
            }

            return false;
        }

        private static bool IsAscii(string value)
        {
            foreach (var c in value)
            {
                if (c > (char)0x7F)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsList(ITypeSymbol type, out ITypeSymbol? element)
        {
            element = null;

            if (!IsGeneric(type, ListMetadataName, out var named))
            {
                return false;
            }

            element = named!.TypeArguments[0];
            return true;
        }

        private static bool IsDictionary(ITypeSymbol type, out ITypeSymbol? key, out ITypeSymbol? value)
        {
            key = null;
            value = null;

            if (!IsGeneric(type, DictionaryMetadataName, out var named))
            {
                return false;
            }

            key = named!.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }

        /// <summary>
        /// Опознание по метаданным, а не по напечатанному имени: строка вида
        /// <c>global::System.Collections.Generic.List&lt;T&gt;</c> зависит от
        /// того, как Roslyn назовёт параметр типа, и сравнивать с ней значило бы
        /// опираться на форматирование.
        /// </summary>
        private static bool IsGeneric(ITypeSymbol type, string metadataName, out INamedTypeSymbol? named)
        {
            named = null;

            if (type is not INamedTypeSymbol { IsGenericType: true } candidate)
            {
                return false;
            }

            var definition = candidate.OriginalDefinition;
            if (definition.MetadataName != metadataName
                || !IsCollectionsGeneric(definition.ContainingNamespace))
            {
                return false;
            }

            named = candidate;
            return true;
        }

        /// <summary>
        /// <c>System.Collections.Generic</c> - по звеньям, а не строкой:
        /// <c>ToDisplayString</c> собирал бы её заново на каждую коллекцию
        /// каждого члена (§16.2 плана), а сравнить три коротких имени дешевле
        /// в разы.
        /// </summary>
        private static bool IsCollectionsGeneric(INamespaceSymbol? ns)
        {
            return ns is { Name: "Generic", }
                && ns.ContainingNamespace is { Name: "Collections", } collections
                && collections.ContainingNamespace is { Name: "System", } system
                && system.ContainingNamespace is { IsGlobalNamespace: true, };
        }

        /// <summary>
        /// Все различные коллекции поддерева, включая вложенные, в порядке
        /// «сначала внешняя». Порядок фиксирован не ради красоты: текст
        /// порождаемого кода - предмет тестов, и он обязан не зависеть от того,
        /// в каком порядке Roslyn вернул члены.
        /// </summary>
        public static void CollectValues(
            ValueModel value,
            Dictionary<string, ValueModel> collections,
            Dictionary<string, EnumModel> stringEnums,
            Dictionary<string, ValueModel> scalars
            )
        {
            if (value.Form == ValueForm.Enum)
            {
                if (value.IsStringEnum)
                {
                    if (!stringEnums.ContainsKey(value.MethodSuffix))
                    {
                        stringEnums.Add(value.MethodSuffix, value.Enum!);
                    }

                    return;
                }

                //числовой enum читается читателем своего подлежащего типа, и
                //nullability снимается снаружи: приведение к enum'у само по
                //себе null не пропускает
                AddScalar(scalars, value.Builtin, false);
                return;
            }

            if (value.Form == ValueForm.Builtin)
            {
                AddScalar(scalars, value.Builtin, value.IsNullable);
                return;
            }

            if (!value.IsCollection)
            {
                return;
            }

            if (!collections.ContainsKey(value.MethodSuffix))
            {
                collections.Add(value.MethodSuffix, value);
            }

            CollectValues(value.Element!, collections, stringEnums, scalars);
        }

        private static void AddScalar(Dictionary<string, ValueModel> scalars, BuiltinKind kind, bool isNullable)
        {
            var suffix = BuiltinTypes.MethodSuffix(kind, isNullable);
            if (scalars.ContainsKey(suffix))
            {
                return;
            }

            scalars.Add(
                suffix,
                new ValueModel(ValueForm.Builtin, kind, BuiltinTypes.GetTypeName(kind), suffix, null, isNullable)
                );
        }
    }
}
