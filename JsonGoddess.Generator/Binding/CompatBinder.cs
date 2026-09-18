using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Emit;
using JsonGoddess.Generator.Model;
using JsonGoddess.Generator.Shared;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Маршрут A Compat-слоя (§10): найденные вызовы фасада - в порождённый
    /// хост плюс регистрацию быстрого пути из <c>[ModuleInitializer]</c>.
    ///
    /// <para>
    /// Отличий от обычного хоста три, и все три - следствие одного: здесь нет
    /// автора, который что-то объявил.
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>Список субъектов считается сам.</b> Автор обычного хоста
    /// перечисляет вложенные типы <c>[JsonSubject(..., false)]</c>; здесь
    /// перечислять некому, и транзитивное замыкание графа считается обходом
    /// членов.</item>
    /// <item><b>Отказ - не отказ, а отступление.</b> У обычного хоста
    /// несвязавшийся субъект валит весь хост: автор просил именно его. Здесь
    /// тип, который не вышло обслужить, просто не регистрируется, работа уходит
    /// эталону и продолжается - это и есть смысл drop-in'а. Поэтому каждый
    /// корень связывается <b>отдельно</b>, со своим замыканием, и падение
    /// одного не трогает остальных.</item>
    /// <item><b>Строгость назначаем мы.</b> См. <see cref="CompatGuards"/>.</item>
    /// </list>
    /// </summary>
    public static class CompatBinder
    {
        public const string FacadeMetadataName = "JsonGoddess.Compat.JsonSerializer";

        /// <summary>
        /// Метка, которой фасад сам называет перегрузки с быстрым путём.
        /// Список имён здесь был бы вторым списком - а два списка, разъехавшись,
        /// дают не отказ, а тихое расхождение.
        /// </summary>
        public const string FastPathAttributeName = "JsonGoddess.Compat.CompatFastPathAttribute";

        private const string BindingTypeName = "global::JsonGoddess.Compat.CompatBinding";
        private const string ReaderDelegateName = "global::JsonGoddess.Compat.Utf8Reader";
        private const string Exhauster = "global::JsonGoddess.PooledUtf8Exhauster";
        private const string Injector = "global::JsonGoddess.DefaultInjector";
        private const string ModuleInitializerName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

        private const string HostNamespace = "JsonGoddess.Compat.Generated";
        private const string HostTypeName = "JsonGoddessCompatHost";
        private const string HostDeclaration = "internal static partial class " + HostTypeName;

        /// <summary>
        /// Строгость фасада. Ровно та, что у эталона с опциями по умолчанию, -
        /// и это <b>не</b> «включить всё», как было записано в §6.3 плана.
        ///
        /// <para>
        /// Обоснование там было верное - «фасад, более снисходительный, чем
        /// заменяемая библиотека, это не фича», - а вывод из него обратный.
        /// Два стража из семи делают нас <b>строже</b> эталона:
        /// <c>UnknownProperties</c> (эталон незнакомое свойство пропускает) и
        /// <c>DuplicateProperties</c> (эталон разрешает, побеждает последнее
        /// значение). Включив их, фасад отвергал бы документы, которые
        /// заменяемая библиотека принимает, - то же расхождение, только в
        /// другую сторону.
        /// </para>
        ///
        /// <para>
        /// Фичи (<c>JsonFeature</c>) по той же причине не включаются ни одна:
        /// они отвечают на вопрос «понимать ли эту конструкцию», то есть все
        /// до одной делают снисходительнее. Комментарии, висячие запятые,
        /// <c>NaN</c> строкой, числа из строк, имена без учёта регистра - у
        /// эталона по умолчанию всё это отвергается.
        /// </para>
        /// </summary>
        public const JsonGuard CompatGuards =
            JsonGuard.TrailingContent
            | JsonGuard.ControlCharsInStrings
            | JsonGuard.StrictNumbers
            | JsonGuard.InvalidUtf8
            | JsonGuard.MaxDepth;

        /// <summary>Предел вложенности у эталона по умолчанию.</summary>
        public const int CompatMaxDepth = 64;

        public static GenerationResult Bind(
            Compilation compilation,
            ImmutableArray<CompatCallSite> sites,
            CancellationToken token
            )
        {
            if (sites.IsDefaultOrEmpty)
            {
                return GenerationResult.Empty;
            }

            var known = new KnownSymbols(compilation);
            var diagnostics = new List<DiagnosticInfo>();

            //корни в порядке идентификатора, а не в порядке нахождения: текст
            //порождаемого кода не должен зависеть от того, в каком файле вызов
            //встретился первым
            var roots = new Dictionary<INamedTypeSymbol, LocationInfo?>(SymbolEqualityComparer.Default);
            var order = new List<INamedTypeSymbol>();

            foreach (var site in sites.OrderBy(s => s.ReferenceId, System.StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();

                var type = Resolve(site.ReferenceId, compilation);
                if (type is null)
                {
                    //тип исчез между триггером и связыванием, либо аргументом
                    //была форма, которую идентификатор не называет, - обычное
                    //дело при наборе текста, и это не ошибка пользователя
                    continue;
                }

                if (roots.ContainsKey(type))
                {
                    continue;
                }

                roots.Add(type, site.Location);
                order.Add(type);
            }

            if (order.Count == 0)
            {
                return GenerationResult.Empty;
            }

            //Каждый корень пробуется отдельно: связыватель отказывает хосту
            //целиком, а нам нужно, чтобы уцелевшие корни уцелели. Цена -
            //повторное связывание общих поддеревьев; она платится на
            //компиляции и только у того, кто подключил фасад.
            var served = new List<INamedTypeSymbol>();
            var registrations = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);

            foreach (var root in order)
            {
                token.ThrowIfCancellationRequested();

                var closure = Closure(root, known);
                var trial = new List<DiagnosticInfo>();

                if (BuildFor(closure, known, trial) is null)
                {
                    diagnostics.Add(
                        new DiagnosticInfo(
                            JsonGoddessDiagnostics.CompatTypeFallsBackId,
                            roots[root],
                            root.ToDisplayString(),
                            Explain(trial)
                            )
                        );
                    continue;
                }

                served.Add(root);
                foreach (var registration in closure)
                {
                    //корень остаётся корнем, даже если он же встретился чьим-то
                    //членом: точка входа печатается по этому признаку
                    registrations[registration.Type] =
                        registrations.TryGetValue(registration.Type, out var was)
                            ? was || registration.IsRoot
                            : registration.IsRoot;
                }
            }

            if (served.Count == 0)
            {
                return new GenerationResult(new List<GeneratedFile>(), diagnostics);
            }

            var all = registrations
                .OrderBy(pair => pair.Key.ToDisplayString(), System.StringComparer.Ordinal)
                .Select(pair => new HostBinder.Registration(pair.Key, pair.Value))
                .ToList();

            var final = new List<DiagnosticInfo>();
            var model = BuildFor(all, known, final);

            if (model is null)
            {
                //каждый корень поодиночке связался, а все вместе - нет. Это
                //дыра в генераторе, а не в коде пользователя, и молчать о ней
                //нельзя: работать будет, но медленно и необъяснимо
                diagnostics.Add(
                    new DiagnosticInfo(
                        JsonGoddessDiagnostics.CompatGenerationFailedId,
                        roots[served[0]],
                        served[0].ToDisplayString(),
                        Explain(final)
                        )
                    );
                return new GenerationResult(new List<GeneratedFile>(), diagnostics);
            }

            var files = new List<GeneratedFile>
            {
                new GeneratedFile(HostNamespace + "." + HostTypeName + ".g.cs", ClassSourceProducer.Produce(model)),
                new GeneratedFile(
                    HostNamespace + "." + HostTypeName + ".Registration.g.cs",
                    EmitRegistration(model, served)
                    ),
            };

            //ModuleInitializerAttribute появился в .NET 5; компилятору довольно
            //того, что тип существует, - хоть свой. Потребитель на net472 или
            //netstandard2.0 без полифилла просто не собрался бы.
            //
            //Отдельным файлом, а не сверху регистрации: полифилл объявляется
            //namespace'ом-блоком, а регистрация - namespace'ом файловой
            //области, и вместе они в один файл не помещаются (CS8956).
            if (compilation.GetTypeByMetadataName(ModuleInitializerName) is null)
            {
                files.Add(new GeneratedFile(HostNamespace + ".ModuleInitializerAttribute.g.cs", EmitPolyfill()));
            }

            return new GenerationResult(files, diagnostics);
        }

        private static HostModel? BuildFor(
            IReadOnlyList<HostBinder.Registration> registrations,
            KnownSymbols known,
            List<DiagnosticInfo> diagnostics
            )
        {
            return HostBinder.BuildModel(
                registrations,
                known,
                SerializationOptions.Default,
                GuardOptions.For(CompatGuards, CompatMaxDepth),
                FeatureOptions.Default,
                new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default),
                null,
                HostNamespace,
                HostDeclaration,
                HostNamespace + "." + HostTypeName,
                new List<string> { Exhauster },
                new List<string> { Injector },
                diagnostics,
                false
                );
        }

        private static List<HostBinder.Registration> Closure(INamedTypeSymbol root, KnownSymbols known)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { root, };
            var queue = new Queue<INamedTypeSymbol>();
            queue.Enqueue(root);

            var found = new List<HostBinder.Registration> { new HostBinder.Registration(root, true), };

            while (queue.Count > 0)
            {
                var type = queue.Dequeue();
                var candidates = new List<INamedTypeSymbol>();

                //сам субъект может быть коллекцией (§9.10): тогда обходить надо
                //его элемент, а не его члены
                ValueBinder.CollectSubjectCandidates(type, candidates);
                candidates.RemoveAll(c => SymbolEqualityComparer.Default.Equals(c, type));

                foreach (var member in Members(type, known))
                {
                    ValueBinder.CollectSubjectCandidates(member, candidates);
                }

                foreach (var derived in DerivedTypes(type, known))
                {
                    candidates.Add(derived);
                }

                foreach (var candidate in candidates)
                {
                    if (!seen.Add(candidate))
                    {
                        continue;
                    }

                    found.Add(new HostBinder.Registration(candidate, false));
                    queue.Enqueue(candidate);
                }
            }

            //порядок фиксирован: корень первым, остальные по имени - текст
            //порождаемого кода не должен зависеть от порядка обхода множества
            found.Sort((a, b) =>
                System.StringComparer.Ordinal.Compare(a.Type.ToDisplayString(), b.Type.ToDisplayString()));
            return found;
        }

        /// <summary>
        /// Типы членов, по которым идёт замыкание.
        ///
        /// <para>
        /// Фильтр повторяет <c>BindMember</c>, и важна здесь только одна
        /// сторона: <b>лишний</b> тип в замыкании может завалить корень, который
        /// без него связался бы (скажем, <c>[JsonIgnore]</c> над членом
        /// неподдержанного типа), а <b>недостающий</b> обнаружит себя сам -
        /// отказом «тип не зарегистрирован» на том же связывании.
        /// </para>
        /// </summary>
        private static IEnumerable<ITypeSymbol> Members(INamedTypeSymbol subject, KnownSymbols known)
        {
            for (var current = subject;
                current is not null && current.SpecialType != SpecialType.System_Object;
                current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member.IsStatic)
                    {
                        continue;
                    }

                    if (known.TryReadIgnore(member, out var condition)
                        && condition == KnownSymbols.JsonIgnoreConditionAlways)
                    {
                        continue;
                    }

                    switch (member)
                    {
                        case IPropertySymbol property
                            when property.Parameters.Length == 0
                                && property.DeclaredAccessibility == Accessibility.Public:
                            yield return property.Type;
                            break;

                        case IFieldSymbol field
                            when field.DeclaredAccessibility == Accessibility.Public
                                && known.Has(field, known.JsonInclude):
                            yield return field.Type;
                            break;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> DerivedTypes(INamedTypeSymbol subject, KnownSymbols known)
        {
            if (known.JsonDerivedType is null)
            {
                yield break;
            }

            foreach (var attribute in subject.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.JsonDerivedType))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is INamedTypeSymbol derived)
                {
                    yield return derived;
                }
            }
        }

        private static INamedTypeSymbol? Resolve(string referenceId, Compilation compilation)
        {
            return DocumentationCommentId.GetFirstSymbolForReferenceId(referenceId, compilation) as INamedTypeSymbol;
        }

        /// <summary>
        /// Причина отступления - первая из собранных диагностик. Их бывает
        /// много, но человеку нужна та, с которой начать; остальные он увидит,
        /// когда почините первую.
        /// </summary>
        private static string Explain(List<DiagnosticInfo> refusals)
        {
            if (refusals.Count == 0)
            {
                return "the type graph could not be bound";
            }

            var first = refusals[0].ToDiagnostic().GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            return refusals.Count == 1
                ? first
                : first + " (and " + (refusals.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " more)";
        }

        /// <summary>
        /// Регистрация быстрого пути. Отдельным файлом, а не в одном с хостом:
        /// хост печатается общим эмиттером, который про Compat ничего не знает
        /// и знать не должен.
        /// </summary>
        /// <summary>
        /// <c>[ModuleInitializer]</c> для таргетов, где его нет в BCL.
        /// <c>internal</c>: две сборки-потребителя не должны столкнуться
        /// именами, а компилятору внутреннего типа довольно.
        /// </summary>
        private static string EmitPolyfill()
        {
            var builder = new SourceBuilder();
            builder.Line("// <auto-generated/>");
            builder.Line("#pragma warning disable");
            builder.Line();
            builder.OpenBlock("namespace System.Runtime.CompilerServices");
            builder.Line("[global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]");
            builder.OpenBlock("internal sealed class ModuleInitializerAttribute : global::System.Attribute");
            builder.CloseBlock();
            builder.CloseBlock();

            return builder.ToString();
        }

        private static string EmitRegistration(HostModel model, IReadOnlyList<INamedTypeSymbol> served)
        {
            var builder = new SourceBuilder();
            builder.Line("// <auto-generated/>");
            builder.Line("#pragma warning disable");
            builder.Line("#nullable enable");
            builder.Line();

            builder.Line("namespace " + HostNamespace + ";");
            builder.Line();
            builder.OpenBlock(HostDeclaration);

            builder.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            builder.OpenBlock("internal static void RegisterWithCompatFacade()");

            foreach (var type in served
                .OrderBy(t => t.ToDisplayString(), System.StringComparer.Ordinal))
            {
                var full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var subject = model.Subjects.First(s => s.FullName == full);
                var binding = BindingTypeName + "<" + full + ">";

                builder.Line(binding + ".Register(");
                builder.Indent();
                builder.Line("static value =>");
                builder.Line("{");
                builder.Indent();
                builder.Line("using var exhauster = new " + Exhauster + "();");
                builder.Line("Serialize(exhauster, value);");
                builder.Line("return exhauster.ToArray();");
                builder.Unindent();
                builder.Line("},");
                builder.Line("static utf8 =>");
                builder.Line("{");
                builder.Indent();
                builder.Line("Deserialize(" + Injector + ".Instance, utf8, out " + subject.Declaration + " result);");
                builder.Line("return result!;");
                builder.Unindent();
                builder.Line("}");
                builder.Unindent();
                builder.Line(");");
                builder.Line();
            }

            builder.CloseBlock();
            builder.CloseBlock();

            return builder.ToString();
        }
    }
}
