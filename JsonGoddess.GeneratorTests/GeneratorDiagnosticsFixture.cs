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
            //словари приезжают позже; пропустить член нельзя - документ без
            //него это другой документ
            var run = GeneratorHarness.Run(
                Sources.Host(@"
        public int Id { get; set; }
        public System.Collections.Generic.Dictionary<string, int>? Values { get; set; }
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

        [Fact]
        public void Enum_member_is_refused_until_enums_land()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public enum Status { Draft, Sent }

    public class Payload
    {
        public int Id { get; set; }
        public Status State { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD022", run.DiagnosticIds);
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

    public class Bad { public System.Collections.Generic.Dictionary<string, int>? Values { get; set; } }

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
        public void Required_and_init_only_members_are_refused_until_phase_five()
        {
            var required = GeneratorHarness.Run(
                Sources.Host(@"        public required int Id { get; set; }")
                );
            Assert.Contains("JGD022", required.DiagnosticIds);

            var initOnly = GeneratorHarness.Run(
                Sources.Host(@"        public int Id { get; init; }")
                );
            Assert.Contains("JGD022", initOnly.DiagnosticIds);
        }

        [Fact]
        public void Subject_without_a_parameterless_constructor_is_refused()
        {
            var run = GeneratorHarness.Run(@"
using JsonGoddess;

namespace Demo
{
    public class Payload
    {
        public Payload(int id) { Id = id; }
        public int Id { get; set; }
    }

    [JsonSubject(typeof(Payload), true)]
    public partial class Serializer { }
}
");

            Assert.Contains("JGD021", run.DiagnosticIds);
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
