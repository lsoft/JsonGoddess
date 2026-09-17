using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Утверждения о <b>тексте</b> порождаемого кода для <c>JsonGuard</c>
    /// (§6.3 плана).
    ///
    /// Центральное требование всей opt-in конструкции - "выключенный страж
    /// стоит ноль, не одну ветку" - держится именно здесь, а не обещанием в
    /// комментарии: хост без <c>[JsonGuard]</c> не должен упоминать в
    /// сгенерированном тексте ни один из маркеров, которые печатает
    /// включённый страж. Поведенческие тесты (round-trip) на это не годятся:
    /// они бы прошли и на медленной безусловной проверке, и на быстрой
    /// условной - разницу видно только в тексте.
    /// </summary>
    public class JsonGuardTextFixture
    {
        private const string Members =
            "        public int Id { get; set; }\n        public string? Name { get; set; }";

        /// <summary>
        /// Маркеры, которые появляются в тексте только под соответствующим
        /// стражем. Полный отрицательный список - самое сильное утверждение о
        /// "цене ноль": не одно точечное сравнение, а гарантия, что ни один
        /// из семи стражей не оставил и следа.
        /// </summary>
        private static readonly string[] AllGuardMarkers =
        {
            "dup_",
            "Duplicate property",
            "Unexpected trailing content",
            "ReadStringContentStrict",
            "ReadNumberRawStrict",
            "EnsureValidUtf8",
            "context.Depth",
            "SkipValueGuarded",
            "Unknown property",
            "UnescapeName(name, true)",
            "UnescapeValue(raw, true)",
        };

        [Fact]
        public void Host_without_json_guard_attribute_emits_no_guard_code_at_all()
        {
            var text = GeneratorHarness.Run(Sources.Host(Members)).SingleGeneratedFile;

            foreach (var marker in AllGuardMarkers)
            {
                Assert.DoesNotContain(marker, text);
            }

            //счастливый путь остаётся дословно тем же, что и до JsonGuard
            Assert.Contains(".ReadStringContent(json, ref position, out var nameEscaped);", text);
            Assert.Contains(".ReadNumberRaw(json, ref position);", text);
            Assert.Contains(".SkipValue(json, ref position);", text);
        }

        /// <summary>
        /// То же самое, но на форме с полиморфизмом, коллекциями и словарями
        /// сразу (<see cref="Sources.Composite"/>) - маркеры не должны
        /// просочиться ни из одного из путей эмиттера, которые эта работа
        /// трогала (объект, коллекция, субъект-коллекция, discriminator).
        /// </summary>
        [Fact]
        public void Composite_host_without_json_guard_attribute_emits_no_guard_code_either()
        {
            var text = GeneratorHarness.Run(Sources.Composite).SingleGeneratedFile;

            foreach (var marker in AllGuardMarkers)
            {
                Assert.DoesNotContain(marker, text);
            }
        }

        [Fact]
        public void Duplicate_properties_guard_adds_a_seen_flag_per_member()
        {
            var run = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.DuplicateProperties)]")
                );

            Assert.Empty(run.CompilationErrors);
            var text = run.SingleGeneratedFile;

            Assert.Contains("var dup_Id = false;", text);
            Assert.Contains("var dup_Name = false;", text);
            Assert.Contains("if (dup_Id)", text);
            Assert.Contains("Duplicate property 'Id'.", text);

            //страж не трогает ничего, кроме дубликатов: числа и строки
            //по-прежнему читаются лёгкой лексикой
            Assert.Contains(".ReadNumberRaw(json, ref position);", text);
            Assert.DoesNotContain("ReadNumberRawStrict", text);
        }

        [Fact]
        public void Trailing_content_guard_checks_the_tail_only_in_the_entry_point()
        {
            var withGuard = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.TrailingContent)]")
                ).SingleGeneratedFile;

            Assert.Contains("public static void Deserialize(", withGuard);
            Assert.Contains("Unexpected trailing content after the top-level value.", withGuard);

            //ветка стоит один раз - в точке входа, а не в каждом Read_
            Assert.Equal(1, Occurrences(withGuard, "Unexpected trailing content"));
        }

        [Fact]
        public void Control_chars_guard_switches_every_string_scan_to_the_strict_reader()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.ControlCharsInStrings)]")
                ).SingleGeneratedFile;

            Assert.Contains(".ReadStringContentStrict(json, ref position, out var nameEscaped);", text);
            Assert.Contains(".ReadStringContentStrict(json, ref position, out var rawEscaped);", text);
            Assert.DoesNotContain(".ReadStringContent(json, ref position, out var nameEscaped);", text);
        }

        [Fact]
        public void Strict_numbers_guard_switches_the_number_lexer()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.StrictNumbers)]")
                ).SingleGeneratedFile;

            Assert.Contains(".ReadNumberRawStrict(json, ref position);", text);
            Assert.DoesNotContain(".ReadNumberRaw(json, ref position);", text);
        }

        [Fact]
        public void Invalid_utf8_guard_validates_string_scalars_before_the_injector_sees_them()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.InvalidUtf8)]")
                ).SingleGeneratedFile;

            Assert.Contains("JsonStringDecoder.EnsureValidUtf8(raw, rawEscaped);", text);

            //числовой скаляр (Id) не materializуется строкой - для него
            //проверки быть не должно, только для string (Name)
            var scalarSection = Section(text, "ReadScalar_Int32", "private static ");
            Assert.DoesNotContain("EnsureValidUtf8", scalarSection);
        }

        [Fact]
        public void Invalid_utf8_guard_makes_the_escaped_name_path_strict_too()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.InvalidUtf8)]")
                ).SingleGeneratedFile;

            Assert.Contains("context.UnescapeName(name, true);", text);
        }

        [Fact]
        public void Max_depth_guard_wraps_object_and_collection_readers_in_a_counted_try_finally()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.MaxDepth, MaxDepth = 5)]")
                ).SingleGeneratedFile;

            Assert.Contains("context.Depth++;", text);
            Assert.Contains("if (context.Depth > 5)", text);
            Assert.Contains("context.Depth--;", text);
            Assert.Contains("The maximum configured depth of 5 has been exceeded.", text);

            //без UnknownProperties неизвестное свойство по-прежнему
            //пропускается, но с тем же счётчиком глубины
            Assert.Contains("SkipValueGuarded(json, ref position, ref context.Depth, 5);", text);
        }

        [Fact]
        public void Max_depth_defaults_to_sixty_four_like_the_reference()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.MaxDepth)]")
                ).SingleGeneratedFile;

            Assert.Contains("if (context.Depth > 64)", text);
        }

        [Fact]
        public void Unknown_properties_guard_throws_instead_of_skipping()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.UnknownProperties)]")
                ).SingleGeneratedFile;

            Assert.Contains("Unknown property '\"", text);
            Assert.DoesNotContain(".SkipValue(json, ref position);", text);
            Assert.DoesNotContain("SkipValueGuarded", text);
        }

        /// <summary>
        /// <c>UnknownProperties</c> побеждает <c>MaxDepth</c> на пути
        /// неизвестного свойства: если оно всё равно отказ, пропускать его
        /// значение (пусть даже с подсчётом глубины) незачем.
        /// </summary>
        [Fact]
        public void Unknown_properties_guard_wins_over_max_depth_on_the_fallback_branch()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(
                    Members,
                    "[JsonGuard(JsonGuard.UnknownProperties | JsonGuard.MaxDepth)]"
                    )
                ).SingleGeneratedFile;

            Assert.Contains("Unknown property '\"", text);
            Assert.DoesNotContain("SkipValueGuarded", text);

            //при этом счётчик глубины у ИЗВЕСТНЫХ типов всё равно на месте
            Assert.Contains("context.Depth++;", text);
        }

        [Fact]
        public void System_text_json_compatible_composite_turns_on_every_marker_at_once()
        {
            var text = GeneratorHarness.Run(
                Sources.Host(Members, "[JsonGuard(JsonGuard.SystemTextJsonCompatible)]")
                ).SingleGeneratedFile;

            Assert.Contains("dup_Id", text);
            Assert.Contains("Unexpected trailing content", text);
            Assert.Contains("ReadStringContentStrict", text);
            Assert.Contains("ReadNumberRawStrict", text);
            Assert.Contains("EnsureValidUtf8", text);
            Assert.Contains("context.Depth", text);
            Assert.Contains("Unknown property", text);
        }

        private static string Section(string text, string from, string until)
        {
            var start = text.IndexOf(from, System.StringComparison.Ordinal);
            Assert.True(start >= 0, "marker not found: " + from);

            var end = text.IndexOf(until, start + from.Length, System.StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static int Occurrences(string text, string needle)
        {
            var count = 0;
            for (var i = text.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
                i = text.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
