using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Утверждения о <b>тексте</b> порождаемого кода для <c>JsonFeature</c>
    /// (§6.2 плана) - зеркало <c>JsonGuardTextFixture</c> по устройству и по
    /// центральному требованию: выключенная фича не должна оставить и следа
    /// в тексте, а не только вести себя так же на счастливом пути (медленная
    /// безусловная проверка тоже прошла бы поведенческий тест - разницу
    /// видно только здесь).
    /// </summary>
    public class JsonFeatureTextFixture
    {
        private const string Members =
            "        public int Id { get; set; }\n"
            + "        public string? Name { get; set; }\n"
            + "        public float F { get; set; }\n"
            + "        public double D { get; set; }\n";

        /// <summary>
        /// Маркеры, которые появляются в тексте только под соответствующей
        /// фичей - подтверждено грепом по <c>ClassSourceProducer</c>/
        /// <c>ValueSourceProducer</c>/<c>NameDispatcher</c>: ни один из них
        /// не печатается больше нигде.
        /// </summary>
        private static readonly string[] AllFeatureMarkers =
        {
            "SkipWhitespaceAndComments",
            ".EndObject)",
            ".EndArray)",
            "JsonNamedFloat",
            "out parsed)",
            "EqualsIgnoreCase",
        };

        [Fact]
        public void Host_without_json_feature_attribute_emits_no_feature_code_at_all()
        {
            var text = GeneratorHarness.Run(Sources.Host(Members)).SingleGeneratedFile;

            foreach (var marker in AllFeatureMarkers)
            {
                Assert.DoesNotContain(marker, text);
            }

            //счастливый путь остаётся дословно тем же, что и до JsonFeature -
            //число по-прежнему читается однострочным присваиванием
            Assert.Contains("out int parsed);", text);
        }

        [Fact]
        public void Composite_host_without_json_feature_attribute_emits_no_feature_code_either()
        {
            var text = GeneratorHarness.Run(Sources.Composite).SingleGeneratedFile;

            foreach (var marker in AllFeatureMarkers)
            {
                Assert.DoesNotContain(marker, text);
            }
        }

        [Fact]
        public void Comments_feature_switches_scanning_to_the_comment_aware_form()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.Comments)]")
                ).SingleGeneratedFile;

            Assert.Contains("SkipWhitespaceAndComments(json, ref position);", text);

            //единственное исключение из §6.2 - между именем свойства и
            //двоеточием комментарий не пропускается даже под фичей
            Assert.Contains(
                "SkipWhitespaceAndComments(json, ref position);\n            var name = __Scan.ReadStringContent",
                text
                );
        }

        [Fact]
        public void Trailing_commas_feature_adds_a_peek_for_the_closing_token_after_a_comma()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.TrailingCommas)]")
                ).SingleGeneratedFile;

            Assert.Contains(".EndObject)", text);

            //без фичи по-прежнему нет ни одной проверки на висячую запятую
            var withoutFeature = GeneratorHarness.Run(Sources.Host(Members)).SingleGeneratedFile;
            Assert.DoesNotContain(".EndObject)", withoutFeature);
        }

        [Fact]
        public void Trailing_commas_feature_reaches_collection_readers_too()
        {
            var source = Sources.Composite.Replace(
                "[JsonSubject(typeof(Basket), true)]",
                "[JsonFeature(JsonFeature.TrailingCommas)]\n    [JsonSubject(typeof(Basket), true)]"
                );
            var text = GeneratorHarness.Run(source).SingleGeneratedFile;

            //Basket.Lines - List<Line>, Numbers - int[]: оба читаются
            //ReadCollection_, и оба обязаны получить проверку на массив
            Assert.Contains(".EndArray)", text);
        }

        [Fact]
        public void Named_floating_point_literals_feature_touches_only_single_and_double()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.NamedFloatingPointLiterals)]")
                ).SingleGeneratedFile;

            Assert.Contains("JsonNamedFloat.TryGetLiteral", text);
            Assert.Contains("JsonNamedFloat.TryParse", text);

            //Id - int, лексика NamedFloatingPointLiterals его не касается:
            //писатель Id остаётся однострочным Append
            Assert.Contains("exhauster.Append(value.Id);", text);
        }

        [Fact]
        public void Numbers_from_strings_feature_splits_the_number_scalar_reader()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.NumbersFromStrings)]")
                ).SingleGeneratedFile;

            Assert.Contains("out parsed)", text);

            //фича не подключает проверку именованных литералов - это отдельный флаг
            Assert.DoesNotContain("JsonNamedFloat", text);
        }

        [Fact]
        public void Case_insensitive_names_feature_switches_the_dispatcher_to_a_case_insensitive_chain()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.CaseInsensitiveNames)]")
                ).SingleGeneratedFile;

            Assert.Contains("JsonAsciiName.EqualsIgnoreCase(name,", text);

            //без фичи диспетчер сравнивает сырые байты, а не сворачивает регистр
            var withoutFeature = GeneratorHarness.Run(Sources.Host(Members)).SingleGeneratedFile;
            Assert.DoesNotContain("EqualsIgnoreCase", withoutFeature);
        }

        [Fact]
        public void System_text_json_compatible_composite_turns_on_every_marker_at_once()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonFeature(JsonFeature.SystemTextJsonCompatible)]")
                ).SingleGeneratedFile;

            Assert.Contains("SkipWhitespaceAndComments", text);
            Assert.Contains(".EndObject)", text);
            Assert.Contains("JsonNamedFloat", text);
            Assert.Contains("out parsed)", text);
            Assert.Contains("EqualsIgnoreCase", text);
        }
    }
}
