using System.Collections.Generic;
using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Перехват вызовов фасада (§10, маршрут A) - со стороны генератора: что
    /// он печатает, о чём молчит и о чём говорит.
    ///
    /// <para>
    /// Сквозная проверка «документ тот же, что у эталона» живёт не здесь, а в
    /// <c>JsonGoddess.CompatTests</c>: она требует исполнить порождённый код, а
    /// этот проект его только читает.
    /// </para>
    /// </summary>
    public class CompatGeneratorTextFixture
    {
        private const string Graph = @"
using System.Collections.Generic;
using JsonGoddess.Compat;

namespace Sample
{
    public class Order
    {
        public int Id { get; set; }
        public Address? ShipTo { get; set; }
        public List<Line>? Lines { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
    }

    public class Line
    {
        public string? Sku { get; set; }
    }

    public static class Caller
    {
        public static string Write(Order order) => JsonSerializer.Serialize(order);
    }
}
";

        private static readonly IReadOnlyDictionary<string, string> Disabled =
            new Dictionary<string, string> { { "JsonGoddessCompat", "disable" }, };

        private static string Host(GeneratorRun run) =>
            run.GeneratedFiles["JsonGoddess.Compat.Generated.JsonGoddessCompatHost.g.cs"];

        private static string Registration(GeneratorRun run) =>
            run.GeneratedFiles["JsonGoddess.Compat.Generated.JsonGoddessCompatHost.Registration.g.cs"];

        [Fact]
        public void A_call_to_the_facade_generates_a_host_and_a_registration()
        {
            var run = GeneratorHarness.Run(Graph);

            Assert.Empty(run.CompilationErrors);
            Assert.Contains("JsonGoddess.Compat.Generated.JsonGoddessCompatHost.g.cs", run.GeneratedFiles.Keys);
            Assert.Contains(
                "JsonGoddess.Compat.Generated.JsonGoddessCompatHost.Registration.g.cs", run.GeneratedFiles.Keys
                );
        }

        [Fact]
        public void The_type_graph_is_walked_transitively_without_a_single_attribute()
        {
            //ни Address, ни Line в вызове не упоминаются - только Order
            var host = Host(GeneratorHarness.Run(Graph));

            Assert.Contains("Read_Sample_Order", host);
            Assert.Contains("Read_Sample_Address", host);
            Assert.Contains("Read_Sample_Line", host);
            Assert.Contains("ReadCollection_ListOf_Sample_Line", host);
        }

        [Fact]
        public void Only_the_called_type_gets_an_entry_point()
        {
            //точка входа - у корня; Address и Line обслуживаются, но извне их
            //никто не просил, и публичного входа у них быть не должно
            var host = Host(GeneratorHarness.Run(Graph));

            Assert.Contains("public static void Deserialize(", host);
            Assert.Equal(1, Occurrences(host, "public static void Deserialize("));
            Assert.Equal(1, Occurrences(host, "public static void Serialize("));
        }

        [Fact]
        public void The_registration_runs_from_a_module_initializer()
        {
            var registration = Registration(GeneratorHarness.Run(Graph));

            Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", registration);
            Assert.Contains("global::JsonGoddess.Compat.CompatBinding<global::Sample.Order>.Register(", registration);
        }

        /// <summary>
        /// Строгость фасада - ровно эталонная (см. <c>CompatBinder.CompatGuards</c>).
        /// Проверяется по тексту, потому что здесь вопрос именно в том, какой
        /// код напечатан, а не в том, как он себя ведёт.
        /// </summary>
        [Fact]
        public void The_compat_host_is_as_strict_as_the_reference_and_no_stricter()
        {
            var host = Host(GeneratorHarness.Run(Graph));

            //эталон отвергает всегда - печатаем
            Assert.Contains("Unexpected trailing content", host);
            Assert.Contains("ReadNumberRawStrict", host);
            Assert.Contains("context.Depth", host);

            //эталон по умолчанию разрешает - НЕ печатаем: страж сделал бы нас
            //строже заменяемой библиотеки, а это такое же расхождение, как
            //быть снисходительнее
            Assert.DoesNotContain("Unknown property", host);
            Assert.DoesNotContain("Duplicate property", host);
        }

        [Fact]
        public void A_type_that_cannot_be_served_falls_back_with_JGD001_and_does_not_break_the_build()
        {
            const string source = @"
using JsonGoddess.Compat;
using System.Text.Json.Serialization;

namespace Sample
{
    public class Weird
    {
        public int Id { get; set; }

        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public int Amount { get; set; }
    }

    public static class Caller
    {
        public static string Write(Weird value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);

            var fallback = run.GeneratorDiagnostics.Single(d => d.Id == "JGD001");

            //Info, а не Warning: отступление к эталону - объявленное поведение,
            //а не поломка. Документ тот же, скорость прежняя
            Assert.Equal(DiagnosticSeverity.Info, fallback.Severity);
            Assert.Contains("Sample.Weird", fallback.GetMessage());
            Assert.Contains("JsonNumberHandling", fallback.GetMessage());

            //ничего не напечатано - значит фасад на этом типе уйдёт к эталону
            Assert.Empty(run.GeneratedFiles);
        }

        [Fact]
        public void One_refused_type_does_not_take_the_others_down_with_it()
        {
            const string source = @"
using JsonGoddess.Compat;
using System.Text.Json.Serialization;

namespace Sample
{
    public class Good
    {
        public int Id { get; set; }
    }

    public class Weird
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public int Amount { get; set; }
    }

    public static class Caller
    {
        public static string A(Good value) => JsonSerializer.Serialize(value);
        public static string B(Weird value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);
            Assert.Single(run.GeneratorDiagnostics.Where(d => d.Id == "JGD001"));

            var host = Host(run);
            Assert.Contains("Read_Sample_Good", host);
            Assert.DoesNotContain("Read_Sample_Weird", host);
        }

        [Fact]
        public void A_generic_caller_whose_type_is_not_known_statically_is_not_intercepted()
        {
            const string source = @"
using JsonGoddess.Compat;

namespace Sample
{
    public static class Caller
    {
        public static string Write<T>(T value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);
            Assert.Empty(run.GeneratedFiles);
            Assert.Empty(run.GeneratorDiagnostics);
        }

        [Fact]
        public void The_build_property_turns_the_interception_off_without_removing_the_reference()
        {
            var run = GeneratorHarness.Run(
                new[] { new SourceFile("Subject.cs", Graph), },
                Disabled
                );

            Assert.Empty(run.CompilationErrors);
            Assert.Empty(run.GeneratedFiles);
        }

        [Fact]
        public void A_call_to_the_real_System_Text_Json_is_left_alone()
        {
            const string source = @"
namespace Sample
{
    public class Order
    {
        public int Id { get; set; }
    }

    public static class Caller
    {
        public static string Write(Order order) => System.Text.Json.JsonSerializer.Serialize(order);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);
            Assert.Empty(run.GeneratedFiles);
        }

        private static int Occurrences(string text, string needle)
        {
            var count = 0;
            var at = 0;

            while ((at = text.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }
    }
}
