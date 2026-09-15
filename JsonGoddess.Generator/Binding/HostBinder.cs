using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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

            foreach (var registration in accepted)
            {
                var members = BindMembers(registration.Type, byType, known, LocationInfo.From(host), diagnostics);
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
                enumList
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
            if (subject.TypeKind != TypeKind.Class)
            {
                refusal = "only classes are supported (structs and records with positional parameters arrive in phase 5)";
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
        /// От производного типа к базовому - и это не вкус, а <b>проверенное
        /// поведение</b> <c>System.Text.Json</c>: он печатает члены самого
        /// производного типа первыми (<c>{"C","D","A","B"}</c> для
        /// <c>Derived : Base</c>), и документ обязан совпасть с его документом
        /// байт в байт. Тот же порядок заодно даёт и правильное перекрытие:
        /// член, объявленный <c>new</c> в производном типе, встречается первым
        /// и вытесняет одноимённый базовый, а не наоборот.
        /// </summary>
        private static List<MemberModel>? BindMembers(
            INamedTypeSymbol subject,
            Dictionary<ISymbol, string> byType,
            KnownSymbols known,
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
                foreach (var member in type.GetMembers())
                {
                    if (member.IsStatic || member.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    var bound = BindMember(subject, member, byType, known, location, diagnostics, ref failed);
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

            return failed ? null : result;
        }

        private static MemberModel? BindMember(
            INamedTypeSymbol subject,
            ISymbol member,
            Dictionary<ISymbol, string> byType,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            if (known.Has(member, known.JsonIgnore))
            {
                return null;
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

                    if (field.IsReadOnly || field.IsConst)
                    {
                        Refuse(subject, member, field.Type, location, diagnostics, ref failed,
                            "readonly and const fields cannot be assigned by the generated reader");
                        return null;
                    }

                    memberType = field.Type;
                    canWrite = true;
                    canRead = true;
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

            var jsonName = known.ReadStringArgument(member, known.JsonPropertyName) ?? member.Name;

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

            return new MemberModel(
                member.Name,
                jsonName,
                utf8,
                value!,
                canWrite,
                canRead
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
