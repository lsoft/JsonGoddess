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
        public const string SubjectAttribute = "JsonGoddess.JsonSubjectAttribute";
        private const string ExhausterAttribute = "JsonGoddess.JsonExhausterAttribute";
        private const string InjectorAttribute = "JsonGoddess.JsonInjectorAttribute";
        private const string ExhausterBase = "JsonGoddess.ExhausterBase";
        private const string InjectorBase = "JsonGoddess.InjectorBase";

        private const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";
        private const string JsonIncludeAttribute = "System.Text.Json.Serialization.JsonIncludeAttribute";
        private const string JsonPropertyNameAttribute = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

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

            var exhausters = BindSinks(host, known.ExhausterAttribute, known.ExhausterBase, "an exhauster", diagnostics);
            var injectors = BindSinks(host, known.InjectorAttribute, known.InjectorBase, "an injector", diagnostics);

            var subjects = new List<SubjectModel>();
            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.SubjectAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol subjectType)
                {
                    continue;
                }

                var isRoot = attribute.ConstructorArguments[1].Value is true;

                var subject = BindSubject(subjectType, host, isRoot, known, diagnostics);
                if (subject is not null)
                {
                    subjects.Add(subject);
                }
            }

            if (subjects.Count == 0)
            {
                return null;
            }

            var ns = host.ContainingNamespace.IsGlobalNamespace
                ? null
                : host.ContainingNamespace.ToDisplayString();

            return new HostModel(
                ns,
                BuildHostDeclaration(host),
                host.ToDisplayString(),
                exhausters.Count > 0 ? exhausters : new List<string> { "global::" + ExhausterBase },
                injectors.Count > 0 ? injectors : new List<string> { "global::" + InjectorBase },
                subjects
                );
        }

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

        private static SubjectModel? BindSubject(
            INamedTypeSymbol subject,
            INamedTypeSymbol host,
            bool isRoot,
            KnownSymbols known,
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
                return null;
            }

            var members = BindMembers(subject, known, location, diagnostics);
            if (members is null)
            {
                return null;
            }

            var displayName = subject.ToDisplayString();

            return new SubjectModel(
                subject.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                displayName.Replace('.', '_'),
                isRoot,
                members
                );
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

        private static List<MemberModel>? BindMembers(
            INamedTypeSymbol subject,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics
            )
        {
            //от базового к производному: член производного типа перекрывает
            //одноимённый член базового, поэтому имена, уже увиденные ниже по
            //иерархии, во внимание не принимаются
            var chain = new List<INamedTypeSymbol>();
            for (var current = subject; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                chain.Add(current);
            }

            chain.Reverse();

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

                    var bound = BindMember(subject, member, known, location, diagnostics, ref failed);
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
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            if (HasAttribute(member, known.JsonIgnoreAttribute))
            {
                return null;
            }

            var included = HasAttribute(member, known.JsonIncludeAttribute);

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

            if (!BuiltinTypes.TryBind(memberType, out var kind, out var isNullableValueType, out var isReferenceType))
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed,
                    "phase 2 serves only builtin scalar types; classes, collections and enums arrive in phase 4");
                return null;
            }

            var jsonName = ReadPropertyName(member, known) ?? member.Name;

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
                kind,
                isNullableValueType,
                isReferenceType,
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

        private static string? ReadPropertyName(ISymbol member, KnownSymbols known)
        {
            if (known.JsonPropertyNameAttribute is null)
            {
                return null;
            }

            foreach (var attribute in member.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonPropertyNameAttribute)
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is string name)
                {
                    return name;
                }
            }

            return null;
        }

        private static bool HasAttribute(ISymbol member, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
            {
                return false;
            }

            foreach (var attribute in member.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Типы, известные генератору по именам. Атрибуты System.Text.Json
        /// могут отсутствовать вовсе - на них никто не обязан ссылаться, -
        /// поэтому каждый из них nullable, и отсутствие означает "такого
        /// атрибута в этой компиляции не бывает", а не ошибку.
        /// </summary>
        private sealed class KnownSymbols
        {
            public readonly INamedTypeSymbol? SubjectAttribute;
            public readonly INamedTypeSymbol? ExhausterAttribute;
            public readonly INamedTypeSymbol? InjectorAttribute;
            public readonly INamedTypeSymbol? ExhausterBase;
            public readonly INamedTypeSymbol? InjectorBase;
            public readonly INamedTypeSymbol? JsonIgnoreAttribute;
            public readonly INamedTypeSymbol? JsonIncludeAttribute;
            public readonly INamedTypeSymbol? JsonPropertyNameAttribute;

            public KnownSymbols(Compilation compilation)
            {
                SubjectAttribute = compilation.GetTypeByMetadataName(HostBinder.SubjectAttribute);
                ExhausterAttribute = compilation.GetTypeByMetadataName(HostBinder.ExhausterAttribute);
                InjectorAttribute = compilation.GetTypeByMetadataName(HostBinder.InjectorAttribute);
                ExhausterBase = compilation.GetTypeByMetadataName(HostBinder.ExhausterBase);
                InjectorBase = compilation.GetTypeByMetadataName(HostBinder.InjectorBase);
                JsonIgnoreAttribute = compilation.GetTypeByMetadataName(HostBinder.JsonIgnoreAttribute);
                JsonIncludeAttribute = compilation.GetTypeByMetadataName(HostBinder.JsonIncludeAttribute);
                JsonPropertyNameAttribute = compilation.GetTypeByMetadataName(HostBinder.JsonPropertyNameAttribute);
            }
        }
    }
}
