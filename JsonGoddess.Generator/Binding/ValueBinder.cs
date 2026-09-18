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

        //Интерфейсы фазы 6 (§9.10 плана). Объявленный тип члена - это в
        //точности один из них или ни один: член не может «быть одновременно»
        //IList<T> и ICollection<T>, поэтому, в отличие от связывания субъекта,
        //который сам является коллекцией (HostBinder), здесь не нужен обход
        //AllInterfaces - метаданное имя сравнивается напрямую с объявленным типом.
        private const string IListMetadataName = "IList`1";
        private const string IReadOnlyListMetadataName = "IReadOnlyList`1";
        private const string ICollectionMetadataName = "ICollection`1";
        private const string IEnumerableMetadataName = "IEnumerable`1";
        private const string IReadOnlyCollectionMetadataName = "IReadOnlyCollection`1";
        private const string IDictionaryMetadataName = "IDictionary`2";
        private const string IReadOnlyDictionaryMetadataName = "IReadOnlyDictionary`2";

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
                    "ArrayOf_" + ElementSuffix(array.ElementType, arrayElement!),
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
                    "MapOf_" + ElementSuffix(valueType!, dictionaryValue!),
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
                    "ListOf_" + ElementSuffix(listElementType!, listElement!),
                    listElement,
                    true
                    );
                return true;
            }

            //Фаза 6 (§9.10 плана): интерфейсы на месте члена. Читателю всё
            //равно нечем строить результат, кроме конкретного типа - List<T>
            //для всех пяти интерфейсов списка, Dictionary<string,V> для двух
            //словарных, - и это ровно то, что подставляет сам эталон
            //(проверено пробой: IReadOnlyDictionary<string,int> на чтении
            //оказывается Dictionary<string,int>). Оттого у них свой
            //ConstructTypeName, а TypeName остаётся объявленным интерфейсом.
            if (TryBindInterfaceDictionary(type, subjects, known, out value, out refusal))
            {
                return value is not null;
            }

            if (TryBindInterfaceList(type, subjects, known, out value, out refusal))
            {
                return value is not null;
            }

            refusal = type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct
                ? "the type is not registered; add [JsonSubject(typeof(" + type.Name + "), false)] to the host, or mark the member [JsonIgnore]"
                : "only builtin types, registered [JsonSubject] classes/structs, List<T>, T[], Dictionary<string, V>, "
                    + "IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, "
                    + "IDictionary<string, V> and IReadOnlyDictionary<string, V> are supported; other collections "
                    + "(sets, queues, stacks, non-string-keyed dictionaries, multidimensional arrays) are not";
            return false;
        }

        /// <summary>
        /// Суффикс элемента в имени метода коллекции - с учётом
        /// <c>Nullable&lt;&gt;</c>.
        ///
        /// <para>
        /// Собственный <see cref="ValueModel.MethodSuffix"/> элемента о
        /// nullability молчит: он именует <b>вид</b> значения
        /// (<c>Int32</c>, <c>MyEnum</c>, субъект), а разрешён ли на его месте
        /// <c>null</c> - свойство места, и живёт оно в
        /// <see cref="ValueModel.IsNullable"/>. Для скаляра этого хватает:
        /// читатель скаляра именуется отдельно и <c>_OrNull</c> к себе
        /// приписывает сам. Для коллекции - нет: <c>int[]</c> и <c>int?[]</c>
        /// это два разных типа с двумя разными методами, а имя у них выходило
        /// одно - <c>ArrayOf_Int32</c>.
        /// </para>
        ///
        /// <para>
        /// Пока хост печатается на один граф, столкнуться им негде: два таких
        /// члена редко живут в одном дереве типов. У Compat-слоя хост
        /// <b>общий на всю сборку</b>, и там это случилось сразу - на чужом
        /// корпусе, где <c>SimpleTestClass</c> с <c>int[]</c> и
        /// <c>SimpleTestClassWithNullables</c> с <c>int?[]</c> приехали в один
        /// файл. Нашлось компиляцией порождённого кода (CS1503), то есть самым
        /// дешёвым из возможных способов; молча разойтись документы тут не
        /// могли бы, но это везение, а не устройство.
        /// </para>
        ///
        /// <para>
        /// Различается ровно <c>Nullable&lt;T&gt;</c>, а не nullability вообще:
        /// <c>string[]</c> и <c>string?[]</c> - один и тот же тип в рантайме,
        /// и один и тот же метод. Приписывать суффикс и им значило бы
        /// разделить то, что делить нечем.
        /// </para>
        /// </summary>
        private static string ElementSuffix(ITypeSymbol element, ValueModel model)
        {
            return element is INamedTypeSymbol { IsGenericType: true, } named
                && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
                    ? model.MethodSuffix + "_OrNull"
                    : model.MethodSuffix;
        }

        /// <summary>
        /// <c>IDictionary&lt;string, V&gt;</c> / <c>IReadOnlyDictionary&lt;string, V&gt;</c>.
        /// <c>false</c> без <paramref name="value"/> и без <paramref name="refusal"/>
        /// означает «тип не один из этих двух интерфейсов вовсе» - продолжать
        /// связывание дальше, это не отказ.
        /// </summary>
        private static bool TryBindInterfaceDictionary(
            ITypeSymbol type,
            IReadOnlyDictionary<ISymbol, string> subjects,
            KnownSymbols known,
            out ValueModel? value,
            out string refusal
            )
        {
            value = null;
            refusal = string.Empty;

            string prefix;
            if (IsGeneric(type, IDictionaryMetadataName, out var named))
            {
                prefix = "IDictionaryOf_";
            }
            else if (IsGeneric(type, IReadOnlyDictionaryMetadataName, out named))
            {
                prefix = "IReadOnlyDictionaryOf_";
            }
            else
            {
                return false;
            }

            var keyType = named!.TypeArguments[0];
            var valueType = named.TypeArguments[1];

            if (keyType.SpecialType != SpecialType.System_String)
            {
                refusal = "only a string-keyed " + named.OriginalDefinition.Name + " is supported; other key types arrive later";
                value = null;
                return true;
            }

            if (!TryBind(valueType, subjects, known, out var dictionaryValue, out refusal))
            {
                value = null;
                return true;
            }

            value = new ValueModel(
                ValueForm.Dictionary,
                default,
                known.FullName(type),
                prefix + ElementSuffix(valueType, dictionaryValue!),
                dictionaryValue,
                true,
                constructTypeName: "global::System.Collections.Generic.Dictionary<string, " + dictionaryValue!.Declaration + ">"
                );
            return true;
        }

        /// <summary>
        /// <c>IList&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
        /// <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
        /// <c>IReadOnlyCollection&lt;T&gt;</c>.
        ///
        /// Первые два получают <see cref="ValueForm.List"/>: у обоих
        /// гарантирован индексатор и <c>Count</c>, значит писать их можно тем
        /// же индексированным циклом, что и <c>List&lt;T&gt;</c>. Остальным
        /// трём индексатор не гарантирован (<c>ICollection&lt;T&gt;</c> его
        /// вовсе не объявляет), и им достаётся <see cref="ValueForm.Enumerable"/> -
        /// запись <c>foreach</c>'ем со своим счётчиком, как у словаря, но без
        /// ключа. Чтение при этом одно и то же для всех пяти: <c>List&lt;T&gt;</c>
        /// строится и наполняется через <c>Add</c>, индексатор ему не нужен.
        /// </summary>
        private static bool TryBindInterfaceList(
            ITypeSymbol type,
            IReadOnlyDictionary<ISymbol, string> subjects,
            KnownSymbols known,
            out ValueModel? value,
            out string refusal
            )
        {
            value = null;
            refusal = string.Empty;

            ValueForm form;
            string prefix;
            INamedTypeSymbol? named;

            if (IsGeneric(type, IListMetadataName, out named))
            {
                form = ValueForm.List;
                prefix = "IListOf_";
            }
            else if (IsGeneric(type, IReadOnlyListMetadataName, out named))
            {
                form = ValueForm.List;
                prefix = "IReadOnlyListOf_";
            }
            else if (IsGeneric(type, ICollectionMetadataName, out named))
            {
                form = ValueForm.Enumerable;
                prefix = "ICollectionOf_";
            }
            else if (IsGeneric(type, IEnumerableMetadataName, out named))
            {
                form = ValueForm.Enumerable;
                prefix = "IEnumerableOf_";
            }
            else if (IsGeneric(type, IReadOnlyCollectionMetadataName, out named))
            {
                form = ValueForm.Enumerable;
                prefix = "IReadOnlyCollectionOf_";
            }
            else
            {
                return false;
            }

            if (!TryBind(named!.TypeArguments[0], subjects, known, out var element, out refusal))
            {
                value = null;
                return true;
            }

            value = new ValueModel(
                form,
                default,
                known.FullName(type),
                prefix + ElementSuffix(named!.TypeArguments[0], element!),
                element,
                true,
                constructTypeName: "global::System.Collections.Generic.List<" + element!.Declaration + ">"
                );
            return true;
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

        /// <summary>
        /// Какие типы должны быть зарегистрированы субъектами, чтобы
        /// <see cref="TryBind"/> справился с типом члена.
        ///
        /// <para>
        /// Существует ради Compat-слоя (§10): там список субъектов никто не
        /// пишет руками, и транзитивное замыкание графа приходится считать
        /// самим. Живёт здесь, а не в Compat-биндере, потому что обязано
        /// разбирать тип ровно теми же шагами, что и <see cref="TryBind"/>
        /// прямо над ним, - разъехавшись, они дали бы не отказ, а
        /// незарегистрированный тип и непонятную ошибку компилятора поверх.
        /// </para>
        ///
        /// <para>
        /// Про то, <b>можно</b> ли обслужить найденный тип, здесь не судят
        /// вовсе: находка - это кандидат, а приговор выносит связывание.
        /// Незнакомая форма (множество, очередь, словарь не по строке) не даёт
        /// кандидатов и уедет в отказ там же, где уехала бы у обычного хоста.
        /// </para>
        /// </summary>
        public static void CollectSubjectCandidates(
            ITypeSymbol type, ICollection<INamedTypeSymbol> into
            )
        {
            if (type is INamedTypeSymbol { IsGenericType: true } nullable
                && nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            {
                CollectSubjectCandidates(nullable.TypeArguments[0], into);
                return;
            }

            if (BuiltinTypes.TryBind(type, out _, out _) || type.TypeKind == TypeKind.Enum)
            {
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                CollectSubjectCandidates(array.ElementType, into);
                return;
            }

            if (IsDictionary(type, out _, out var dictionaryValue))
            {
                CollectSubjectCandidates(dictionaryValue!, into);
                return;
            }

            if (IsList(type, out var listElement))
            {
                CollectSubjectCandidates(listElement!, into);
                return;
            }

            foreach (var name in new[]
                {
                    IDictionaryMetadataName, IReadOnlyDictionaryMetadataName,
                })
            {
                if (IsGeneric(type, name, out var dictionaryInterface))
                {
                    CollectSubjectCandidates(dictionaryInterface!.TypeArguments[1], into);
                    return;
                }
            }

            foreach (var name in new[]
                {
                    IListMetadataName, IReadOnlyListMetadataName, ICollectionMetadataName,
                    IEnumerableMetadataName, IReadOnlyCollectionMetadataName,
                })
            {
                if (IsGeneric(type, name, out var listInterface))
                {
                    CollectSubjectCandidates(listInterface!.TypeArguments[0], into);
                    return;
                }
            }

            if (type is INamedTypeSymbol named
                && (named.TypeKind == TypeKind.Class || named.TypeKind == TypeKind.Struct))
            {
                //аннотация nullability снимается: она приехала от объявления
                //члена (`Address? ShipTo`), а суффиксы методов строятся по
                //ToDisplayString(), и `?` в имени метода - это не имя метода
                into.Add((INamedTypeSymbol)named.WithNullableAnnotation(NullableAnnotation.None));
            }
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
