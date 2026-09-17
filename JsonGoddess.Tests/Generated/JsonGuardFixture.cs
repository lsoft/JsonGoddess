using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Ladder-фикстуры <c>JsonGuard</c> (§6.3 плана): тот же документ идёт
    /// хосту без стража (обязан <b>принять</b>, ровно как сейчас) и хосту
    /// ровно с одним включённым стражем (обязан <b>отказать</b>).
    ///
    /// Ожидание, что документ нарушает именно то, что заявлено, берётся не из
    /// моего описания в таблице §6.3, а прогоном самого
    /// <c>System.Text.Json</c> внутри теста - там, где у него вообще есть
    /// настройка, управляющая этим поведением. Там, где настройки нет (эталон
    /// отказывает безусловно - <c>ControlCharsInStrings</c>,
    /// <c>StrictNumbers</c>, <c>TrailingContent</c>, <c>InvalidUtf8</c>), тест
    /// всё равно прогоняет эталон с опциями по умолчанию и проверяет, что он
    /// отказывает тоже - это и есть его единственное поведение.
    /// </summary>
    public class JsonGuardFixture
    {
        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        // ---------- DuplicateProperties ----------

        [Fact]
        public void Duplicate_properties_are_accepted_by_default_and_the_last_value_wins()
        {
            var json = Utf8(@"{""Id"":1,""Id"":2,""Name"":""a""}");

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(2, ours!.Id);

            //пробой: System.Text.Json по умолчанию тоже разрешает дубликаты и
            //тоже побеждает последним значением - наш default и без стража
            //уже совпадает с default эталона
            var theirs = JsonSerializer.Deserialize<GuardSubject>(json);
            Assert.Equal(2, theirs!.Id);
        }

        [Fact]
        public void Duplicate_properties_guard_rejects_a_repeated_known_member()
        {
            var json = Utf8(@"{""Id"":1,""Id"":2,""Name"":""a""}");

            Assert.Throws<JsonDocumentException>(
                () => DuplicatePropertiesGuardSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

#if NET10_0_OR_GREATER
            //пробой: ровно тот же отказ у эталона при AllowDuplicateProperties=false.
            //Свойство появилось в System.Text.Json 10, и на net472/net8.0, где
            //эталон закреплён на 9.0.0 (§9.3 плана), его попросту нет - там
            //страж проверяется только против нашего собственного поведения.
            var options = new JsonSerializerOptions { AllowDuplicateProperties = false, };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(json, options));
#endif
        }

        [Fact]
        public void Duplicate_properties_guard_ignores_a_repeated_unknown_name()
        {
            //пробой подтвердил: AllowDuplicateProperties=false ловит только
            //дубликат СОПОСТАВЛЕННОГО члена - повтор незнакомого имени эталон
            //пропускает не глядя, и страж обязан вести себя так же
            var json = Utf8(@"{""Extra"":1,""Extra"":2,""Id"":9,""Name"":""a""}");

            DuplicatePropertiesGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(9, ours!.Id);

#if NET10_0_OR_GREATER
            var options = new JsonSerializerOptions { AllowDuplicateProperties = false, };
            var theirs = JsonSerializer.Deserialize<GuardSubject>(json, options);
            Assert.Equal(9, theirs!.Id);
#endif
        }

        // ---------- TrailingContent ----------

        [Fact]
        public void Trailing_content_is_ignored_by_default()
        {
            var json = Utf8(@"{""Id"":1,""Name"":""a""}garbage");

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
        }

        [Fact]
        public void Trailing_content_guard_rejects_garbage_after_the_top_level_value()
        {
            var json = @"{""Id"":1,""Name"":""a""}garbage";

            Assert.Throws<JsonDocumentException>(
                () => TrailingContentGuardSerializer.Deserialize(DefaultInjector.Instance, Utf8(json), out _)
                );

            //пробой: у эталона это встроенное поведение string-перегрузки без
            //какой-либо опции - способа получить от него прежнее (снисходительное)
            //поведение нет вовсе
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(json));
        }

        [Fact]
        public void Trailing_content_guard_allows_trailing_whitespace()
        {
            var json = Utf8("{\"Id\":1,\"Name\":\"a\"}   \n\t ");

            TrailingContentGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
        }

        // ---------- ControlCharsInStrings ----------

        [Fact]
        public void Unescaped_control_characters_are_accepted_by_default()
        {
            var text = "{\"Id\":1,\"Name\":\"a" + (char)1 + "b\"}";
            var json = Utf8(text);

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal("a" + (char)1 + "b", ours!.Name);
        }

        [Fact]
        public void Control_chars_guard_rejects_an_unescaped_control_character_in_a_string()
        {
            var text = "{\"Id\":1,\"Name\":\"a" + (char)1 + "b\"}";
            var json = Utf8(text);

            Assert.Throws<JsonDocumentException>(
                () => ControlCharsGuardSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            //пробой: у эталона это безусловное поведение - в JsonReaderOptions
            //нет свойства, которое бы это разрешало
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(text));
        }

        // ---------- StrictNumbers ----------

        [Theory]
        [InlineData(@"{""Id"":01,""Name"":""a""}")] //ведущий ноль
        [InlineData(@"{""Id"":+1,""Name"":""a""}")] //ведущий плюс
        public void Strict_numbers_guard_rejects_numbers_outside_the_rfc_grammar(string text)
        {
            Assert.Throws<JsonDocumentException>(
                () => StrictNumbersGuardSerializer.Deserialize(DefaultInjector.Instance, Utf8(text), out _)
                );

            //пробой: у эталона это тоже безусловный отказ - Utf8Parser, на
            //который опирается наш разбор по умолчанию, эту грамматику не
            //проверяет вовсе, а System.Text.Json проверяет всегда
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(text));
        }

        [Fact]
        public void Leading_zero_is_accepted_by_default()
        {
            var json = Utf8(@"{""Id"":01,""Name"":""a""}");

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
        }

        // ---------- InvalidUtf8 ----------

        [Fact]
        public void Invalid_utf8_in_a_string_value_is_replaced_with_u_fffd_by_default()
        {
            var json = Prefix("{\"Id\":1,\"Name\":\"a").Concat(0x80).Concat("b\"}").ToArray();

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Contains((char)0xFFFD, ours!.Name!);
        }

        [Fact]
        public void Invalid_utf8_guard_rejects_a_malformed_byte_sequence_in_a_string_value()
        {
            var json = Prefix("{\"Id\":1,\"Name\":\"a").Concat(0x80).Concat("b\"}").ToArray();

            Assert.Throws<JsonDocumentException>(
                () => InvalidUtf8GuardSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            //пробой: эталон при материализации строки отказывает всегда -
            //способа получить от него замену на U+FFFD нет
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(json));
        }

        [Fact]
        public void Invalid_utf8_guard_rejects_an_unpaired_surrogate_escape()
        {
            const string text = @"{""Id"":1,""Name"":""\uD800""}";

            Assert.Throws<JsonDocumentException>(
                () => InvalidUtf8GuardSerializer.Deserialize(DefaultInjector.Instance, Utf8(text), out _)
                );

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(text));
        }

        [Fact]
        public void Unpaired_surrogate_escape_is_accepted_by_default()
        {
            const string text = @"{""Id"":1,""Name"":""\uD800""}";

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, Utf8(text), out var ours);
            Assert.Equal(1, ours!.Name!.Length);
        }

        // ---------- MaxDepth ----------

        [Fact]
        public void Max_depth_guard_accepts_nesting_at_the_configured_limit()
        {
            var json = Utf8(@"{""Value"":1,""Child"":{""Value"":2,""Child"":{""Value"":3,""Child"":null}}}");

            MaxDepthNodeSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(3, ours!.Child!.Child!.Value);

            //пробой: то же самое ограничение на System.Text.Json - Node
            //обслуживается им напрямую рефлексией, никакой генерации не нужно
            var theirs = JsonSerializer.Deserialize<GuardChainNode>(json, new JsonSerializerOptions { MaxDepth = 3, });
            Assert.Equal(3, theirs!.Child!.Child!.Value);
        }

        [Fact]
        public void Max_depth_guard_rejects_nesting_deeper_than_the_configured_limit()
        {
            var json = Utf8(
                @"{""Value"":1,""Child"":{""Value"":2,""Child"":{""Value"":3,""Child"":{""Value"":4,""Child"":null}}}}"
                );

            Assert.Throws<JsonDocumentException>(
                () => MaxDepthNodeSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<GuardChainNode>(json, new JsonSerializerOptions { MaxDepth = 3, })
                );
        }

        [Fact]
        public void Deep_nesting_is_accepted_without_the_guard()
        {
            //без стража глубина не считается вовсе - ограничивает её только
            //стек вызовов, а не счётчик; десяти уровней достаточно, чтобы
            //отличить это поведение от MaxDepth=3
            var json = Utf8(
                @"{""Value"":1,""Child"":{""Value"":2,""Child"":{""Value"":3,""Child"":{""Value"":4,"
                + @"""Child"":{""Value"":5,""Child"":null}}}}}"
                );

            PlainNodeSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(5, ours!.Child!.Child!.Child!.Child!.Value);
        }

        // ---------- UnknownProperties ----------

        [Fact]
        public void Unknown_properties_are_skipped_by_default()
        {
            var json = Utf8(@"{""Id"":1,""Name"":""a"",""Extra"":42}");

            PlainGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
        }

        [Fact]
        public void Unknown_properties_guard_rejects_a_name_that_maps_to_no_member()
        {
            var json = Utf8(@"{""Id"":1,""Name"":""a"",""Extra"":42}");

            Assert.Throws<JsonDocumentException>(
                () => UnknownPropertiesGuardSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            //пробой: аналог JsonUnmappedMemberHandling.Disallow
            var options = new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GuardSubject>(json, options));
        }

        [Fact]
        public void Unknown_properties_guard_accepts_a_document_with_no_unknown_names()
        {
            var json = Utf8(@"{""Id"":1,""Name"":""a""}");

            UnknownPropertiesGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
        }

        // ---------- SystemTextJsonCompatible ----------

        [Fact]
        public void Compatible_composite_rejects_a_document_that_violates_any_single_guard()
        {
            var json = Utf8(@"{""Id"":1,""Id"":2,""Name"":""a""}");

            Assert.Throws<JsonDocumentException>(
                () => CompatibleGuardSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );
        }

        [Fact]
        public void Compatible_composite_accepts_a_clean_document()
        {
            var json = Utf8(@"{""Id"":1,""Name"":""a""}");

            CompatibleGuardSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
            Assert.Equal("a", ours.Name);
        }

        /// <summary>
        /// Маленький помощник для сборки документа с битым UTF-8-байтом
        /// внутри: C#-строка не может нести невалидный байт сама по себе, а
        /// JSON-документу он нужен буквально.
        /// </summary>
        private static ByteBuilder Prefix(string ascii) => new ByteBuilder().Concat(ascii);

        private sealed class ByteBuilder
        {
            private byte[] _bytes = System.Array.Empty<byte>();

            public ByteBuilder Concat(string ascii) => Concat(Encoding.ASCII.GetBytes(ascii));

            public ByteBuilder Concat(byte single) => Concat(new[] { single, });

            private ByteBuilder Concat(byte[] more)
            {
                var next = new byte[_bytes.Length + more.Length];
                _bytes.CopyTo(next, 0);
                more.CopyTo(next, _bytes.Length);
                _bytes = next;
                return this;
            }

            public byte[] ToArray() => _bytes;
        }
    }
}
