using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Отказы.
    ///
    /// Каждый из них - применение одного принципа: молча выданный валидный код,
    /// дающий <b>другой документ</b>, хуже, чем несобравшийся проект. Поэтому
    /// неизвестный тип члена, дубликат имени и неподдержанная форма типа - это
    /// ошибки, а не пропуски.
    /// </summary>
    public class GeneratorDiagnosticsFixture
    {
        [Fact]
        public void Host_must_be_partial()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload { public int Id { get; set; } }

    [JsonSubject(typeof(Payload), true)]
    public class Serializer { }
}
");

            Assert.Contains("JGD020", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        [Fact]
        public void Nested_host_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload { public int Id { get; set; } }

    public partial class Outer
    {
        [JsonSubject(typeof(Payload), true)]
        public partial class Serializer { }
    }
}
");

            Assert.Contains("JGD025", run.DiagnosticIds);
        }

        [Fact]
        public void Unsupported_member_type_is_refused_rather_than_skipped()
        {
            //множества и прочие коллекции приезжают позже; пропустить член
            //нельзя - документ без него это другой документ
            var run = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        public System.Collections.Generic.HashSet<int>? Values { get; set; }
")
                );

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        /// <summary>
        /// Класс, который автор не зарегистрировал, - отказ, и отказ с
        /// указанием, что дописать. Догадка «раз это класс, значит обслужим»
        /// подменила бы явное решение автора нашим.
        /// </summary>
        [Fact]
        public void Unregistered_class_member_is_refused_with_the_missing_registration_named()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Line { public int Quantity { get; set; } }

    public class Payload
    {
        public int Id { get; set; }
        public Line? Head { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
            Assert.Contains(
                "[JsonSubject(typeof(Line), false)]",
                Assert.Single(run.GeneratorDiagnostics).GetMessage()
                );
        }

        /// <summary>
        /// Отказ обязан назвать <b>элемент</b>, а не коллекцию: человеку
        /// незачем догадываться, что именно в <c>List&lt;Line&gt;</c> нам
        /// незнакомо.
        /// </summary>
        [Fact]
        public void Refusal_inside_a_collection_names_the_element_type()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Line { public int Quantity { get; set; } }

    public class Payload
    {
        public System.Collections.Generic.List<Line>? Lines { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains(
                "[JsonSubject(typeof(Line), false)]",
                Assert.Single(run.GeneratorDiagnostics).GetMessage()
                );
        }

        [Fact]
        public void Registering_the_nested_type_is_all_it_takes()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Line { public int Quantity { get; set; } }

    public class Payload
    {
        public int Id { get; set; }
        public Line? Head { get; set; }
        public System.Collections.Generic.List<Line>? Lines { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    [JsonSubject(typeof(Line), false)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
        }

        /// <summary>
        /// Enum в числовом режиме не знает ни про регистр, ни про комбинации
        /// имён, поэтому ни <c>[Flags]</c>, ни не-ASCII ему не мешают: он
        /// пишется числом подлежащего типа, и всё.
        /// </summary>
        [Fact]
        public void Numeric_enum_is_served_even_with_flags_and_non_ascii_names()
        {
            var run = GeneratorHarness.Run(@"
using System;
using JsonGoddess;

namespace Demo
{
    [Flags]
    public enum Access { None = 0, Read = 1, Write = 2 }

    public enum Cyrillic { Черновик, Отправлено }

    public class Payload
    {
        public Access Access { get; set; }
        public Cyrillic Cyrillic { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
        }

        /// <summary>
        /// А в строковом режиме каждый из трёх случаев - отказ, и у каждого своя
        /// причина. <c>[Flags]</c> - потому что у эталона там своя грамматика
        /// (<c>"Read, Write"</c>); не-ASCII - потому что он свернул бы регистр
        /// по Unicode, а мы по ASCII; два имени на одно значение - потому что
        /// какое из них он напишет, не определено и в самом BCL.
        /// </summary>
        [Theory]
        [InlineData("[System.Flags] public enum E { None = 0, Read = 1 }")]
        [InlineData("public enum E { Черновик, Отправлено }")]
        [InlineData("public enum E { None = 0, Default = 0 }")]
        public void String_enum_refuses_what_cannot_be_matched_exactly(string declaration)
        {
            var run = GeneratorHarness.Run(@"
using System;
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    " + declaration + @"

    public class Payload
    {
        public E Value { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        /// <summary>
        /// Любой другой конвертер - отказ, и это закрывает дыру шире enum'ов:
        /// молча проигнорировать <c>[JsonConverter]</c> значило бы выдать
        /// документ, которого эталон не выдаёт, и не сказать об этом.
        /// </summary>
        [Fact]
        public void Unknown_converter_on_a_type_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using System;
using JsonGoddess;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Demo
{
    public sealed class Odd : JsonConverter<E>
    {
        public override E Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) => default;
        public override void Write(Utf8JsonWriter writer, E value, JsonSerializerOptions o) { }
    }

    [JsonConverter(typeof(Odd))]
    public enum E { Draft, Sent }

    public class Payload { public E Value { get; set; } }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
        }

        /// <summary>
        /// Два атрибута, которые §6.1 перечислял как читаемые, а генератор не
        /// знал о них вовсе - найдено при сборке
        /// <c>docs/stj-divergences.md</c>. Молчать здесь было нельзя:
        /// <c>[JsonNumberHandling(WriteAsString)]</c> у эталона даёт
        /// <c>{"Amount":"5"}</c>, а у нас давало <c>{"Amount":5}</c>, то есть
        /// расходился сам документ. Проверено прогоном эталона.
        ///
        /// Второй из тех двух, <c>[JsonRequired]</c>, больше не отказ:
        /// присутствие отслеживается, см.
        /// <c>Required_member_is_served_and_its_absence_refuses_the_document</c>.
        /// </summary>
        [Theory]
        [InlineData("        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int Amount { get; set; }", "JsonNumberHandling")]
        [InlineData("        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Amount { get; set; }", "JsonNumberHandling")]
        public void Attribute_we_do_not_reproduce_is_refused_instead_of_ignored(string member, string named)
        {
            var run = GeneratorHarness.Run(Sources.Host(member));

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Contains(
                named,
                string.Join("\n", run.GeneratorDiagnostics.Select(d => d.GetMessage())),
                System.StringComparison.Ordinal
                );
        }

        /// <summary>
        /// Настройки читаются из <c>[JsonSourceGenerationOptions]</c> - того же
        /// атрибута, которым настраивается source-генератор эталона. Цена
        /// решения взять чужой атрибут, а не завести свой, - обязательство
        /// разобрать каждое его свойство: их двадцать семь, и молча пропустить
        /// хоть одно значило бы выдать документ, которого не просили.
        /// </summary>
        [Theory]
        [InlineData("WriteIndented = true")]
        [InlineData("DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull")]
        [InlineData("IncludeFields = true")]
        [InlineData("PropertyNameCaseInsensitive = true")]
        [InlineData("UseStringEnumConverter = true")]
        [InlineData("AllowTrailingCommas = true")]
        [InlineData("MaxDepth = 8")]
        public void Option_we_do_not_implement_is_refused(string option)
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Id { get; set; }",
                    "[JsonSourceGenerationOptions(" + option + ")]"
                    )
                );

            Assert.Contains("JGD028", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        /// <summary>
        /// Конструктор с <c>JsonSerializerDefaults.Web</c> включает разом
        /// camelCase, нечувствительность к регистру имён и чтение чисел из
        /// строк. Принять его значило бы принять три решения вместо одного, из
        /// которых два мы не исполняем.
        /// </summary>
        [Fact]
        public void Web_defaults_constructor_is_refused()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Id { get; set; }",
                    "[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]"
                    )
                );

            Assert.Contains("JGD028", run.DiagnosticIds);
        }

        /// <summary>
        /// А <c>GenerationMode</c> описывает, какой код порождать <b>их</b>
        /// генератору, и документа не касается вовсе - его можно не исполнять,
        /// ничего не нарушив.
        /// </summary>
        [Fact]
        public void Generation_mode_is_none_of_our_business_and_passes()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Id { get; set; }",
                    "[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]"
                    )
                );

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
        }

        [Fact]
        public void Converter_on_a_member_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    public enum E { Draft, Sent }

    public class Payload
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public E Value { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Contains("[JsonConverter]", Assert.Single(run.GeneratorDiagnostics).GetMessage());
        }

        /// <summary>
        /// Один негодный субъект гасит хост целиком, а не только себя: он мог
        /// быть чьим-то членом, и код без него не скомпилировался бы - поверх
        /// понятной диагностики приехала бы непонятная ошибка компилятора.
        /// </summary>
        [Fact]
        public void One_broken_subject_silences_the_whole_host()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Good { public int Id { get; set; } }

    public class Bad { public System.Collections.Generic.HashSet<int>? Values { get; set; } }

    [JsonSubject(typeof(Good), true)]
    [JsonSubject(typeof(Bad), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        [Fact]
        public void Two_members_cannot_share_one_json_name()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        [JsonPropertyName(""Id"")] public int Other { get; set; }
")
                );

            Assert.Contains("JGD023", run.DiagnosticIds);
        }

        [Fact]
        public void Name_that_requires_json_escaping_is_refused()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(@"        [JsonPropertyName(""a\""b"")] public int Id { get; set; }")
                );

            Assert.Contains("JGD027", run.DiagnosticIds);
        }

        /// <summary>
        /// Обязательный член обслуживается: присутствие отслеживается маской,
        /// объект собирается инициализатором (<c>new T()</c> у типа с
        /// <c>required</c>-членом - ошибка компиляции CS9035).
        /// </summary>
        [Theory]
        [InlineData("        public required int Id { get; set; }")]
        [InlineData("        [JsonRequired] public int Id { get; set; }")]
        [InlineData("        public required int Id { get; init; }")]
        public void Required_member_is_served_and_its_absence_refuses_the_document(string member)
        {
            var run = GeneratorHarness.Run(Sources.Host(member));

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
            Assert.Contains("was missing required properties including", run.SingleGeneratedFile, System.StringComparison.Ordinal);
            Assert.Contains("new global::Demo.Subject() { Id = set_Id, }", run.SingleGeneratedFile, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// <c>[SetsRequiredMembers]</c> на конструкторе снимает обязательность
        /// <b>целиком</b> - со всех членов субъекта, а не только с тех, что
        /// конструктор трогает. Это поведение эталона, снятое пробой: он не
        /// отказывает на пустом документе ни при параметризованном
        /// конструкторе, ни при конструкторе без параметров, ни даже когда
        /// обязательный член помечен <c>[JsonIgnore]</c>.
        ///
        /// Для нас это снимает целый узел: компилятор перестаёт требовать
        /// инициализатор (CS9035), значит отложенная сборка не нужна, а вместе
        /// с ней уходят и оба отказа §9.13.
        /// </summary>
        [Theory]
        [InlineData(@"
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public Subject() { Id = 1; }
        public required int Id { get; set; }
")]
        [InlineData(@"
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public Subject(int id) { Id = id; }
        public required int Id { get; set; }
")]
        [InlineData(@"
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public Subject() { Id = 1; }
        [JsonIgnore] public required int Id { get; set; }
        public int Other { get; set; }
")]
        public void Sets_required_members_turns_the_whole_requirement_off(string members)
        {
            var run = GeneratorHarness.Run(Sources.Host(members));

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);

            //ни маски присутствия, ни отказа по ней
            Assert.DoesNotContain("was missing required properties", run.SingleGeneratedFile, System.StringComparison.Ordinal);
            Assert.DoesNotContain("var required = 0UL;", run.SingleGeneratedFile, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// <c>[JsonFactory]</c> заменяет <c>new T()</c> в читателе - и только
        /// там. Запись объект не создаёт, значит фабрике в ней делать нечего.
        /// </summary>
        [Fact]
        public void Factory_replaces_the_new_expression_in_the_reader_only()
        {
            var run = GeneratorHarness.Run(Sources.Factory("global::Demo.Pool.Reuse()"));

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);

            var text = run.SingleGeneratedFile;

            Assert.Contains("var result = global::Demo.Pool.Reuse();", text, System.StringComparison.Ordinal);
            Assert.DoesNotContain("new global::Demo.Subject()", text, System.StringComparison.Ordinal);
            Assert.Equal(
                1,
                text.Split('\n').Count(l => l.Contains("global::Demo.Pool.Reuse()"))
                );
        }

        /// <summary>
        /// Выражение фабрики печатается дословно, поэтому всё, что генератор
        /// способен проверить вокруг него, он проверяет на компиляции.
        /// </summary>
        [Theory]
        //тип этому хосту не субъект - фабрику никто никогда не позовёт
        [InlineData("[JsonFactory(typeof(Demo.Other), \"null\")]", "not registered")]
        //две фабрики на один тип: оба выражения заменить одно new нельзя
        [InlineData("[JsonFactory(typeof(Demo.Subject), \"null\")] [JsonFactory(typeof(Demo.Subject), \"null\")]", "already has a factory")]
        //пустое выражение
        [InlineData("[JsonFactory(typeof(Demo.Subject), \"   \")]", "empty")]
        public void Factory_that_cannot_work_is_refused(string attributes, string expected)
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Subject
    {
        public int Id { get; set; }
    }

    public class Other
    {
        public int Id { get; set; }
    }

    " + attributes + @"
    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
");

            Assert.Contains("JGD031", run.DiagnosticIds);
            Assert.Contains(
                expected,
                string.Join("\n", run.GeneratorDiagnostics.Select(d => d.GetMessage())),
                System.StringComparison.Ordinal
                );
        }

        /// <summary>
        /// Фабрика и конструктор десериализации спорят за одно место: первая
        /// отдаёт готовый объект, второй требует передать ему аргументы.
        /// Выбрать за автора нельзя - оба он написал сам.
        /// </summary>
        [Fact]
        public void Factory_and_a_parameterised_constructor_are_refused_together()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Subject
    {
        public Subject(int id)
        {
            Id = id;
        }

        public int Id { get; set; }
    }

    public static class Pool
    {
        public static Subject Reuse() => new Subject(0);
    }

    [JsonFactory(typeof(Demo.Subject), ""global::Demo.Pool.Reuse()"")]
    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
");

            Assert.Contains("JGD031", run.DiagnosticIds);
            Assert.Contains(
                "nowhere to pass them",
                string.Join("\n", run.GeneratorDiagnostics.Select(d => d.GetMessage())),
                System.StringComparison.Ordinal
                );
        }

        /// <summary>
        /// Фабрика снимает с обязательного члена отложенную сборку: запрет
        /// CS9035 адресован выражению <c>new T()</c>, которого с фабрикой в
        /// коде нет вовсе, а присвоить <c>required</c>-член после создания
        /// объекта язык позволяет всегда. Проверка присутствия при этом
        /// остаётся.
        /// </summary>
        [Fact]
        public void Factory_lets_a_required_member_be_assigned_in_place()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Subject
    {
        public required int Id { get; set; }
    }

    public static class Pool
    {
        public static Subject Reuse() => new Subject { Id = 0, };
    }

    [JsonFactory(typeof(Demo.Subject), ""global::Demo.Pool.Reuse()"")]
    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);

            var text = run.SingleGeneratedFile;

            Assert.Contains("result.Id = ", text, System.StringComparison.Ordinal);
            Assert.DoesNotContain("var set_Id", text, System.StringComparison.Ordinal);
            Assert.Contains("was missing required properties including", text, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Обязательный член у структуры и у полиморфной пары. Обе формы
        /// строят объект не так, как обычный класс - у структуры нет ссылки,
        /// у наследника читатель вызывается изнутри чужого, - и обе обязаны
        /// напечатать инициализатор, иначе компилятор откажет (CS9035).
        /// </summary>
        [Fact]
        public void Required_member_compiles_in_a_struct_and_in_a_polymorphic_pair()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    public struct Point
    {
        public required int X { get; set; }
    }

    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal
    {
        public required string Name { get; set; }
    }

    public class Dog : Animal
    {
        public required int Legs { get; set; }
    }

    [JsonSubject(typeof(Point), true)]
    [JsonSubject(typeof(Animal), true)]
    [JsonSubject(typeof(Dog), false)]
    public partial class ShapeSerializer
    {
    }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
        }

        /// <summary>
        /// Обязательный член, связанный с параметром конструктора, - отказ, и
        /// причина не в трудности. Компилятор требует присвоить такой член в
        /// инициализаторе объекта (CS9035), а эталон setter члена-параметра не
        /// вызывает вовсе (§9.8). Законный код тут можно напечатать только
        /// ценой другого значения в объекте.
        /// </summary>
        [Fact]
        public void Required_member_bound_to_a_constructor_parameter_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Order
    {
        public Order(int id)
        {
            Id = id;
        }

        public required int Id { get; set; }
    }

    [JsonSubject(typeof(Order), true)]
    public partial class OrderSerializer
    {
    }
}
");

            Assert.NotEmpty(run.GeneratorDiagnostics);
            Assert.Contains(
                "never calls its setter",
                string.Join("\n", run.GeneratorDiagnostics.Select(d => d.GetMessage())),
                System.StringComparison.Ordinal
                );
        }

        /// <summary>
        /// <c>[JsonIgnore]</c> + <c>required</c> - у эталона не документ, а
        /// ошибка настройки: <c>InvalidOperationException</c> на любом
        /// обращении к типу, включая запись (проверено пробой). Мы отказываем
        /// на компиляции - раньше и громче.
        /// </summary>
        [Fact]
        public void Required_member_marked_ignored_is_refused()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(@"        [JsonIgnore] public required int Id { get; set; }")
                );

            Assert.Contains("JGD022", run.DiagnosticIds);
            Assert.Contains(
                "configuration error",
                string.Join("\n", run.GeneratorDiagnostics.Select(d => d.GetMessage())),
                System.StringComparison.Ordinal
                );
        }

        /// <summary>
        /// <c>init</c>-член, который конструктору не аргумент, - отказ, и
        /// причина в нём не «трудно присвоить», а «нельзя <b>не</b>
        /// присвоить»: инициализатор объекта либо есть в тексте, либо нет.
        /// А эталон оставляет такому члену его собственное значение, если
        /// имени в документе не было.
        /// </summary>
        [Fact]
        public void Standalone_init_only_member_is_refused_with_the_reason_named()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(@"        public int Id { get; init; }")
                );

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "object initializer",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
        }

        /// <summary>
        /// Единственный параметризованный конструктор берётся сам, без
        /// атрибута, - ровно как у эталона.
        /// </summary>
        [Fact]
        public void Single_parameterized_constructor_is_taken_without_an_attribute()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload
    {
        public Payload(int id) { Id = id; }
        public int Id { get; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
            Assert.Contains("new global::Demo.Payload(arg_Id)", run.SingleGeneratedFile);
        }

        /// <summary>
        /// Конструктор без параметров побеждает даже при наличии публичного
        /// параметризованного - проверено прогоном эталона на паре, дающей
        /// разный результат. Видно это на тексте: отложенной формы нет.
        /// </summary>
        [Fact]
        public void Parameterless_constructor_wins_over_a_parameterized_one()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload
    {
        public Payload() { }
        public Payload(int id) { Id = id; }
        public int Id { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Contains("new global::Demo.Payload()", run.SingleGeneratedFile);
            Assert.DoesNotContain("arg_Id", run.SingleGeneratedFile);
        }

        /// <summary>
        /// Два параметризованных конструктора без <c>[JsonConstructor]</c> - у
        /// эталона <c>NotSupportedException</c> в рантайме. У нас то же
        /// решение, только на компиляции.
        /// </summary>
        [Fact]
        public void Two_parameterized_constructors_need_the_attribute()
        {
            var source = @"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    public class Payload
    {
        MARK public Payload(int id) { Id = id; Extra = 0; }
        public Payload(int id, int extra) { Id = id; Extra = extra; }
        public int Id { get; }
        public int Extra { get; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
";

            var ambiguous = GeneratorHarness.Run(source.Replace("MARK", string.Empty));
            Assert.Contains("JGD021", ambiguous.DiagnosticIds);
            Assert.Contains("NotSupportedException", ambiguous.GeneratorDiagnostics.Single().GetMessage());

            var marked = GeneratorHarness.Run(source.Replace("MARK", "[JsonConstructor]"));
            Assert.Empty(marked.GeneratorDiagnostics);
            Assert.Contains("new global::Demo.Payload(arg_Id)", marked.SingleGeneratedFile);
        }

        /// <summary>
        /// Параметр, которому не нашлось члена, у эталона -
        /// <c>InvalidOperationException</c> на <b>любом</b> документе, а не
        /// только на том, где этого имени нет. Отказ на компиляции - то же
        /// самое, сказанное вовремя.
        /// </summary>
        [Fact]
        public void Constructor_parameter_without_a_member_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload
    {
        public Payload(int id, int missing) { Id = id + missing; }
        public int Id { get; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("'missing'", run.GeneratorDiagnostics.Single().GetMessage());
        }

        /// <summary>
        /// Приватный конструктор с <c>[JsonConstructor]</c> эталон вызывает
        /// рефлексией. У нас порождённый код лежит в чужом классе и такого
        /// хода не имеет, поэтому отказ - и он прямо говорит, чем именно мы
        /// отличаемся.
        /// </summary>
        [Fact]
        public void Private_json_constructor_is_refused_because_generated_code_cannot_call_it()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    public class Payload
    {
        [JsonConstructor]
        private Payload(int id) { Id = id; }
        public int Id { get; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("reflection", run.GeneratorDiagnostics.Single().GetMessage());
        }

        /// <summary>
        /// <c>ref struct</c> - единственная форма структуры, которая остаётся
        /// отказом: её нельзя ни положить в поле, ни передать аргументом
        /// обобщённого типа, так что эталон её не сериализует тем более - у
        /// него <c>T</c> обобщённый.
        /// </summary>
        [Fact]
        public void Ref_struct_stays_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public ref struct Payload
    {
        public int Id { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("ref struct", run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage());
        }

        /// <summary>
        /// Корень-массив отказывает <b>вслух</b>, и это починка молчания, а не
        /// новое ограничение.
        ///
        /// <para>
        /// Раньше <c>[JsonSubject(typeof(T[]), true)]</c> роняли в самом начале
        /// - в <c>CollectRegistrations</c>, где регистрация сопоставлялась с
        /// <c>INamedTypeSymbol</c>, а массив это <c>IArrayTypeSymbol</c>.
        /// Сборка шла с нулём предупреждений, кода не порождалось, и не
        /// говорилось ни слова: узнать об этом можно было только по
        /// ненайденной перегрузке <c>Serialize</c>, а через мост - вообще
        /// никак.
        /// </para>
        ///
        /// <para>
        /// Упирается массив в читателя, а не в писателя: читатель
        /// коллекции-субъекта строит результат <c>new</c>'ом и наполняет через
        /// <c>ICollection&lt;T&gt;.Add</c>, а у массива нет ни того, ни
        /// другого. Поэтому сообщение обязано говорить, <b>что делать</b>, и
        /// тест это требует: в нём должно стоять слово про элемент.
        /// </para>
        /// </summary>
        [Fact]
        public void Array_subject_is_refused_out_loud()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload
    {
        public int Id { get; set; }
    }

    [JsonSubject(typeof(Payload[]), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);

            var message = run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage();

            Assert.Contains("arrays cannot be registered as subjects", message);
            Assert.Contains("element type", message);
        }

        /// <summary>
        /// Тот же массив <b>на месте члена</b> обслуживается и обязан
        /// продолжать обслуживаться: отказ касается только регистрации
        /// субъектом. Без этой пары предыдущий тест читался бы как «массивы не
        /// поддерживаются», что неправда.
        /// </summary>
        [Fact]
        public void Array_member_is_served_as_before()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Line
    {
        public int Id { get; set; }
    }

    public class Payload
    {
        public Line[] Lines { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    [JsonSubject(typeof(Line), false)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Contains("ArrayOf_Demo_Line", run.SingleGeneratedFile);
        }

        /// <summary>
        /// readonly-поле с <c>[JsonInclude]</c> эталон <b>пишет</b> и молча
        /// роняет на чтении - проверено прогоном. Раньше мы на нём отказывали,
        /// то есть отвергали то, что он обслуживает; теперь оно только на
        /// запись, как и get-only свойство.
        /// </summary>
        [Fact]
        public void Readonly_included_field_is_written_but_not_read()
        {
            var run = GeneratorHarness.Run(Sources.Host(@"
        [JsonInclude]
        public readonly int Frozen = 5;

        public int Normal { get; set; }
"));

            Assert.Empty(run.GeneratorDiagnostics);

            var text = run.SingleGeneratedFile;

            //имя приезжает внутрь u8-литерала, то есть с экранированными
            //кавычками: \"Frozen\"
            Assert.Contains("\\\"Frozen\\\"", text);

            //в диспетчере читателя имени быть не должно: присвоить его нечем
            Assert.DoesNotContain("result.Frozen", text);
        }

        /// <summary>
        /// Часть 1 фазы 6 (§9.10 плана): субъект, который сам является
        /// коллекцией, - теперь принят, а не отказ <c>JGD021</c> целиком, как
        /// было решено в §9.6. Обе формы: наследник <c>List&lt;T&gt;</c> без
        /// собственных членов и ручная <c>ICollection&lt;T&gt;</c> <b>с</b>
        /// собственным свойством - свойство эталон молча теряет (проверено
        /// пробой), и мы теряем его так же, а не отказываем: это не
        /// расхождение, а намеренное совпадение с тем, что теряет сам эталон.
        /// </summary>
        [Theory]
        [InlineData("public class Payload : global::System.Collections.Generic.List<string> { }")]
        [InlineData(@"
    public class Payload : global::System.Collections.Generic.ICollection<string>
    {
        public int Unrelated { get; set; }
        public int Count => 0;
        public bool IsReadOnly => false;
        public void Add(string item) { }
        public void Clear() { }
        public bool Contains(string item) => false;
        public void CopyTo(string[] array, int index) { }
        public bool Remove(string item) => false;
        public global::System.Collections.Generic.IEnumerator<string> GetEnumerator() => null!;
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => null!;
    }")]
        public void Subject_that_is_itself_a_collection_is_accepted_and_loses_its_own_members(string payload)
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
" + payload + @"

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.NotEmpty(run.GeneratedFiles);

            //"Unrelated"/собственных свойств в порождённом коде не бывает
            //вовсе - ни на запись, ни на чтение: субъект-коллекция читается и
            //пишется циклом по элементам, а не диспетчером имён
            Assert.DoesNotContain("Unrelated", run.SingleGeneratedFile);
            Assert.Contains("AppendRaw(\"[\"u8)", run.SingleGeneratedFile);
        }

        /// <summary>
        /// Словарь смотрится раньше списка (§9.10): <c>Dictionary&lt;,&gt;</c>
        /// реализует оба, а эталон пишет его объектом - проверено пробой.
        /// </summary>
        [Fact]
        public void Subject_that_is_a_dictionary_is_accepted_and_written_as_an_object()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload : global::System.Collections.Generic.Dictionary<string, int> { }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Contains("AppendRaw(\"{\"u8)", run.SingleGeneratedFile);
        }

        /// <summary>
        /// Ключ не-<c>string</c> - отказ, как и на месте члена (§9.3): у
        /// эталона свои правила преобразования для чисел, повторять их
        /// вслепую нельзя.
        /// </summary>
        [Fact]
        public void Subject_that_is_a_non_string_keyed_dictionary_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload : global::System.Collections.Generic.Dictionary<int, int> { }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("not string", run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage());
        }

        /// <summary>
        /// <c>IEnumerable&lt;T&gt;</c> без <c>ICollection&lt;T&gt;</c> - пишется
        /// (<c>GetEnumerator</c> хватает), но читать некуда: у эталона на
        /// любом документе <c>NotSupportedException</c>, потому что класть
        /// элемент негде. Отказ на компиляции - то же решение раньше, а не
        /// метод, обречённый бросать всегда.
        /// </summary>
        [Fact]
        public void Subject_that_implements_only_ienumerable_without_add_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Collections;
using System.Collections.Generic;

namespace Demo
{
    public class Payload : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator() => new List<string>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "no accessible Add method",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
        }

        /// <summary>
        /// Только негенерический <c>IEnumerable</c> - элемент был бы
        /// <c>object</c>, а <c>object</c> этот генератор не пишет.
        /// </summary>
        [Fact]
        public void Subject_that_implements_only_the_non_generic_ienumerable_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Collections;
using System.Collections.Generic;

namespace Demo
{
    public class Payload : IEnumerable
    {
        public IEnumerator GetEnumerator() => new List<int> { 1 }.GetEnumerator();
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "System.Object",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
        }

        /// <summary>
        /// <c>IsReadOnly</c>, зашитый в <c>true</c>, - найдено их же корпусом
        /// (<c>ReadOnlyStringICollectionWrapper</c> и три соседа, §9.10):
        /// эталон бросает <c>NotSupportedException</c> на любом документе,
        /// потому что класть элемент, даже когда <c>Add</c> есть, запрещает
        /// сам контракт коллекции.
        /// </summary>
        [Fact]
        public void Subject_with_hardcoded_read_only_true_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Collections;
using System.Collections.Generic;

namespace Demo
{
    public class Base<T> : ICollection<T>
    {
        private readonly List<T> _list = new List<T>();
        public int Count => _list.Count;
        public virtual bool IsReadOnly => false;
        public void Add(T item) => _list.Add(item);
        public void Clear() => _list.Clear();
        public bool Contains(T item) => false;
        public void CopyTo(T[] array, int index) { }
        public bool Remove(T item) => false;
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class Payload : Base<string>
    {
        public override bool IsReadOnly => true;
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "IsReadOnly",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
        }

        /// <summary>
        /// Комбинация исключена по построению: у субъекта-коллекции нет
        /// собственных членов, а эталон не печатает дискриминатор для
        /// типа, который пишется как голый массив или объект.
        /// </summary>
        [Fact]
        public void Subject_that_is_both_collection_shaped_and_polymorphic_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Demo
{
    [JsonDerivedType(typeof(Payload), ""p"")]
    public class Payload : List<string>
    {
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "JsonDerivedType",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
        }

        /// <summary>
        /// Производный тип обязан быть зарегистрирован: <c>[JsonDerivedType]</c>
        /// объявляет иерархию, но обслуживать типы мы беремся только по
        /// явному <c>[JsonSubject]</c> - регистрация есть решение автора, и
        /// подменять его догадкой нельзя.
        /// </summary>
        [Fact]
        public void Derived_type_must_be_registered()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal { public string? Name { get; set; } }

    public class Dog : Animal { public bool Barks { get; set; } }

    [JsonSubject(typeof(Animal), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("not registered", run.GeneratorDiagnostics.Single().GetMessage());
        }

        /// <summary>
        /// То же правило, что у <c>[JsonSourceGenerationOptions]</c>: свойство,
        /// которое мы не исполняем, - отказ, а не пропуск.
        /// </summary>
        [Fact]
        public void Unsupported_polymorphic_option_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal { public string? Name { get; set; } }

    public class Dog : Animal { public bool Barks { get; set; } }

    [JsonSubject(typeof(Animal), true)]
    [JsonSubject(typeof(Dog), false)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains("IgnoreUnrecognizedTypeDiscriminators", run.GeneratorDiagnostics.Single().GetMessage());
        }

        /// <summary>
        /// Развилка на записи идёт по <b>точному</b> типу, а не по <c>is</c>:
        /// иначе незарегистрированный потомок потомка уехал бы в документ как
        /// его база, потеряв члены и не сказав об этом. Видно это на тексте.
        /// </summary>
        [Fact]
        public void Polymorphic_writer_dispatches_on_the_exact_runtime_type()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal { public string? Name { get; set; } }

    public class Dog : Animal { public bool Barks { get; set; } }

    [JsonSubject(typeof(Animal), true)]
    [JsonSubject(typeof(Dog), false)]
    public partial class Serializer { }
}
");

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);

            var text = run.SingleGeneratedFile;

            Assert.Contains("runtimeType == typeof(global::Demo.Dog)", text);
            Assert.DoesNotContain("value is global::Demo.Dog", text);

            //дискриминатор склеен со скобкой в один литерал - он такая же
            //часть строения документа, как имя первого члена
            Assert.Contains("{\\\"$type\\\":\\\"dog\\\"", text);
        }

        /// <summary>
        /// Неполиморфный тип не платит за полиморфизм ни строкой: ни развилки
        /// по типу, ни проверки дискриминатора в его коде не появляется.
        /// </summary>
        [Fact]
        public void A_type_without_derived_types_pays_nothing()
        {
            var run = GeneratorHarness.Run(Sources.Host(@"        public int Id { get; set; }"));

            Assert.DoesNotContain("runtimeType", run.SingleGeneratedFile);
            Assert.DoesNotContain("discriminator", run.SingleGeneratedFile);
        }

        /// <summary>
        /// Требование к версии языка, а не к таргету: и u8-литералы, и
        /// <c>scoped</c> - это про компилятор. netstandard2.0 и net472
        /// поддержанными остаются.
        /// </summary>
        [Fact]
        public void Language_version_below_eleven_is_refused_with_a_named_reason()
        {
            var run = GeneratorHarness.Run(Sources.DistinctLengths, LanguageVersion.CSharp10);

            Assert.Contains("JGD026", run.DiagnosticIds);
            Assert.Empty(run.GeneratedFiles);
        }

        [Fact]
        public void Fields_follow_system_text_json_and_stay_out_unless_included()
        {
            var withoutInclude = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        public int Bare;
")
                );

            Assert.Empty(withoutInclude.GeneratorDiagnostics);
            Assert.DoesNotContain("value.Bare", withoutInclude.SingleGeneratedFile);

            var withInclude = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        [JsonInclude] public int Included;
")
                );

            Assert.Empty(withInclude.GeneratorDiagnostics);
            Assert.Contains("value.Included", withInclude.SingleGeneratedFile);
        }

        [Fact]
        public void Ignored_members_leave_no_trace()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        [JsonIgnore] public System.Collections.Generic.List<int>? Values { get; set; }
")
                );

            Assert.Empty(run.GeneratorDiagnostics);
            Assert.Empty(run.CompilationErrors);
            Assert.DoesNotContain("Values", run.SingleGeneratedFile);
        }

        [Fact]
        public void Registered_sink_that_is_not_sealed_is_reported_but_still_served()
        {
            var run = GeneratorHarness.Run(@"
using System;
using JsonGoddess;

namespace Demo
{
    public class LooseExhauster : ExhausterBase
    {
        public override void AppendRaw(ReadOnlySpan<byte> utf8) { }
        public override void AppendNull() { }
        public override void Append(bool value) { }
        public override void Append(sbyte value) { }
        public override void Append(byte value) { }
        public override void Append(short value) { }
        public override void Append(ushort value) { }
        public override void Append(int value) { }
        public override void Append(uint value) { }
        public override void Append(long value) { }
        public override void Append(ulong value) { }
        public override void Append(float value) { }
        public override void Append(double value) { }
        public override void Append(decimal value) { }
        public override void Append(char value) { }
        public override void Append(string? value) { }
        public override void Append(DateTime value) { }
        public override void Append(DateTimeOffset value) { }
        public override void Append(TimeSpan value) { }
        public override void Append(Guid value) { }
        public override void AppendBase64(byte[]? value) { }
    }

    public class Payload { public int Id { get; set; } }

    [JsonExhauster(typeof(LooseExhauster))]
    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD010", run.DiagnosticIds);
            Assert.Contains("Demo.LooseExhauster exhauster", run.SingleGeneratedFile);
        }

        [Fact]
        public void Type_registered_as_a_sink_must_derive_from_the_base_class()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public sealed class NotASink { }

    public class Payload { public int Id { get; set; } }

    [JsonExhauster(typeof(NotASink))]
    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD024", run.DiagnosticIds);
        }

        /// <summary>
        /// <c>MaxDepth</c> считает вложенность так же, как эталон - корень
        /// уже глубина 1 (проверено пробой), - поэтому предел меньше единицы
        /// не пропустил бы ни одного документа вовсе. Это сломанная
        /// настройка, а не «очень строгий страж», и отказ на компиляции
        /// говорит об этом раньше, чем документ.
        /// </summary>
        [Fact]
        public void Max_depth_below_one_is_refused()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Id { get; set; }",
                    "[JsonGuard(JsonGuard.MaxDepth, MaxDepth = 0)]"
                    )
                );

            Assert.Contains("JGD029", run.DiagnosticIds);
        }

        /// <summary>
        /// <c>JsonFeature.CaseInsensitiveNames</c> (вопрос O5, §15 плана):
        /// мы сворачиваем регистр по ASCII, эталон - по Unicode (пробоем
        /// подтверждено на кириллице), и на не-ASCII имени совпадение
        /// зависело бы от алфавита - это молчаливое расхождение, запрещённое
        /// принципом 1, поэтому отказ, а не приблизительная поддержка.
        /// </summary>
        [Fact]
        public void Case_insensitive_names_refuses_a_non_ascii_property_name()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        [JsonPropertyName(\"Имя\")]\n        public string? Name { get; set; }",
                    "[JsonFeature(JsonFeature.CaseInsensitiveNames)]"
                    )
                );

            Assert.Contains("JGD030", run.DiagnosticIds);
        }

        /// <summary>
        /// Второй отказ той же фичи: два имени, совпадающие после ASCII-свёртки,
        /// - диспетчер не может завести две ветки на один и тот же случай, и
        /// эталон в этой ситуации тоже отказывает (проверено пробой), хоть и в
        /// рантайме при первом обращении, а не на компиляции.
        /// </summary>
        [Fact]
        public void Case_insensitive_names_refuses_two_members_colliding_after_ascii_fold()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Alpha { get; set; }\n        public int alpha { get; set; }",
                    "[JsonFeature(JsonFeature.CaseInsensitiveNames)]"
                    )
                );

            Assert.Contains("JGD030", run.DiagnosticIds);
        }

        /// <summary>
        /// ASCII-имя без коллизий фича обслуживает как обычно - отказы выше
        /// про конкретные формы, которые честно не совпали бы с эталоном, а
        /// не про фичу целиком.
        /// </summary>
        [Fact]
        public void Case_insensitive_names_accepts_an_ordinary_ascii_only_subject()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(
                    "        public int Id { get; set; }\n        public string? Name { get; set; }",
                    "[JsonFeature(JsonFeature.CaseInsensitiveNames)]"
                    )
                );

            Assert.Empty(run.CompilationErrors);
            Assert.DoesNotContain("JGD030", run.DiagnosticIds);
        }
    }
}
