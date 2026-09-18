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

        /// <summary>
        /// Перегрузка без быстрого пути не повод порождать код. <c>T</c> у неё
        /// известен ровно так же, но работа уходит эталону безусловно - значит,
        /// порождённый хост был бы мёртвым, а <c>JGD001</c> на несвязавшемся
        /// типе - жалобой на вызов, которому быстрый путь всё равно не достался
        /// бы.
        /// </summary>
        [Fact]
        public void A_call_to_an_overload_without_a_fast_path_generates_nothing()
        {
            const string source = @"
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonSerializer = JsonGoddess.Compat.JsonSerializer;

namespace Sample
{
    public class Order
    {
        public int Id { get; set; }
    }

    public static class Caller
    {
        //контракт назван явно - фасад обязан уйти к эталону, а генератор молчать
        public static Order? A(JsonElement element) => JsonSerializer.Deserialize<Order>(element);

        //модель эталона на выходе - быстрого пути нет
        public static JsonNode? B(Order order) => JsonSerializer.SerializeToNode(order);

        //тип назван не параметром, а значением
        public static string C(Order order) => JsonSerializer.Serialize(order, typeof(Order));
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);
            Assert.Empty(run.GeneratedFiles);
            Assert.Empty(run.GeneratorDiagnostics);
        }

        [Fact]
        public void A_type_reached_only_through_a_slow_overload_is_not_registered()
        {
            const string source = @"
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonSerializer = JsonGoddess.Compat.JsonSerializer;

namespace Sample
{
    public class Fast
    {
        public int Id { get; set; }
    }

    public class Slow
    {
        public int Id { get; set; }
    }

    public static class Caller
    {
        public static string A(Fast value) => JsonSerializer.Serialize(value);
        public static Slow? B(JsonElement element) => JsonSerializer.Deserialize<Slow>(element);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);

            var host = Host(run);
            Assert.Contains("Read_Sample_Fast", host);
            Assert.DoesNotContain("Sample_Slow", host);
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

        /// <summary>
        /// Член типа <c>object</c> или <c>JsonElement</c> уводит весь тип к
        /// эталону.
        ///
        /// <para>
        /// Отказ здесь важнее обычного. По форме это самый обычный класс или
        /// структура, и без отдельной проверки он связывался бы <b>успешно</b>:
        /// публичных членов у <c>object</c> нет, и документ выходил бы
        /// <c>{}</c>; у <c>JsonElement</c> публичное свойство одно, и выходило
        /// бы <c>{"ValueKind":4}</c> там, где эталон пишет <c>1</c>. То есть
        /// валидный код, дающий другой документ, молча.
        /// </para>
        ///
        /// <para>
        /// Нашлось прогоном их корпуса поверх фасада (<c>StjFacadeFixture</c>),
        /// и иначе найтись почти не могло: обычному хосту такой тип надо
        /// зарегистрировать руками, а Compat-слой считает замыкание сам и
        /// доходит до <c>object</c> на первом же чужом типе.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("object?", "object")]
        [InlineData("global::System.Text.Json.JsonElement", "JsonElement")]
        [InlineData("global::System.Text.Json.Nodes.JsonNode?", "JsonNode")]
        public void A_member_whose_shape_is_known_only_at_run_time_sends_the_type_to_the_reference(
            string declaration, string expected
            )
        {
            var source = @"
using JsonGoddess.Compat;

namespace Sample
{
    public class Envelope
    {
        public int Id { get; set; }
        public " + declaration + @" Payload { get; set; }
    }

    public static class Caller
    {
        public static string Write(Envelope value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);

            var fallback = run.GeneratorDiagnostics.Single(d => d.Id == "JGD001");

            Assert.Equal(DiagnosticSeverity.Info, fallback.Severity);
            Assert.Contains("Sample.Envelope", fallback.GetMessage());
            Assert.Contains(expected, fallback.GetMessage());

            Assert.Empty(run.GeneratedFiles);
        }

        /// <summary>
        /// <c>int[]</c> и <c>int?[]</c> в одной сборке - два метода, а не один.
        ///
        /// <para>
        /// Имя метода коллекции строится по элементу, а собственный суффикс
        /// элемента о nullability молчал: он именует вид значения, а не место.
        /// Выходило <c>ArrayOf_Int32</c> на оба типа, и второй получал тело
        /// первого.
        /// </para>
        ///
        /// <para>
        /// У обычного хоста столкнуться им негде - два таких члена редко живут
        /// в одном дереве типов. У Compat-слоя хост <b>общий на всю сборку</b>,
        /// и там это случилось сразу: их <c>SimpleTestClass</c> с <c>int[]</c>
        /// и <c>SimpleTestClassWithNullables</c> с <c>int?[]</c> приехали в
        /// один файл. Проверка тут и живёт, потому что беда компатовая.
        /// </para>
        /// </summary>
        [Fact]
        public void A_nullable_element_does_not_share_a_method_with_a_non_nullable_one()
        {
            const string source = @"
using System.Collections.Generic;
using JsonGoddess.Compat;

namespace Sample
{
    public class Plain
    {
        public int[]? Values { get; set; }
        public List<int>? More { get; set; }
        public Dictionary<string, int>? Map { get; set; }
    }

    public class Maybe
    {
        public int?[]? Values { get; set; }
        public List<int?>? More { get; set; }
        public Dictionary<string, int?>? Map { get; set; }
    }

    public static class Caller
    {
        public static string Write(Plain value) => JsonSerializer.Serialize(value);
        public static string WriteMaybe(Maybe value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            //главное утверждение - код собирается: разъехавшиеся имена дают
            //не тихое расхождение, а CS1503 на порождённом файле
            Assert.Empty(run.CompilationErrors);

            var host = Host(run);

            Assert.Contains("ArrayOf_Int32(", host);
            Assert.Contains("ArrayOf_Int32_OrNull(", host);
            Assert.Contains("ListOf_Int32(", host);
            Assert.Contains("ListOf_Int32_OrNull(", host);
            Assert.Contains("MapOf_Int32(", host);
            Assert.Contains("MapOf_Int32_OrNull(", host);
        }

        /// <summary>
        /// Compat-хост печатается под свой sink, а имена - экранированными
        /// по-эталонному.
        ///
        /// <para>
        /// Проверяется здесь <b>устройство</b>, а не результат: что документ
        /// совпадает с эталонным байт в байт, проверяет
        /// <c>CompatTests/CompatGeneratorFixture</c> исполнением, и это
        /// утверждение сильнее. Но оно не отличает «имя напечатано
        /// экранированным» от «имя экранировал sink», а разница тут в цене:
        /// имена - константы, и платить за них в рантайме не надо.
        /// </para>
        /// </summary>
        [Fact]
        public void The_compat_host_writes_through_its_own_sink_and_bakes_escaped_names()
        {
            const string source = @"
using JsonGoddess.Compat;

namespace Sample
{
    public class Письмо
    {
        public string? Текст { get; set; }
    }

    public static class Caller
    {
        public static string Write(Письмо value) => JsonSerializer.Serialize(value);
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);

            var host = Host(run);

            //sink свой - у обычного хоста здесь стоял бы PooledUtf8Exhauster
            Assert.Contains("global::JsonGoddess.CompatUtf8Exhauster", host);
            Assert.DoesNotContain("PooledUtf8Exhauster", host);

            //Различает формы ДВОЙНОЙ слэш, и это не придирка к тексту.
            //SourceBuilder печатает любой не-ASCII символ C#-escape'ом, поэтому
            //одиночный \u0422 стоит в обоих хостах и ничего не доказывает. У compat'а
            //в документ уезжает сам JSON-escape, то есть строка из символов
            //'\', 'u', '0', '4', '2', '2', - и её обратный слэш C#-литерал удваивает.
            Assert.Contains("\\\\u0422\\\\u0435\\\\u043A\\\\u0441\\\\u0442", host);

            //а на чтении - сырое имя: диспетчер сличает его уже
            //разэкранированным, и JSON-escape там не подошёл бы ни к чему
            Assert.Contains("\"\\u0422\\u0435\\u043A\\u0441\\u0442\"u8", host);
        }

        /// <summary>
        /// Обычный хост от появления экранирования не изменился ни на байт.
        /// Это тот же инвариант, что у <c>JsonGuard</c> и <c>JsonFeature</c>:
        /// выключенное решение обязано не оставлять следа в тексте.
        /// </summary>
        [Fact]
        public void An_ordinary_host_is_untouched_by_the_compat_escaping()
        {
            const string source = @"
namespace Sample
{
    [global::JsonGoddess.JsonExhauster(typeof(global::JsonGoddess.PooledUtf8Exhauster))]
    [global::JsonGoddess.JsonInjector(typeof(global::JsonGoddess.DefaultInjector))]
    [global::JsonGoddess.JsonSubject(typeof(Письмо), true)]
    public partial class Host
    {
    }

    public class Письмо
    {
        public string? Текст { get; set; }
    }
}
";
            var run = GeneratorHarness.Run(source);

            Assert.Empty(run.CompilationErrors);

            var text = run.SingleGeneratedFile;

            //имя уезжает в документ как есть; Т в тексте - это C#-escape
            //от SourceBuilder, а не JSON-escape, и потому одиночный
            Assert.Contains("\\u0422\\u0435\\u043A\\u0441\\u0442", text);
            Assert.DoesNotContain("\\\\u0422", text);
        }

        /// <summary>
        /// <c>JsonGoddessCompatStrict</c> поднимает громкость, <b>не меняя
        /// решения</b> (§10 плана).
        ///
        /// <para>
        /// Второе проверяется наравне с первым и важнее его: свойство заводится
        /// затем, чтобы отступление к эталону не проходило незамеченным, - а не
        /// затем, чтобы отступлений стало меньше. Тип, который обслужить
        /// нельзя, обязан уйти эталону при любом значении.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(null, DiagnosticSeverity.Info)]
        [InlineData("info", DiagnosticSeverity.Info)]
        [InlineData("warning", DiagnosticSeverity.Warning)]
        [InlineData("Warning", DiagnosticSeverity.Warning)]
        [InlineData("error", DiagnosticSeverity.Error)]
        public void The_strictness_property_changes_the_volume_of_JGD001(string? strict, DiagnosticSeverity expected)
        {
            var run = Strict(Refusing, strict);

            var fallback = run.GeneratorDiagnostics.Single(d => d.Id == "JGD001");

            Assert.Equal(expected, fallback.Severity);

            //решение то же самое: тип по-прежнему уходит эталону, и кода по
            //нему по-прежнему нет
            Assert.Empty(run.GeneratedFiles);
            Assert.Contains("Sample.Weird", fallback.GetMessage());
        }

        /// <summary>
        /// Строгость - это <b>нижняя граница</b>, а не точное значение.
        /// <c>JGD002</c> объявлен <c>Warning</c>, потому что это дыра в
        /// генераторе, и <c>JsonGoddessCompatStrict=info</c> не имеет права
        /// сделать его тише.
        /// </summary>
        [Theory]
        [InlineData("info")]
        [InlineData("warning")]
        public void The_strictness_property_never_makes_a_diagnostic_quieter(string strict)
        {
            //JGD002 руками не воспроизвести - это расхождение двух наших же
            //шагов. Проверяется само правило, на нём же и построенное
            Assert.Null(Settings(strict).Raise(DiagnosticSeverity.Warning));
            Assert.Null(Settings(strict).Raise(DiagnosticSeverity.Error));
        }

        /// <summary>
        /// Опечатка в значении - предупреждение, а не молчание. Человек просил
        /// строгости, не получил её, и узнать об этом он должен от компилятора.
        /// </summary>
        [Fact]
        public void An_unrecognized_strictness_value_is_reported_instead_of_ignored()
        {
            var run = Strict(Refusing, "wraning");

            var complaint = run.GeneratorDiagnostics.Single(d => d.Id == "JGD003");

            Assert.Equal(DiagnosticSeverity.Warning, complaint.Severity);
            Assert.Contains("wraning", complaint.GetMessage());

            //а решение и громкость остались прежними: непонятое значение
            //игнорируется, но не подменяется догадкой
            Assert.Equal(
                DiagnosticSeverity.Info,
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD001").Severity
                );
        }

        /// <summary>
        /// Свойство, которого нет, не порождает ни диагностики, ни разницы в
        /// тексте - тот же инвариант, что у <c>JsonGuard</c> и
        /// <c>JsonFeature</c>.
        /// </summary>
        [Fact]
        public void A_project_that_never_heard_of_the_property_sees_nothing_new()
        {
            var run = GeneratorHarness.Run(Graph);

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Equal(Host(Strict(Graph, null)), Host(run));
        }

        private const string Refusing = @"
using JsonGoddess.Compat;
using System.Text.Json.Serialization;

namespace Sample
{
    public class Weird
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public int Amount { get; set; }
    }

    public static class Caller
    {
        public static string Write(Weird value) => JsonSerializer.Serialize(value);
    }
}
";

        private static GeneratorRun Strict(string source, string? strict)
        {
            var properties = new Dictionary<string, string>();
            if (strict is not null)
            {
                properties.Add("JsonGoddessCompatStrict", strict);
            }

            return GeneratorHarness.Run(new[] { new SourceFile("Subject.cs", source), }, properties);
        }

        private static JsonGoddess.Generator.Binding.CompatSettings Settings(string strict)
        {
            return JsonGoddess.Generator.Binding.CompatSettings.Read(
                new SingleProperty(JsonGoddess.Generator.Binding.CompatSettings.StrictProperty, strict)
                );
        }

        /// <summary>
        /// Одно свойство вместо провайдера: <see cref="JsonGoddess.Generator.Binding.CompatSettings.Raise"/>
        /// проверяется как правило, а не через прогон генератора, потому что
        /// <c>JGD002</c> руками не воспроизвести.
        /// </summary>
        private sealed class SingleProperty : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
        {
            private readonly string _key;
            private readonly string _value;

            public SingleProperty(string key, string value)
            {
                _key = key;
                _value = value;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (key == _key)
                {
                    value = _value;
                    return true;
                }

                value = null!;
                return false;
            }
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
