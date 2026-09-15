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
        private const string ListNamespace = "System.Collections.Generic";

        /// <summary>
        /// Класс, зарегистрированный <c>[JsonSubject]</c>, опознаётся по карте,
        /// а не по форме: регистрация - это явное решение автора, и подменять
        /// его догадкой «раз это класс, значит обслужим» нельзя. Незнакомый
        /// класс - отказ с указанием, что именно дописать.
        /// </summary>
        public static bool TryBind(
            ITypeSymbol type,
            IReadOnlyDictionary<ISymbol, string> subjects,
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

            //Nullable<T> над всем остальным не бывает: субъект и коллекция -
            //ссылочные типы, и null у них выражается самим типом
            if (isNullableValueType)
            {
                refusal = "Nullable<> is only supported over builtin value types";
                return false;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                refusal = "enums are not supported yet; they arrive later in phase 4";
                return false;
            }

            if (subjects.TryGetValue(type, out var subjectSuffix))
            {
                value = new ValueModel(
                    ValueForm.Subject,
                    default,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    subjectSuffix,
                    null,
                    true
                    );
                return true;
            }

            if (type is IArrayTypeSymbol array)
            {
                if (!array.IsSZArray)
                {
                    refusal = "multidimensional arrays have no JSON form";
                    return false;
                }

                if (!TryBind(array.ElementType, subjects, out var arrayElement, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.Array,
                    default,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "ArrayOf_" + arrayElement!.MethodSuffix,
                    arrayElement,
                    true
                    );
                return true;
            }

            if (IsList(type, out var listElementType))
            {
                if (!TryBind(listElementType!, subjects, out var listElement, out refusal))
                {
                    return false;
                }

                value = new ValueModel(
                    ValueForm.List,
                    default,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "ListOf_" + listElement!.MethodSuffix,
                    listElement,
                    true
                    );
                return true;
            }

            refusal = type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct
                ? "the type is not registered; add [JsonSubject(typeof(" + type.Name + "), false)] to the host, or mark the member [JsonIgnore]"
                : "only builtin types, registered [JsonSubject] classes, List<T> and T[] are supported; dictionaries, interfaces and other collections arrive later";
            return false;
        }

        private static bool IsList(ITypeSymbol type, out ITypeSymbol? element)
        {
            element = null;

            if (type is not INamedTypeSymbol { IsGenericType: true } named)
            {
                return false;
            }

            var definition = named.OriginalDefinition;
            if (definition.MetadataName != ListMetadataName
                || definition.ContainingNamespace?.ToDisplayString() != ListNamespace)
            {
                return false;
            }

            element = named.TypeArguments[0];
            return true;
        }

        /// <summary>
        /// Все различные коллекции поддерева, включая вложенные, в порядке
        /// «сначала внешняя». Порядок фиксирован не ради красоты: текст
        /// порождаемого кода - предмет тестов, и он обязан не зависеть от того,
        /// в каком порядке Roslyn вернул члены.
        /// </summary>
        public static void CollectCollections(ValueModel value, Dictionary<string, ValueModel> into)
        {
            if (!value.IsCollection)
            {
                return;
            }

            if (!into.ContainsKey(value.MethodSuffix))
            {
                into.Add(value.MethodSuffix, value);
            }

            CollectCollections(value.Element!, into);
        }
    }
}
