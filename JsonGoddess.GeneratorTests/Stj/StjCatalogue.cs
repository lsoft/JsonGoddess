using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Приговор генератора одному типу корпуса.
    ///
    /// Приговор <b>снимается прогоном</b>, а не пишется руками, и в этом вся
    /// суть: рукописная таблица совместимости неизбежно врёт - не со зла, а
    /// потому что устаревает молча. Эта устареть не может, потому что её
    /// заново печатает тот же тест, который её проверяет.
    /// </summary>
    public sealed class StjVerdict
    {
        public required string Name { get; init; }

        public required string FullName { get; init; }

        /// <summary>Сколько типов пришлось зарегистрировать вместе с корневым.</summary>
        public required int ClosureSize { get; init; }

        public required bool Accepted { get; init; }

        /// <summary>Идентификатор нашей диагностики - <c>null</c> у принятых.</summary>
        public required string? DiagnosticId { get; init; }

        /// <summary>Причина отказа словами генератора - <c>null</c> у принятых.</summary>
        public required string? Reason { get; init; }

        /// <summary>
        /// <b>Все</b> причины, а не первая.
        ///
        /// Разница обошлась дорого: по первой причине выходило, что их
        /// <c>SimpleStruct</c> держит двадцать типов и поддержка структур
        /// откроет их разом. Открылся один. Остальные девятнадцать просто
        /// назвали следующую свою причину - у них их было по нескольку с
        /// самого начала. Считать «что разблокирует работа» по первой причине
        /// нельзя, и теперь этого не сделать.
        /// </summary>
        public required IReadOnlyList<string> Reasons { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Прогон корпуса <c>System.Text.Json</c> через наш генератор.
    ///
    /// Хост печатается на каждый тип свой, и это не расточительство: отказ у
    /// нас устроен по принципу «всё или ничего на хост» (один негодный член -
    /// и хост не порождает ничего), поэтому общий хост дал бы один отказ на
    /// весь корпус вместо приговора каждому типу.
    ///
    /// Вместе с корневым типом регистрируется его <b>замыкание</b> по членам:
    /// зарегистрировать один корневой тип значило бы получить отказ «вложенный
    /// тип не зарегистрирован» и записать в таблицу совместимости нашу же
    /// небрежность вместо настоящей причины.
    /// </summary>
    public static class StjCatalogue
    {
        public const string HostNamespace = "JsonGoddess.GeneratorTests.Stj.Hosts";

        private static readonly Lazy<CSharpCompilation> _compilation =
            new Lazy<CSharpCompilation>(() => GeneratorHarness.CreateCompilation(StjCorpus.Files));

        private static readonly Lazy<IReadOnlyList<INamedTypeSymbol>> _candidates =
            new Lazy<IReadOnlyList<INamedTypeSymbol>>(CollectCandidates);

        private static readonly Lazy<IReadOnlyList<StjVerdict>> _verdicts =
            new Lazy<IReadOnlyList<StjVerdict>>(Evaluate);

        public static CSharpCompilation Compilation => _compilation.Value;

        /// <summary>
        /// Типы корпуса, которым выносится приговор.
        ///
        /// Отбор нарочно щедрый: берутся все публичные необобщённые типы, а не
        /// те, про которые заранее понятно, что мы их потянем. Структуры,
        /// абстрактные классы и типы без конструктора остаются в списке именно
        /// затем, чтобы отказ по ним стоял в таблице строкой, а не исчезал из
        /// знаменателя.
        /// </summary>
        public static IReadOnlyList<INamedTypeSymbol> Candidates => _candidates.Value;

        public static IReadOnlyList<StjVerdict> Verdicts => _verdicts.Value;

        private static IReadOnlyList<INamedTypeSymbol> CollectCandidates()
        {
            var corpus = Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.Tests.ITestClass")
                ?? throw new InvalidOperationException(
                    "В корпусе нет System.Text.Json.Serialization.Tests.ITestClass - значит, до компиляции "
                    + "доехали не те файлы."
                    );

            var result = new List<INamedTypeSymbol>();

            foreach (var member in corpus.ContainingNamespace.GetTypeMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public
                    || member.IsGenericType
                    || member.TypeKind is not (TypeKind.Class or TypeKind.Struct)
                    || member.IsStatic)
                {
                    continue;
                }

                result.Add(member);
            }

            return result
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Замыкание по членам: какие ещё типы корпуса придётся зарегистрировать,
        /// чтобы корневой вообще имел шанс.
        ///
        /// Обход нарочно шире, чем связывание: из обобщённого типа берутся все
        /// аргументы, а не только те, что мы умеем. Там, где хост в итоге
        /// принимается, это одно и то же (члены могут быть только builtin,
        /// субъектом, <c>List&lt;T&gt;</c>, <c>T[]</c> или словарём); там, где
        /// отказывается, лишняя регистрация ничего не меняет - хост уже
        /// отказан по члену.
        /// </summary>
        public static INamedTypeSymbol Resolve(StjVerdict verdict)
        {
            return Candidates.Single(c => c.Name == verdict.Name);
        }

        public static IReadOnlyList<INamedTypeSymbol> ClosureOf(INamedTypeSymbol root) => Closure(root);

        /// <summary>
        /// Их <c>ITestClass</c>: тип, у которого есть и образец
        /// (<c>Initialize</c>), и собственная проверка (<c>Verify</c>).
        /// </summary>
        public static bool IsTestClass(INamedTypeSymbol type)
        {
            return type.AllInterfaces.Any(
                i => i.Name == "ITestClass" && i.ContainingNamespace.ToDisplayString() == StjCorpus.Namespace
                );
        }

        /// <summary>
        /// Их документ - выражение, дающее UTF-8-байты, или <c>null</c>.
        ///
        /// Имена <c>s_json</c> и <c>s_data</c> - соглашение корпуса, а не наше,
        /// и живут в нём оба: где-то документ лежит строкой, где-то сразу
        /// байтами. Признавать только одно из двух значило бы потерять
        /// половину документов и не заметить.
        /// </summary>
        public static string? DocumentExpression(INamedTypeSymbol type, string subject)
        {
            if (HasStaticField(type, "s_data", f => f.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, }))
            {
                return subject + ".s_data";
            }

            if (HasStaticField(type, "s_json", f => f.Type.SpecialType == SpecialType.System_String))
            {
                return "global::System.Text.Encoding.UTF8.GetBytes(" + subject + ".s_json)";
            }

            return null;
        }

        public static bool HasDocument(INamedTypeSymbol type) =>
            DocumentExpression(type, "x") is not null;

        private static bool HasStaticField(INamedTypeSymbol type, string name, Func<IFieldSymbol, bool> shape)
        {
            return type.GetMembers(name)
                .OfType<IFieldSymbol>()
                .Any(f => f.IsStatic && f.DeclaredAccessibility == Accessibility.Public && shape(f));
        }

        private static IReadOnlyList<INamedTypeSymbol> Closure(INamedTypeSymbol root)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { root, };
            var order = new List<INamedTypeSymbol> { root, };
            var queue = new Queue<INamedTypeSymbol>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var type = queue.Dequeue();

                for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
                {
                    foreach (var member in current.GetMembers())
                    {
                        var memberType = MemberType(member);
                        if (memberType is null)
                        {
                            continue;
                        }

                        foreach (var reachable in Unwrap(memberType))
                        {
                            if (!IsCorpusType(reachable) || !seen.Add(reachable))
                            {
                                continue;
                            }

                            order.Add(reachable);
                            queue.Enqueue(reachable);
                        }
                    }
                }
            }

            return order;
        }

        private static ITypeSymbol? MemberType(ISymbol member)
        {
            //[JsonIgnore] пропускается здесь по той же причине, по которой его
            //смотрит связыватель: член, которого нет в документе, не может
            //потребовать регистрации типа
            if (member.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute"))
            {
                return null;
            }

            return member switch
            {
                IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false, Parameters.Length: 0 } p => p.Type,
                IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, } f => f.Type,
                _ => null,
            };
        }

        private static IEnumerable<INamedTypeSymbol> Unwrap(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                foreach (var inner in Unwrap(array.ElementType))
                {
                    yield return inner;
                }

                yield break;
            }

            if (type is not INamedTypeSymbol named)
            {
                yield break;
            }

            if (named.IsGenericType)
            {
                //сам обобщённый тип тоже идёт в замыкание, если он из корпуса.
                //Иначе приговор ему выносился бы отказом «не зарегистрирован» -
                //то есть в таблице стояла бы небрежность нашего обхода вместо
                //настоящей причины («обобщённые типы не поддержаны»)
                yield return named;

                foreach (var argument in named.TypeArguments)
                {
                    foreach (var inner in Unwrap(argument))
                    {
                        yield return inner;
                    }
                }

                yield break;
            }

            yield return named;
        }

        private static bool IsCorpusType(INamedTypeSymbol type)
        {
            return type.TypeKind is TypeKind.Class or TypeKind.Struct
                && !type.IsStatic
                && type.Locations.Any(l => l.IsInSource);
        }

        /// <summary>
        /// Хост на один корневой тип. Своё пространство имён и полностью
        /// квалифицированные имена субъектов: корпус живёт в
        /// <c>System.Text.Json.Serialization.Tests</c>, и печатать хост туда же
        /// значило бы разрешать имена в чужом пространстве имён - там, где
        /// <c>Json</c> и <c>Serialization</c> уже что-то значат.
        /// </summary>
        public static string HostSource(INamedTypeSymbol root, IReadOnlyList<INamedTypeSymbol> closure)
        {
            var builder = new StringBuilder();

            builder.AppendLine("namespace " + HostNamespace);
            builder.AppendLine("{");

            builder.AppendLine("    [global::JsonGoddess.JsonExhauster(typeof(global::JsonGoddess.PooledUtf8Exhauster))]");
            builder.AppendLine("    [global::JsonGoddess.JsonInjector(typeof(global::JsonGoddess.DefaultInjector))]");

            foreach (var subject in closure)
            {
                builder.AppendLine(
                    "    [global::JsonGoddess.JsonSubject(typeof("
                    + subject.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + "), "
                    + (SymbolEqualityComparer.Default.Equals(subject, root) ? "true" : "false")
                    + ")]"
                    );
            }

            builder.AppendLine("    public partial class " + HostName(root));
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        public static string HostName(INamedTypeSymbol root) => root.Name + "_Host";

        private static IReadOnlyList<StjVerdict> Evaluate()
        {
            var driver = GeneratorHarness.CreateDriver();
            var result = new List<StjVerdict>();

            foreach (var candidate in Candidates)
            {
                var closure = Closure(candidate);
                var host = HostSource(candidate, closure);

                var compilation = Compilation.AddSyntaxTrees(
                    CSharpSyntaxTree.ParseText(
                        host,
                        new CSharpParseOptions(LanguageVersion.CSharp11),
                        path: HostName(candidate) + ".cs"
                        )
                    );

                var run = GeneratorHarness.Continue(driver, compilation);

                var errors = run.GeneratorDiagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                result.Add(
                    new StjVerdict
                    {
                        Name = candidate.Name,
                        FullName = candidate.ToDisplayString(),
                        ClosureSize = closure.Count,
                        Accepted = errors.Count == 0,
                        DiagnosticId = errors.Count == 0 ? null : errors[0].Id,
                        Reason = errors.Count == 0 ? null : errors[0].GetMessage(),
                        Reasons = errors.Select(e => e.Id + " " + e.GetMessage()).ToList(),
                    }
                    );
            }

            return result;
        }
    }
}
