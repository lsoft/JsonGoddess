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

        [Fact]
        public void Required_members_are_refused_until_presence_is_tracked()
        {
            var required = GeneratorHarness.Run(
                Sources.Host(@"        public required int Id { get; set; }")
                );

            Assert.Contains("JGD022", required.DiagnosticIds);
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
        /// Найдено переносом набора System.Text.Json (§11.1) - их
        /// <c>StringListWrapper : List&lt;string&gt; { }</c> мы принимали и
        /// писали <c>{}</c> вместо <c>["Hello","World"]</c>.
        ///
        /// Проверяются обе формы, потому что они разные по причине: у
        /// наследника коллекции своих членов нет вовсе, а у класса с
        /// <c>ICollection&lt;T&gt;</c> они есть - и эталон их молча
        /// выбрасывает, потому что смотрит на интерфейс, а не на свойства.
        /// Принять вторую значило бы выдать документ, в котором есть то, чего
        /// у эталона нет.
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
        public void Subject_that_is_itself_a_collection_is_refused(string payload)
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

            Assert.Contains("JGD021", run.DiagnosticIds);
            Assert.Contains(
                "IEnumerable",
                run.GeneratorDiagnostics.Single(d => d.Id == "JGD021").GetMessage()
                );
            Assert.Empty(run.GeneratedFiles);
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
    }
}
