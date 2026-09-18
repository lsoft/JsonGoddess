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
            //позиция хоста снимается один раз: она одна на весь хост, а
            //спрашивали её на каждого субъекта по три раза, и каждый раз это
            //разбор позиции в тексте
            var hostLocation = LocationInfo.From(host);
            var location = hostLocation ?? reference.Location;

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

            var failedBeforeSubjects = false;
            var options = SerializationOptions.Read(host, known, diagnostics, ref failedBeforeSubjects);
            var guardOptions = GuardOptions.Read(host, known, diagnostics, ref failedBeforeSubjects);
            var featureOptions = FeatureOptions.Read(host, known);
            var factories = CollectFactories(
                host, known, registered, hostLocation, diagnostics, ref failedBeforeSubjects
                );

            var ns = host.ContainingNamespace.IsGlobalNamespace
                ? null
                : host.ContainingNamespace.ToDisplayString();

            return BuildModel(
                registered,
                known,
                options,
                guardOptions,
                featureOptions,
                factories,
                hostLocation,
                ns,
                BuildHostDeclaration(host),
                host.ToDisplayString(),
                exhausters.Count > 0 ? exhausters : new List<string> { "global::" + ExhausterBase },
                injectors.Count > 0 ? injectors : new List<string> { "global::" + InjectorBase },
                diagnostics,
                failedBeforeSubjects
                );
        }

        /// <summary>
        /// Связывание всего, что уже не зависит от того, <b>откуда</b> взялся
        /// список субъектов.
        ///
        /// <para>
        /// Вынесено из <c>BindHost</c> ради Compat-слоя (§10): там хоста нет
        /// вовсе - список субъектов приезжает из найденных вызовов фасада, а
        /// стражи, фичи и sink'и назначаются не автором, а нами. Всё, что ниже
        /// этой границы, про хост уже не знает и знать не должно.
        /// </para>
        /// </summary>
        internal static HostModel? BuildModel(
            IReadOnlyList<Registration> registered,
            KnownSymbols known,
            SerializationOptions options,
            GuardOptions guardOptions,
            FeatureOptions featureOptions,
            Dictionary<ISymbol, string> factories,
            LocationInfo? hostLocation,
            string? ns,
            string declaration,
            string fullName,
            IReadOnlyList<string> exhausters,
            IReadOnlyList<string> injectors,
            List<DiagnosticInfo> diagnostics,
            bool failedBefore
            )
        {
            var accepted = new List<Registration>();
            var byType = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            foreach (var registration in registered)
            {
                if (!IsSubjectShapeSupported(registration.Type, hostLocation, diagnostics))
                {
                    continue;
                }

                accepted.Add(registration);
                byType[registration.Type] = MethodSuffix(registration.Type);
            }

            var subjects = new List<SubjectModel>();
            var collections = new Dictionary<string, ValueModel>(System.StringComparer.Ordinal);
            var stringEnums = new Dictionary<string, EnumModel>(System.StringComparer.Ordinal);
            var scalars = new Dictionary<string, ValueModel>(System.StringComparer.Ordinal);
            var failed = failedBefore || accepted.Count != registered.Count;

            foreach (var registration in accepted)
            {
                if (TryClassifyCollectionShape(registration.Type, out var elementType, out var isDictionary, out _))
                {
                    if (!BindCollectionSubject(
                            registration, byType, known, hostLocation, diagnostics,
                            elementType!, isDictionary, collections, stringEnums, scalars,
                            out var collectionModel, ref failed))
                    {
                        continue;
                    }

                    subjects.Add(collectionModel!);
                    continue;
                }

                var members = BindMembers(
                    registration.Type, byType, known, options, featureOptions.Features, hostLocation, diagnostics
                    );
                if (members is null)
                {
                    failed = true;
                    continue;
                }

                var parameters = ConstructorBinder.Bind(
                    registration.Type, members, known, hostLocation, diagnostics, ref failed
                    );

                if (parameters is null)
                {
                    continue;
                }

                factories.TryGetValue(registration.Type, out var factory);

                //Фабрика и конструктор десериализации спорят за одно место:
                //первая отдаёт готовый объект, второй требует передать ему
                //аргументы. Выбрать за автора нельзя - оба варианта он написал
                //сам и оба имел в виду, - поэтому отказ с названной причиной.
                if (factory is not null && parameters.Count > 0)
                {
                    RefuseFactory(fullName, registration.Type.ToDisplayString(), hostLocation, diagnostics, ref failed,
                        "the type is deserialized through a constructor with parameters, and a factory would "
                        + "have nowhere to pass them; drop one of the two");
                    continue;
                }

                if (!PolymorphismBinder.TryBind(
                        registration.Type, byType, known, hostLocation, diagnostics,
                        out var derived, out var discriminatorName))
                {
                    failed = true;
                    continue;
                }

                foreach (var member in members)
                {
                    ValueBinder.CollectValues(member.Value, collections, stringEnums, scalars);
                }

                subjects.Add(
                    new SubjectModel(
                        registration.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        byType[registration.Type],
                        registration.IsRoot,
                        registration.Type.IsValueType,
                        members,
                        parameters,
                        derived,
                        discriminatorName,
                        factoryInvocation: factory
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

            //порядок вспомогательных методов фиксирован: текст порождаемого кода -
            //предмет тестов, и зависеть от порядка обхода словаря он не должен
            var collectionList = new List<ValueModel>(collections.Values);
            collectionList.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.MethodSuffix, b.MethodSuffix));

            var enumList = new List<EnumModel>(stringEnums.Values);
            enumList.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.MethodSuffix, b.MethodSuffix));

            var scalarList = new List<ValueModel>(scalars.Values);
            scalarList.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.MethodSuffix, b.MethodSuffix));

            return new HostModel(
                ns,
                declaration,
                fullName,
                exhausters,
                injectors,
                subjects,
                collectionList,
                enumList,
                scalarList,
                options.DictionaryKeyNaming,
                guardOptions.Guards,
                guardOptions.MaxDepth,
                featureOptions.Features
                );
        }

        internal readonly struct Registration
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

        /// <summary>
        /// <c>[JsonFactory(typeof(T), "выражение")]</c> - чем заменить
        /// <c>new T()</c> в читателе. Нужно под пулы и переиспользование уже
        /// размещённых объектов; на запись не влияет никак.
        ///
        /// Выражение печатается в порождённый код <b>дословно</b>, и потому
        /// всё, что о нём можно узнать на компиляции, проверяется здесь:
        /// пустое выражение, повтор типа и тип, который этому хосту не
        /// субъект. Ошибку в самом выражении поймает компилятор - в
        /// порождённом файле, с указанием на строку.
        /// </summary>
        private static Dictionary<ISymbol, string> CollectFactories(
            INamedTypeSymbol host,
            KnownSymbols known,
            IReadOnlyList<Registration> registered,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            var result = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

            if (known.Factory is null)
            {
                return result;
            }

            var subjects = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var registration in registered)
            {
                subjects.Add(registration.Type);
            }

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Factory))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol subjectType)
                {
                    continue;
                }

                var invocation = attribute.ConstructorArguments[1].Value as string;
                var name = subjectType.ToDisplayString();

                if (string.IsNullOrWhiteSpace(invocation))
                {
                    RefuseFactory(host.ToDisplayString(), name, location, diagnostics, ref failed,
                        "the invocation expression is empty");
                    continue;
                }

                if (!subjects.Contains(subjectType))
                {
                    RefuseFactory(host.ToDisplayString(), name, location, diagnostics, ref failed,
                        "the type is not registered on this host with [JsonSubject], so nothing would ever "
                        + "call the factory");
                    continue;
                }

                if (result.ContainsKey(subjectType))
                {
                    RefuseFactory(host.ToDisplayString(), name, location, diagnostics, ref failed,
                        "the type already has a factory on this host, and two expressions cannot both "
                        + "replace one 'new'");
                    continue;
                }

                result.Add(subjectType, invocation!);
            }

            return result;
        }

        private static void RefuseFactory(
            string hostName,
            string subjectName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed,
            string reason
            )
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    JsonGoddessDiagnostics.InvalidFactoryId,
                    location,
                    hostName,
                    subjectName,
                    reason
                    )
                );
            failed = true;
        }

        private static string MethodSuffix(INamedTypeSymbol subject) => subject.ToDisplayString().Replace('.', '_');

        private static bool IsAscii(byte[] utf8)
        {
            foreach (var b in utf8)
            {
                if (b >= 0x80)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Свёртка регистра по ASCII для сравнения имён на предмет коллизии -
        /// то же самое сворачивание, что печатает эмиттер в
        /// <c>JsonAsciiName.EqualsIgnoreCase</c>, только здесь оно нужно
        /// один раз на компиляции, а не в порождённом коде.
        /// </summary>
        private static string FoldAscii(string name)
        {
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c >= 'A' && c <= 'Z')
                {
                    chars[i] = (char)(c + 0x20);
                }
            }

            return new string(chars);
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

        private static bool IsSubjectShapeSupported(
            INamedTypeSymbol subject,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics
            )
        {
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
            else if (IsCollectionShaped(subject) && !TryClassifyCollectionShape(subject, out _, out _, out refusal))
            {
                //refusal уже присвоен внутри TryClassifyCollectionShape -
                //его собственный контракт: false означает либо "не
                //коллекция" (сюда мы уже не попадаем - IsCollectionShaped
                //отсеял), либо "коллекция, но не такая, какую мы обслуживаем",
                //и тогда причина обязана быть названа
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
        /// Тип, который сам является коллекцией - быстрый предварительный
        /// отбор по негенерическому <c>IEnumerable</c>, тому же интерфейсу,
        /// на который смотрит сам эталон (не по <c>IEnumerable&lt;T&gt;</c>).
        /// <c>string</c>, <c>byte[]</c>, <c>List&lt;T&gt;</c> и словарь сюда не
        /// доезжают как <b>член</b> - их разбирает связыватель значений раньше,
        /// каждый своей веткой, - но как <b>субъект</b> (класс, унаследованный
        /// от одного из них) доезжают, и <see cref="TryClassifyCollectionShape"/>
        /// разбирает, какая именно это форма (§9.10 плана).
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

        /// <summary>
        /// Форма субъекта-коллекции (§9.10 плана, часть 1). Найдено переносом
        /// их набора (§11.1, §9.6): <c>class StringListWrapper : List&lt;string&gt;
        /// { }</c> мы принимали и писали <c>{}</c>, а эталон пишет
        /// <c>["Hello","World"]</c>. Дальше - три пробы, разошедшиеся с
        /// ожиданием:
        ///
        /// <list type="number">
        /// <item><b>словарь смотрится раньше списка.</b>
        /// <c>class X : Dictionary&lt;string,int&gt;</c> эталон пишет
        /// <c>{"a":1}</c>, а не <c>[...]</c> - хотя <c>Dictionary&lt;,&gt;</c>
        /// реализует и <c>ICollection&lt;KeyValuePair&lt;,&gt;&gt;</c> тоже.
        /// Поэтому <c>IDictionary&lt;string,V&gt;</c> проверяется первым;</item>
        /// <item><b>собственные свойства теряются одинаково у обеих форм.</b>
        /// <c>ICollection&lt;string&gt;</c> с property-членом эталон пишет как
        /// <c>["a"]</c> - свойство исчезает целиком, и мы теряем его так же
        /// (subject.Members у коллекции-субъекта всегда пуст), а не отказываем:
        /// это не «молчаливое расхождение» principle 2, а намеренное совпадение
        /// с тем, что теряет сам эталон;</item>
        /// <item><b>без <c>Add</c> - отказ, а не рантайм-исключение.</b> Тип,
        /// реализующий только <c>IEnumerable&lt;T&gt;</c> без <c>ICollection&lt;T&gt;</c>,
        /// эталон пишет как массив, но читает с <c>NotSupportedException</c>
        /// <b>на любом документе</b> - класть добавленный элемент некуда.
        /// Мы отказываем на компиляции по той же причине, а не генерируем
        /// метод, обречённый бросать всегда.</item>
        /// </list>
        ///
        /// Тип, реализующий только негенерический <c>IEnumerable</c> (без
        /// закрытого <c>IEnumerable&lt;T&gt;</c> вовсе), - тоже отказ: элемент
        /// был бы <c>object</c>, а <c>object</c> этот генератор не пишет и не
        /// планирует.
        /// </summary>
        private static bool TryClassifyCollectionShape(
            INamedTypeSymbol subject,
            out ITypeSymbol? elementOrValueType,
            out bool isDictionary,
            out string? refusal
            )
        {
            elementOrValueType = null;
            isDictionary = false;
            refusal = null;

            INamedTypeSymbol? dictionaryInterface = null;
            var dictionaryAmbiguous = false;
            INamedTypeSymbol? collectionInterface = null;
            var collectionAmbiguous = false;
            INamedTypeSymbol? enumerableInterface = null;
            var enumerableAmbiguous = false;

            foreach (var contract in subject.AllInterfaces)
            {
                //System.Collections.Generic - по звеньям, а не строкой, как и
                //в ValueBinder.IsCollectionsGeneric: ToDisplayString собирал
                //бы её заново на каждый интерфейс каждого субъекта.
                if (!contract.IsGenericType
                    || contract.ContainingNamespace is not { Name: "Generic", } ns
                    || ns.ContainingNamespace is not { Name: "Collections", } collectionsNs
                    || collectionsNs.ContainingNamespace is not { Name: "System", } systemNs
                    || !systemNs.ContainingNamespace.IsGlobalNamespace)
                {
                    continue;
                }

                switch (contract.OriginalDefinition.MetadataName)
                {
                    case "IDictionary`2":
                        dictionaryAmbiguous |= dictionaryInterface is not null
                            && !SymbolEqualityComparer.Default.Equals(dictionaryInterface, contract);
                        dictionaryInterface = contract;
                        break;

                    case "ICollection`1":
                        collectionAmbiguous |= collectionInterface is not null
                            && !SymbolEqualityComparer.Default.Equals(collectionInterface, contract);
                        collectionInterface = contract;
                        break;

                    case "IEnumerable`1":
                        enumerableAmbiguous |= enumerableInterface is not null
                            && !SymbolEqualityComparer.Default.Equals(enumerableInterface, contract);
                        enumerableInterface = contract;
                        break;
                }
            }

            //Словарь смотрится раньше списка: Dictionary<TKey,TValue> реализует
            //и IDictionary<,>, и ICollection<KeyValuePair<,>> одновременно, а
            //эталон пишет его объектом - проверено пробой.
            if (dictionaryInterface is not null)
            {
                if (dictionaryAmbiguous)
                {
                    refusal = "the type implements more than one closed construction of IDictionary<,>, "
                        + "so the value type cannot be determined unambiguously";
                    return false;
                }

                var key = dictionaryInterface.TypeArguments[0];
                if (key.SpecialType != SpecialType.System_String)
                {
                    refusal = "the dictionary key is '" + key.ToDisplayString()
                        + "', not string; only string-keyed dictionaries are supported";
                    return false;
                }

                if (HasHardcodedReadOnlyTrue(subject, dictionaryInterface))
                {
                    refusal = "IsReadOnly on this type always returns true; System.Text.Json refuses to "
                        + "populate such a collection on any document, and so do we - a generated reader "
                        + "that always throws is not a served type";
                    return false;
                }

                elementOrValueType = dictionaryInterface.TypeArguments[1];
                isDictionary = true;
                return true;
            }

            if (collectionInterface is not null)
            {
                if (collectionAmbiguous)
                {
                    refusal = "the type implements more than one closed construction of ICollection<>, "
                        + "so the element type cannot be determined unambiguously";
                    return false;
                }

                if (HasHardcodedReadOnlyTrue(subject, collectionInterface))
                {
                    refusal = "IsReadOnly on this type always returns true; System.Text.Json refuses to "
                        + "populate such a collection on any document, and so do we - a generated reader "
                        + "that always throws is not a served type";
                    return false;
                }

                elementOrValueType = collectionInterface.TypeArguments[0];
                return true;
            }

            if (enumerableInterface is not null)
            {
                if (enumerableAmbiguous)
                {
                    refusal = "the type implements more than one closed construction of IEnumerable<>, "
                        + "so the element type cannot be determined unambiguously";
                    return false;
                }

                //Пишется этот тип успешно - GetEnumerator для записи хватает, -
                //но читать некуда: ICollection<T> с его Add не реализован, а
                //без него System.Text.Json бросает NotSupportedException на
                //любом документе. Отказ на компиляции - то же решение раньше.
                refusal = "the type implements IEnumerable<" + enumerableInterface.TypeArguments[0].ToDisplayString()
                    + "> but not ICollection<> of the same element, so there is no accessible Add method; "
                    + "System.Text.Json throws NotSupportedException at run time for the very same reason";
                return false;
            }

            refusal = "the type implements only the non-generic IEnumerable, so its element type would be "
                + "'object', and JsonGoddess does not serialize System.Object";
            return false;
        }

        /// <summary>
        /// <c>IsReadOnly</c>, зашитый константой <c>true</c>. Найдено их
        /// же корпусом (§9.10): <c>ReadOnlyStringICollectionWrapper</c> и три
        /// его соседа переопределяют <c>IsReadOnly</c> ровно так - и эталон
        /// на любом документе для них бросает <c>NotSupportedException</c>,
        /// потому что класть элемент, даже когда <c>Add</c> есть, ему запрещает
        /// сам контракт коллекции.
        ///
        /// Это единственное место во всей части 1, где решение снято не с
        /// формы типа, а с <b>тела</b> члена: <c>IsReadOnly</c> в общем случае
        /// вычисляемое (у <c>HashSetWithBackingCollection</c> - делегат к
        /// вложенной коллекции, и он законен), и врать про такие типы, отказывая
        /// им тоже, было бы не честнее, чем принимать их. Различает их
        /// только буквальное <c>=&gt; true</c>/<c>{ get { return true; } }</c> -
        /// то, что можно утверждать не запуская код.
        /// </summary>
        private static bool HasHardcodedReadOnlyTrue(INamedTypeSymbol subject, INamedTypeSymbol constructedInterface)
        {
            var collectionOfT = constructedInterface.OriginalDefinition.MetadataName == "ICollection`1"
                ? constructedInterface
                : constructedInterface.AllInterfaces.FirstOrDefault(
                    i => i.OriginalDefinition.MetadataName == "ICollection`1"
                    );

            var isReadOnlyOnInterface = collectionOfT?.GetMembers("IsReadOnly")
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            if (isReadOnlyOnInterface is null)
            {
                return false;
            }

            var implementation = subject.FindImplementationForInterfaceMember(isReadOnlyOnInterface) as IPropertySymbol;
            if (implementation is null)
            {
                return false;
            }

            //FindImplementationForInterfaceMember отдаёт ту реализацию, что
            //впервые закрывает контракт интерфейса - у ReadOnlyStringICollectionWrapper
            //это virtual-свойство на generic-предке, потому что тот и есть
            //место, где интерфейс реализован. Нам нужен не он, а тот, кого
            //настоящий вызов найдёт виртуальной диспетчеризацией - самый
            //производный override, который может лежать в ЛЮБОМ промежуточном
            //типе между subject и найденной реализацией.
            implementation = MostDerivedOverride(subject, implementation);

            return GetterReturnsLiteralTrue(implementation);
        }

        /// <summary>
        /// Самый производный override свойства <paramref name="declared"/>,
        /// начиная поиск от <paramref name="subject"/> и подымаясь к базовым
        /// типам. Виртуальная диспетчеризация делает то же самое в рантайме -
        /// здесь она повторяется на символах, потому что вызывать код мы не
        /// можем и не должны (§1 плана: код порождается на компиляции).
        /// </summary>
        private static IPropertySymbol MostDerivedOverride(INamedTypeSymbol subject, IPropertySymbol declared)
        {
            for (var current = subject; current is not null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(declared.Name))
                {
                    if (member is IPropertySymbol property && OverridesOrIs(property, declared))
                    {
                        return property;
                    }
                }
            }

            return declared;
        }

        private static bool OverridesOrIs(IPropertySymbol property, IPropertySymbol target)
        {
            for (var current = property; current is not null; current = current.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GetterReturnsLiteralTrue(IPropertySymbol property)
        {
            foreach (var reference in property.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
                {
                    continue;
                }

                if (IsLiteralTrue(declaration.ExpressionBody?.Expression))
                {
                    return true;
                }

                var getter = declaration.AccessorList?.Accessors
                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

                if (getter is null)
                {
                    continue;
                }

                if (IsLiteralTrue(getter.ExpressionBody?.Expression))
                {
                    return true;
                }

                if (getter.Body is { Statements: { Count: 1, } statements, }
                    && statements[0] is ReturnStatementSyntax { Expression: var returned, }
                    && IsLiteralTrue(returned))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLiteralTrue(ExpressionSyntax? expression) =>
            expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.TrueLiteralExpression);

        /// <summary>
        /// Субъект-коллекция целиком: элемент связывается только теперь -
        /// раньше <c>byType</c> ещё не полон (§9 плана: субъект может
        /// ссылаться на тип, зарегистрированный ниже по списку атрибутов), -
        /// а конструктор и полиморфизм проверяются теми же связывателями, что
        /// и у обычного субъекта. Отличие только в том, что членов передаётся
        /// пустой список: класть в него нечего, subject.Members у такого
        /// субъекта всегда пуст (§9.10).
        /// </summary>
        private static bool BindCollectionSubject(
            Registration registration,
            Dictionary<ISymbol, string> byType,
            KnownSymbols known,
            LocationInfo? hostLocation,
            List<DiagnosticInfo> diagnostics,
            ITypeSymbol elementType,
            bool isDictionary,
            Dictionary<string, ValueModel> collections,
            Dictionary<string, EnumModel> stringEnums,
            Dictionary<string, ValueModel> scalars,
            out SubjectModel? model,
            ref bool failed
            )
        {
            model = null;

            if (!ValueBinder.TryBind(elementType, byType, known, out var elementValue, out var elementRefusal))
            {
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.SubjectIsNotSupportedId,
                        hostLocation,
                        registration.Type.ToDisplayString(),
                        (isDictionary ? "its value type '" : "its element type '") + elementType.ToDisplayString()
                            + "' cannot be served: " + elementRefusal
                        )
                    );
                failed = true;
                return false;
            }

            var parameters = ConstructorBinder.Bind(
                registration.Type, new List<MemberModel>(), known, hostLocation, diagnostics, ref failed
                );

            if (parameters is null)
            {
                return false;
            }

            if (!PolymorphismBinder.TryBind(
                    registration.Type, byType, known, hostLocation, diagnostics,
                    out var derived, out var discriminatorName))
            {
                failed = true;
                return false;
            }

            if (derived.Count > 0)
            {
                //Комбинация исключена по построению: собственных членов у
                //такого субъекта нет, а дискриминатор - это свойство,
                //приписанное первым к объекту, которого сериализатор эталона
                //для коллекции-субъекта не печатает вовсе
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.SubjectIsNotSupportedId,
                        hostLocation,
                        registration.Type.ToDisplayString(),
                        "the type is both collection-shaped and carries [JsonDerivedType]; System.Text.Json "
                            + "writes a collection-shaped type as a bare array or object and never looks for "
                            + "a type discriminator on it"
                        )
                    );
                failed = true;
                return false;
            }

            ValueBinder.CollectValues(elementValue!, collections, stringEnums, scalars);

            model = new SubjectModel(
                registration.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                byType[registration.Type],
                registration.IsRoot,
                registration.Type.IsValueType,
                new List<MemberModel>(),
                parameters,
                derived,
                discriminatorName,
                new CollectionShapeModel(elementValue!, isDictionary)
                );
            return true;
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
            JsonFeature features,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics
            )
        {
            //[SetsRequiredMembers] спрашивается ДО связывания членов, а не
            //после: отказы на обязательных членах выдаёт BindMember, и узнать
            //об обещании конструктора позже значило бы выдать их зря
            var setsRequired = ConstructorBinder.SetsRequiredMembers(subject, known);

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
                    var bound = BindMember(subject, member, byType, known, options, setsRequired, location, diagnostics, ref failed);
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

            //JsonFeature.CaseInsensitiveNames (§6.2 плана, вопрос O5 §15):
            //сворачивание регистра здесь - по ASCII, у эталона - по Unicode
            //(пробоем подтверждено на кириллице). На ASCII-имени оба
            //совпадения совпадают буква в букву, а на не-ASCII разошлись бы
            //молча - принцип 1 запрещает это, поэтому такой тип честно
            //отвергается, а не обслуживается приблизительно. Второй отказ -
            //на паре имён, которые после ASCII-свёртки совпадают: диспетчер
            //не может завести две ветки на один и тот же случай, а эталон в
            //этой ситуации тоже отказывает (проверено пробой), хоть и в
            //рантайме при первом обращении, а не на компиляции.
            if ((features & JsonFeature.CaseInsensitiveNames) != 0)
            {
                var byFolded = new Dictionary<string, MemberModel>(System.StringComparer.Ordinal);

                foreach (var member in result)
                {
                    if (!IsAscii(member.JsonNameUtf8))
                    {
                        diagnostics.Add(
                            new DiagnosticInfo(
                                JsonGoddessDiagnostics.CaseInsensitiveNameIsNotSupportedId,
                                location,
                                subject.ToDisplayString(),
                                member.MemberName,
                                "its JSON name '" + member.JsonName + "' contains a non-ASCII character; "
                                    + "JsonGoddess folds case by ASCII, System.Text.Json folds it by Unicode, "
                                    + "and the two would not agree on such a name"
                                )
                            );
                        return null;
                    }

                    var folded = FoldAscii(member.JsonName);
                    if (byFolded.TryGetValue(folded, out var previous))
                    {
                        diagnostics.Add(
                            new DiagnosticInfo(
                                JsonGoddessDiagnostics.CaseInsensitiveNameIsNotSupportedId,
                                location,
                                subject.ToDisplayString(),
                                member.MemberName,
                                "its JSON name '" + member.JsonName + "' differs only by case from member '"
                                    + previous.MemberName + "' ('" + previous.JsonName + "'); System.Text.Json "
                                    + "itself refuses this combination when PropertyNameCaseInsensitive is set "
                                    + "(verified by probing), and a case-insensitive dispatcher cannot serve "
                                    + "two members through one name either"
                                )
                            );
                        return null;
                    }

                    byFolded.Add(folded, member);
                }
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
            bool setsRequiredMembers,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            //required и [JsonRequired] - одно и то же, проверено пробой
            //(scratchpad/ReqProbe): оба дают одно сообщение, один путь и одну
            //позицию. Считается это здесь, до всех проверок, потому что
            //дальше член может быть отброшен, а отброшенный required - повод
            //отказать, а не промолчать.
            var isRequired =
                !setsRequiredMembers
                && (member is IPropertySymbol { IsRequired: true }
                    || member is IFieldSymbol { IsRequired: true }
                    || known.Has(member, known.JsonRequired));

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
                        //[JsonIgnore] + required - у эталона это не документ, а
                        //ошибка настройки: InvalidOperationException
                        //«marked required but does not specify a setter», и
                        //бросает он её на ЛЮБОМ обращении к типу, включая
                        //запись (проверено пробой). Мы отказываем на
                        //компиляции: молча прочитать документ там, где эталон
                        //падает, - расхождение в строгости, а отказ раньше и
                        //громче падения позже.
                        if (isRequired)
                        {
                            Refuse(subject, member, MemberType(member), location, diagnostics, ref failed,
                                "the member is required and carries [JsonIgnore]; System.Text.Json treats this "
                                + "combination as a configuration error and throws InvalidOperationException on "
                                + "every use of the type, so accepting it here would be a divergence in strictness. "
                                + "Mark the constructor with [SetsRequiredMembers] if the type sets the member "
                                + "itself: System.Text.Json then accepts the type, and so do we (probed)");
                        }

                        return null;
                }
            }

            var included = known.Has(member, known.JsonInclude);

            ITypeSymbol memberType;
            bool canWrite;
            bool canRead;
            var isInitOnly = false;

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

                    memberType = property.Type;
                    canWrite = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };

                    //init-member читается: присвоить его можно, просто не там,
                    //где остальные, - в инициализаторе объекта, после того как
                    //прочитано всё. Эталон их обслуживает, проверено прогоном
                    canRead = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
                    isInitOnly = property.SetMethod is { IsInitOnly: true };

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

            //Оба атрибута §6.1 перечислял как читаемые, а генератор не знал о
            //них вовсе - найдено при сборке docs/stj-divergences.md. Молчание
            //здесь стоило дороже всего: [JsonNumberHandling(WriteAsString)] у
            //эталона даёт {"Amount":"5"}, у нас давало {"Amount":5}, то есть
            //расходился сам документ, и ни одного слова на компиляции. Пока не
            //умеем воспроизвести - отказываем, как отказываем чужому
            //конвертеру.
            if (known.Has(member, known.JsonNumberHandling))
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed,
                    "the member carries [JsonNumberHandling], which changes the shape of the number in the "
                    + "document (WriteAsString writes it quoted, AllowReadingFromString accepts it quoted); "
                    + "JsonGoddess does not reproduce it yet, and writing the number plainly would silently "
                    + "produce a document the reference implementation never produces");
                return null;
            }

            //Член обязателен, но прочитать его нечем: имя в документе будет,
            //а положить значение некуда. Эталон на такой комбинации падает
            //InvalidOperationException'ом на любом обращении к типу, мы
            //отказываем на компиляции - по той же причине, что и с
            //[JsonIgnore] выше.
            if (isRequired && !canRead)
            {
                Refuse(subject, member, memberType, location, diagnostics, ref failed,
                    "the member is required but has no public setter reachable by the generated code, so its "
                    + "presence could never be satisfied; System.Text.Json refuses this combination too, only "
                    + "at run time. Mark the constructor with [SetsRequiredMembers] if the type sets the member "
                    + "itself (probed)");
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
                known.ReadInt32Argument(member, known.JsonPropertyOrder, 0),
                isInitOnly: isInitOnly,
                isRequired: isRequired
                );
        }

        /// <summary>
        /// Тип члена там, где до <c>memberType</c> дело ещё не дошло, - нужен
        /// одному сообщению об отказе и больше никому. Приведение безопасно:
        /// сюда попадают только свойства и поля, ничего другого перечисление
        /// членов не отдаёт.
        /// </summary>
        private static ITypeSymbol MemberType(ISymbol member)
        {
            return member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;
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
