using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using JsonGoddess.Generator.Shared;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Emit;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Превращает компиляцию и список хостов в <see cref="GenerationResult"/> -
    /// голые строки и описания диагностик.
    ///
    /// Связывание идёт на каждую правку, и это сделано намеренно (§9 плана):
    /// код порождается по транзитивному замыканию графа типов, лежащего в
    /// других файлах, поэтому генератор, заведённый строго от узла хоста, был бы
    /// идеально инкрементален и выдавал бы устаревший код после переименования
    /// члена в соседнем файле. Кэш берётся не отсюда, а из равенства выходов.
    /// </summary>
    public static class HostBinder
    {
        public const string SubjectAttribute = KnownSymbols.SubjectAttribute;
        private const string ExhausterBase = KnownSymbols.ExhausterBaseName;
        private const string InjectorBase = KnownSymbols.InjectorBaseName;

        public static GenerationResult Bind(
            Compilation compilation,
            ImmutableArray<HostReference> hosts,
            CancellationToken token
            )
        {
            if (hosts.IsDefaultOrEmpty)
            {
                return GenerationResult.Empty;
            }

            var diagnostics = new List<DiagnosticInfo>();

            //u8-литералы и scoped - это требование к компилятору, а не к рантайму,
            //поэтому netstandard2.0 и net472 остаются поддержанными, а C# 10 - нет.
            var languageVersion = (compilation as CSharpCompilation)?.LanguageVersion ?? LanguageVersion.Latest;
            if (languageVersion < LanguageVersion.CSharp11)
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.LanguageVersionIsTooLowId,
                        hosts[0].Location,
                        languageVersion.ToString()
                        )
                    );
                return new GenerationResult(new List<GeneratedFile>(), diagnostics);
            }

            var known = new KnownSymbols(compilation);
            var files = new List<GeneratedFile>();

            foreach (var reference in hosts.OrderBy(h => h.MetadataName, System.StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();

                var host = compilation.Assembly.GetTypeByMetadataName(reference.MetadataName);
                if (host is null)
                {
                    //хост исчез между триггером и связыванием - обычное дело при
                    //наборе текста, и это не ошибка пользователя
                    continue;
                }

                var model = BindHost(host, reference, known, diagnostics);
                if (model is null)
                {
                    continue;
                }

                files.Add(
                    new GeneratedFile(
                        model.FullName + ".g.cs",
                        ClassSourceProducer.Produce(model)
                        )
                    );
            }

            return new GenerationResult(files, diagnostics);
        }

        private static HostModel? BindHost(
            INamedTypeSymbol host,
            HostReference reference,
            KnownSymbols known,
            List<DiagnosticInfo> diagnostics
            )
        {
            var location = LocationInfo.From(host) ?? reference.Location;

            if (host.ContainingType is not null || host.IsGenericType)
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.HostShapeIsNotSupportedId,
                        location,
                        host.ToDisplayString(),
                        host.ContainingType is not null
                            ? "a JsonGoddess host cannot be a nested type"
                            : "a JsonGoddess host cannot be generic"
                        )
                    );
                return null;
            }

            if (!IsPartial(host))
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.HostIsNotPartialId,
                        location,
                        host.ToDisplayString()
                        )
                    );
                return null;
            }

            var exhausters = BindSinks(host, known.Exhauster, known.ExhausterBase, "an exhauster", diagnostics);
            var injectors = BindSinks(host, known.Injector, known.InjectorBase, "an injector", diagnostics);

            //Регистрации собираются целиком до того, как связан хоть один член:
            //член может сослаться на субъект, объявленный ниже по списку
            //атрибутов, и порядок объявления не должен ни на что влиять.
            var registered = CollectRegistrations(host, known);
            if (registered.Count == 0)
            {
                return null;
            }

            var accepted = new List<Registration>();
            var byType = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            foreach (var registration in registered)
            {
                if (!IsSubjectShapeSupported(registration.Type, host, diagnostics))
                {
                    continue;
                }

                accepted.Add(registration);
                byType[registration.Type] = MethodSuffix(registration.Type);
            }

            var subjects = new List<SubjectModel>();
            var collections = new Dictionary<string, ValueModel>(System.StringComparer.Ordinal);
            var stringEnums = new Dictionary<string, EnumModel>(System.StringComparer.Ordinal);
            var failed = accepted.Count != registered.Count;

            var options = SerializationOptions.Read(host, known, diagnostics, ref failed);

            foreach (var registration in accepted)
            {
                var members = BindMembers(registration.Type, byType, known, options, LocationInfo.From(host), diagnostics);
                if (members is null)
                {
                    failed = true;
                    continue;
                }

                foreach (var member in members)
                {
                    ValueBinder.CollectValues(member.Value, collections, stringEnums);
                }

                subjects.Add(
                    new SubjectModel(
                        registration.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        MethodSuffix(registration.Type),
                        registration.IsRoot,
                        registration.Type.IsValueType,
                        members
                        )
                    );
            }

            //Отказ целиком, а не «всё, кроме сломанного»: субъект, который не
            //связался, мог быть чьим-то членом, и код без него не
            //скомпилировался бы - поверх понятной диагностики приехала бы
            //непонятная ошибка компилятора.
            if (failed || subjects.Count == 0)
            {
                return null;
            }

            var ns = host.ContainingNamespace.IsGlobalNamespace
                ? null
                : host.ContainingNamespace.ToDisplayString();

            //порядок вспомогательных методов фиксирован: текст порождаемого кода -
            //предмет тестов, и зависеть от порядка обхода словаря он не должен
            var collectionList = new List<ValueModel>(collections.Values);
            collectionList.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.MethodSuffix, b.MethodSuffix));

            var enumList = new List<EnumModel>(stringEnums.Values);
            enumList.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.MethodSuffix, b.MethodSuffix));

            return new HostModel(
                ns,
                BuildHostDeclaration(host),
                host.ToDisplayString(),
                exhausters.Count > 0 ? exhausters : new List<string> { "global::" + ExhausterBase },
                injectors.Count > 0 ? injectors : new List<string> { "global::" + InjectorBase },
                subjects,
                collectionList,
                enumList,
                options.DictionaryKeyNaming
                );
        }

        private readonly struct Registration
        {
            public readonly INamedTypeSymbol Type;
            public readonly bool IsRoot;

            public Registration(INamedTypeSymbol type, bool isRoot)
            {
                Type = type;
                IsRoot = isRoot;
            }
        }

        /// <summary>
        /// Один и тот же тип, зарегистрированный дважды, - не ошибка, а
        /// естественное следствие того, что корень объявляют явно: признак
        /// корня складывается, методы печатаются один раз.
        /// </summary>
        private static List<Registration> CollectRegistrations(INamedTypeSymbol host, KnownSymbols known)
        {
            var result = new List<Registration>();
            var seen = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Subject))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol subjectType)
                {
                    continue;
                }

                var isRoot = attribute.ConstructorArguments[1].Value is true;

                if (seen.TryGetValue(subjectType, out var index))
                {
                    if (isRoot && !result[index].IsRoot)
                    {
                        result[index] = new Registration(subjectType, true);
                    }

                    continue;
                }

                seen.Add(subjectType, result.Count);
                result.Add(new Registration(subjectType, isRoot));
            }

            return result;
        }

        private static string MethodSuffix(INamedTypeSymbol subject) => subject.ToDisplayString().Replace('.', '_');

        /// <summary>
        /// Модификаторы объявления обязаны совпасть с пользовательскими:
        /// <c>static</c>, <c>sealed</c> и <c>abstract</c> у частичных объявлений
        /// разъезжаться не могут. Модификатор доступа, наоборот, опускается -
        /// его частичному объявлению разрешено не называть.
        /// </summary>
        private static string BuildHostDeclaration(INamedTypeSymbol host)
        {
            var modifiers = string.Empty;
            if (host.IsStatic)
            {
                modifiers += "static ";
            }

            if (host.IsSealed && !host.IsStatic)
            {
                modifiers += "sealed ";
            }

            if (host.IsAbstract && !host.IsStatic)
            {
                modifiers += "abstract ";
            }

            return modifiers + "partial class " + host.Name;
        }

        private static bool IsPartial(INamedTypeSymbol host)
        {
            foreach (var syntaxReference in host.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is ClassDeclarationSyntax declaration
                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> BindSinks(
            INamedTypeSymbol host,
            INamedTypeSymbol? attributeType,
            INamedTypeSymbol? baseType,
            string role,
            List<DiagnosticInfo> diagnostics
            )
        {
            var result = new List<string>();
            if (attributeType is null)
            {
                return result;
            }

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol sinkType)
                {
                    continue;
                }

                var location = LocationInfo.From(host);

                if (baseType is not null && !DerivesFrom(sinkType, baseType))
                {
                    diagnostics.Add(
                        new DiagnosticInfo(
                            JsonGoddessDiagnostics.SinkTypeIsWrongId,
                            location,
                            sinkType.ToDisplayString(),
                            role,
                            baseType.ToDisplayString()
                            )
                        );
                    continue;
                }

                if (!sinkType.IsSealed)
                {
                    diagnostics.Add(
                        new DiagnosticInfo(
                            JsonGoddessDiagnostics.SinkIsNotSealedId,
                            location,
                            sinkType.ToDisplayString()
                            )
                        );
                }

                var name = sinkType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!result.Contains(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSubjectShapeSupported(
            INamedTypeSymbol subject,
            INamedTypeSymbol host,
            List<DiagnosticInfo> diagnostics
            )
        {
            var location = LocationInfo.From(host);

            string? refusal = null;
            if (subject.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                refusal = "only classes and structs are supported";
            }
            else if (subject.IsRefLikeType)
            {
                //ref struct нельзя ни положить в поле, ни передать как
                //аргумент обобщённого типа; эталон его не сериализует тем
                //более - у него T обобщённый
                refusal = "ref structs cannot be serialized: they cannot be stored in a field or used as a type argument";
            }
            else if (subject.IsAbstract || subject.IsStatic)
            {
                refusal = "the type is abstract or static, so the generated code cannot instantiate it";
            }
            else if (subject.IsGenericType)
            {
                refusal = "generic types are not supported";
            }
            else if (!HasUsableParameterlessConstructor(subject))
            {
                refusal = "no accessible parameterless constructor (constructor binding arrives in phase 5)";
            }
            else if (IsCollectionShaped(subject))
            {
                refusal =
                    "the type implements IEnumerable, so System.Text.Json writes it as a JSON array of its "
                    + "elements and ignores its properties entirely; serving it as an object would produce a "
                    + "document the reference implementation never produces (collection-shaped subjects arrive later)";
            }

            if (refusal is not null)
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.SubjectIsNotSupportedId,
                        location,
                        subject.ToDisplayString(),
                        refusal
                        )
                    );
                return false;
            }

            return true;
        }

        /// <summary>
        /// Тип, который сам является коллекцией.
        ///
        /// Найдено переносом их набора (§11.1): <c>class StringListWrapper :
        /// List&lt;string&gt; { }</c> мы принимали и писали <c>{}</c>, а эталон
        /// пишет <c>["Hello","World"]</c>. Проверено прогоном и на классе с
        /// собственным свойством: <c>ICollection&lt;string&gt;</c> с
        /// property-членом эталон пишет как <c>["a"]</c> - свойство исчезает
        /// целиком.
        ///
        /// То есть отличался не порядок и не состав, а <b>строение</b>
        /// документа, и отличался молча. Ровно тот исход, который план
        /// называет худшим.
        ///
        /// Проверка стоит после конструктора и по <c>IEnumerable</c>, а не по
        /// <c>IEnumerable&lt;T&gt;</c>: эталон смотрит на негенерический
        /// интерфейс. <c>string</c>, <c>byte[]</c>, <c>List&lt;T&gt;</c> и
        /// словарь сюда не доезжают - их разбирает связыватель значений раньше,
        /// каждый своей веткой.
        /// </summary>
        private static bool IsCollectionShaped(INamedTypeSymbol subject)
        {
            foreach (var contract in subject.AllInterfaces)
            {
                if (contract.SpecialType == SpecialType.System_Collections_IEnumerable)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasUsableParameterlessConstructor(INamedTypeSymbol subject)
        {
            foreach (var constructor in subject.InstanceConstructors)
            {
                if (constructor.Parameters.Length != 0)
                {
                    continue;
                }

                if (constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Порядок членов целиком снят с <c>System.Text.Json</c> прогоном, и
        /// он двухуровневый:
        /// <list type="number">
        /// <item>типы - от производного к базовому (<c>{"C","D","A","B"}</c>
        /// для <c>Derived : Base</c>);</item>
        /// <item>внутри каждого типа - сначала свойства, потом поля
        /// (<c>{"DerivedProp","DerivedField","BaseProp","BaseField"}</c>).</item>
        /// </list>
        /// Второй уровень нашёл дифференциальный харнесс: на типах без полей
        /// он не проявляется никак, а первый же тип с <c>[JsonInclude]</c>-полем
        /// давал другой документ.
        ///
        /// Тот же порядок заодно даёт и правильное перекрытие: член,
        /// объявленный <c>new</c> в производном типе, встречается первым и
        /// вытесняет одноимённый базовый, а не наоборот.
        /// </summary>
        private static List<MemberModel>? BindMembers(
            INamedTypeSymbol subject,
            Dictionary<ISymbol, string> byType,
            KnownSymbols known,
            SerializationOptions options,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics
            )
        {
            var chain = new List<INamedTypeSymbol>();
            for (var current = subject; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                chain.Add(current);
            }

            var shadowed = new HashSet<string>(System.StringComparer.Ordinal);
            var result = new List<MemberModel>();
            var byJsonName = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var failed = false;

            foreach (var type in chain)
            {
                foreach (var member in Ordered(type))
                {
                    var bound = BindMember(subject, member, byType, known, options, location, diagnostics, ref failed);
                    if (bound is null)
                    {
                        continue;
                    }

                    if (!shadowed.Add(bound.MemberName))
                    {
                        //член с этим именем уже пришёл из более производного
                        //типа; в C# перекрытие уже произошло, здесь ему только
                        //не надо мешать
                        continue;
                    }

                    if (byJsonName.TryGetValue(bound.JsonName, out var previous))
                    {
                        diagnostics.Add(
                            new DiagnosticInfo(
                                JsonGoddessDiagnostics.DuplicateJsonNameId,
                                location,
                                previous,
                                bound.MemberName,
                                subject.ToDisplayString(),
                                bound.JsonName
                                )
                            );
                        failed = true;
                        continue;
                    }

                    byJsonName.Add(bound.JsonName, bound.MemberName);
                    result.Add(bound);
                }
            }

            if (failed)
            {
                return null;
            }

            //[JsonPropertyOrder] применяется поверх уже разложенной иерархии, а
            //не вместо неё, и сортировка обязана быть устойчивой: члены с
            //одинаковым порядком у эталона сохраняют взаимное расположение.
            //Проверено прогоном: Derived : Base с [JsonPropertyOrder(-5)] на
            //базовом члене даёт {BaseEarly, DerivedA, BaseA}.
            return result.OrderBy(m => m.Order).ToList();
        }

        /// <summary>
        /// Члены одного типа в том порядке, в котором их печатает эталон:
        /// свойства, затем поля. Внутри каждой из двух групп - порядок
        /// объявления, каким его возвращает Roslyn.
        /// </summary>
        private static IEnumerable<ISymbol> Ordered(INamedTypeSymbol type)
        {
            var members = type.GetMembers();

            foreach (var member in members)
            {
                if (member is IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false })
                {
                    yield return member;
                }
            }

            foreach (var member in members)
            {
                if (member is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false })
                {
                    yield return member;
                }
            }
        }

        private static MemberModel? BindMember(
            INamedTypeSymbol subject,
            ISymbol member,
            Dictionary<ISymbol, string> byType,
            KnownSymbols known,
            SerializationOptions options,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            //[JsonIgnore] без Condition означает Always, то есть член исчезает в
            //обе стороны. Condition = Never - наоборот, «писать всегда», и это
            //не то же самое, что отсутствие атрибута только на вид: смысл тот
            //же, но сказано явно.
            var condition = WriteCondition.Always;
            if (known.TryReadIgnore(member, out var ignoreCondition))
            {
                switch (ignoreCondition)
                {
                    case KnownSymbols.JsonIgnoreConditionNever:
                        break;

                    case KnownSymbols.JsonIgnoreConditionWhenWritingDefault:
                        condition = WriteCondition.WhenNotDefault;
                        break;

                    case KnownSymbols.JsonIgnoreConditionWhenWritingNull:
                        condition = WriteCondition.WhenNotNull;
                        break;

                    default:
                        return null;
                }
            }

            var included = known.Has(member, known.JsonInclude);

            ITypeSymbol memberType;
            bool canWrite;
            bool canRead;

            switch (member)
            {
                case IPropertySymbol property:
                {
                    if (property.Parameters.Length > 0)
                    {
                        //индексатор именем себя не называет и в JSON не попадает
                        return null;
                    }

                    if (property.DeclaredAccessibility != Accessibility.Public)
                    {
                        if (included)
                        {
                            Refuse(subject, member, property.Type, location, diagnostics, ref failed,
                                "non-public members marked [JsonInclude] are not supported yet");
                        }

                        return null;
                    }

                    if (property.IsRequired)
                    {
                        Refuse(subject, member, property.Type, location, diagnostics, ref failed,
                            "'required' members are not supported yet: the generated code builds the object with new T()");
                        return null;
                    }

                    if (property.SetMethod is { IsInitOnly: true, DeclaredAccessibility: Accessibility.Public })
                    {
                        Refuse(subject, member, property.Type, location, diagnostics, ref failed,
                            "init-only setters are not supported yet: the generated code assigns members after new T()");
                        return null;
                    }

                    memberType = property.Type;
                    canWrite = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
                    canRead = property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };

                    if (!canWrite && !canRead)
                    {
                        return null;
                    }

                    break;
                }

                case IFieldSymbol field:
                {
                    //System.Text.Json не сериализует поля без [JsonInclude],
                    //и расходиться с ним здесь значило бы выдавать другой документ
                    if (!included)
                    {
                        return null;
                    }

                    if (field.DeclaredAccessibility != Accessibility.Public)
                    {
                        Refuse(subject, member, field.Type, location, diagnostics, ref failed,
                            "non-public members marked [JsonInclude] are not supported yet");
                        return null;
                    }

                    memberType = field.Type;
                    canWrite = true;

                    //readonly-поле с [JsonInclude] эталон ПИШЕТ и молча роняет
                    //на чтении - проверено прогоном: документ со значением 9
                    //оставляет поле равным 5. Ровно так же он ведёт себя с
                    //get-only свойством, и отказывать здесь значило бы
                    //отвергать то, что он обслуживает.
                    canRead = !field.IsReadOnly;
                    break;
                }

                default:
                    return null;
            }

            //Конвертер на члене мы не воспроизводим и не игнорируем: игнорировать
            //значило бы выдать документ, который эталон не выдаёт, - и не сказать
            //об этом. Конвертер на самом типе разбирается там, где известен тип.
            if (known.Has(member, known.JsonConverter))
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed,
                    "the member carries [JsonConverter], and JsonGoddess cannot reproduce an arbitrary converter; "
                    + "put [JsonConverter(typeof(JsonStringEnumConverter))] on the enum type instead, "
                    + "or mark the member [JsonIgnore]");
                return null;
            }

            if (!ValueBinder.TryBind(memberType, byType, known, out var value, out var refusal))
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed, refusal);
                return null;
            }

            //[JsonPropertyName] сильнее политики - и это поведение эталона, а не
            //наше соглашение: имя, названное явно, он политикой не трогает
            var jsonName = known.ReadStringArgument(member, known.JsonPropertyName)
                ?? JsonNaming.Convert(member.Name, options.PropertyNaming);

            if (!JsonNameUtf8.TryEncode(jsonName, out var utf8))
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.JsonNameRequiresEscapingId,
                        location,
                        subject.ToDisplayString(),
                        member.Name,
                        jsonName
                        )
                    );
                failed = true;
                return null;
            }

            //Эталон отвергает WhenWritingNull на значимом типе - в рантайме, при
            //построении метаданных. Мы можем отвергнуть на компиляции, и это
            //единственный случай во всей фазе, где отказ строже не потому, что
            //мы чего-то не умеем.
            if (condition == WriteCondition.WhenNotNull && !value!.IsNullable)
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed,
                    "JsonIgnoreCondition.WhenWritingNull is not valid on a value-type member, and "
                    + "System.Text.Json refuses it at run time; use JsonIgnoreCondition.WhenWritingDefault");
                return null;
            }

            return new MemberModel(
                member.Name,
                jsonName,
                utf8,
                value!,
                canWrite,
                canRead,
                condition,
                known.ReadInt32Argument(member, known.JsonPropertyOrder, 0)
                );
        }

        private static void Refuse(
            INamedTypeSymbol subject,
            ISymbol member,
            ITypeSymbol memberType,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed,
            string reason
            )
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    JsonGoddessDiagnostics.MemberTypeIsNotSupportedId,
                    LocationInfo.From(member) ?? location,
                    subject.ToDisplayString(),
                    member.Name,
                    memberType.ToDisplayString(),
                    reason
                    )
                );
            failed = true;
        }

    }
}
