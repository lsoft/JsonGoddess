using System.Text;
using System.Text.Json;
using Xunit;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Ladder-фикстуры <c>JsonFeature</c> (§6.2 плана): тот же документ идёт
    /// хосту без фичи (обязан отвергать так же, как сейчас) и хосту ровно с
    /// одной включённой фичей (обязан <b>принять</b> то же, что принимает
    /// эталон с соответствующей опцией, - и не больше). Устройство то же
    /// самое, что у <c>JsonGuardFixture</c>: ожидание берётся прогоном
    /// <c>System.Text.Json</c> внутри теста, а не литералом.
    /// </summary>
    public class JsonFeatureFixture
    {
        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        // ---------- Comments ----------

        [Fact]
        public void Comments_are_rejected_by_default()
        {
            var json = Utf8("// hi\n{\"Id\":1}");

            Assert.Throws<JsonDocumentException>(
                () => PlainFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            //пробой: у эталона это тоже отказ без ReadCommentHandling.Skip
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json));
        }

        [Theory]
        [InlineData("// leading line comment\n{\"Id\":1,\"Name\":\"a\"}")]
        [InlineData("/* leading block comment */{\"Id\":1,\"Name\":\"a\"}")]
        [InlineData("{\"Id\": /* between colon and value */ 1,\"Name\":\"a\"}")]
        [InlineData("{\"Id\": 1 /* between value and comma */ , \"Name\":\"a\"}")]
        [InlineData("{\"Id\": 1, /* between comma and next name */ \"Name\":\"a\"}")]
        [InlineData("{ /* right after opening brace */ \"Id\": 1,\"Name\":\"a\"}")]
        [InlineData("{\"Id\": 1,\"Name\":\"a\" /* before closing brace */ }")]
        [InlineData("{\"Id\":1,\"Name\":\"a\"}// trailing, no newline")]
        public void Comments_feature_accepts_every_position_the_reference_accepts(string text)
        {
            var json = Utf8(text);

            CommentsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);
            Assert.Equal("a", ours.Name);

            var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);
            Assert.Equal(1, theirs!.Id);
            Assert.Equal("a", theirs.Name);
        }

        /// <summary>
        /// Единственное исключение, найденное пробоем: комментарий между
        /// именем свойства и двоеточием эталон отвергает даже с
        /// <c>ReadCommentHandling.Skip</c> - и мы обязаны отвергать его тоже,
        /// а не быть снисходительнее эталона.
        /// </summary>
        [Fact]
        public void Comments_feature_still_rejects_a_comment_between_name_and_colon()
        {
            var json = Utf8("{\"Id\" /* c */ : 1}");

            Assert.Throws<JsonDocumentException>(
                () => CommentsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json, options));
        }

        [Fact]
        public void Comments_feature_accepts_comments_inside_arrays_and_objects_of_a_collection_member()
        {
            var json = Utf8("{\"Numbers\":[1, /* c */ 2, 3 /* c */ ],\"Map\":{ /* c */ \"a\":1}}");

            CommentsCollectionFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(new[] { 1, 2, 3, }, ours!.Numbers);
            Assert.Equal(1, ours.Map!["a"]);

            var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, };
            var theirs = JsonSerializer.Deserialize<FeatureCollectionSubject>(json, options);
            Assert.Equal(ours.Numbers, theirs!.Numbers);
            Assert.Equal(ours.Map, theirs.Map);
        }

        // ---------- TrailingCommas ----------

        [Fact]
        public void Trailing_comma_is_rejected_by_default()
        {
            var json = Utf8("{\"Id\":1,}");

            Assert.Throws<JsonDocumentException>(
                () => PlainFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json));
        }

        [Fact]
        public void Trailing_commas_feature_accepts_one_trailing_comma_in_an_object()
        {
            var json = Utf8("{\"Id\":1,\"Name\":\"a\",}");

            TrailingCommasFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);

            var options = new JsonSerializerOptions { AllowTrailingCommas = true, };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);
            Assert.Equal(1, theirs!.Id);
        }

        [Theory]
        [InlineData("{\"Numbers\":[1,2,]}")]
        [InlineData("{\"Map\":{\"a\":1,}}")]
        public void Trailing_commas_feature_accepts_one_trailing_comma_in_a_collection(string text)
        {
            var json = Utf8(text);

            TrailingCommasCollectionFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);

            var options = new JsonSerializerOptions { AllowTrailingCommas = true, };
            var theirs = JsonSerializer.Deserialize<FeatureCollectionSubject>(json, options);
            Assert.Equal(theirs!.Numbers, ours!.Numbers);
            Assert.Equal(theirs.Map, ours.Map);
        }

        /// <summary>
        /// Пробоем подтверждено: фича разрешает ровно одну запятую перед
        /// закрывающей скобкой, а не запятую вместо элемента - <c>[,]</c>,
        /// <c>[1,,2]</c>, <c>[,1]</c> эталон отвергает даже с
        /// <c>AllowTrailingCommas = true</c>.
        /// </summary>
        [Theory]
        [InlineData("{\"Numbers\":[,]}")]
        [InlineData("{\"Numbers\":[1,,2]}")]
        [InlineData("{\"Numbers\":[,1]}")]
        public void Trailing_commas_feature_still_rejects_a_comma_instead_of_an_element(string text)
        {
            var json = Utf8(text);

            Assert.Throws<JsonDocumentException>(
                () => TrailingCommasCollectionFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions { AllowTrailingCommas = true, };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureCollectionSubject>(json, options));
        }

        // ---------- NamedFloatingPointLiterals ----------

        [Fact]
        public void Named_floating_point_literals_are_rejected_on_read_by_default()
        {
            var json = Utf8("{\"F\":\"NaN\"}");

            //без фичи значение читается как ЧИСЛО-лексема, а квадратная
            //кавычка не входит в набор цифр - отказ происходит на сканере,
            //раньше, чем дело доходит до инжектора
            Assert.Throws<JsonDocumentException>(
                () => PlainFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json));
        }

        [Fact]
        public void Named_floating_point_literals_are_rejected_on_write_by_default()
        {
            //наш дефолт и эталон - оба ArgumentException, пробоем подтверждено
            using var exhauster = new PooledUtf8Exhauster();
            Assert.Throws<System.ArgumentException>(
                () => PlainFeatureSerializer.Serialize(exhauster, new FeatureSubject { F = float.NaN, })
                );
            Assert.Throws<System.ArgumentException>(() => JsonSerializer.Serialize(new FeatureSubject { F = float.NaN, }));
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        public void Named_floating_point_literals_feature_reads_all_three_special_values(string named)
        {
            var json = Utf8("{\"D\":\"" + named + "\"}");

            NamedFloatingPointLiteralsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);

            Assert.Equal(theirs!.D, ours!.D);
        }

        [Theory]
        [InlineData("nan")]
        [InlineData("infinity")]
        [InlineData("+Infinity")]
        public void Named_floating_point_literals_feature_is_exact_and_case_sensitive(string notNamed)
        {
            var json = Utf8("{\"D\":\"" + notNamed + "\"}");

            Assert.ThrowsAny<System.Exception>(
                () => NamedFloatingPointLiteralsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json, options));
        }

        [Fact]
        public void Named_floating_point_literals_feature_does_not_affect_decimal()
        {
            var json = Utf8("{\"M\":\"NaN\"}");

            Assert.ThrowsAny<System.Exception>(
                () => NamedFloatingPointLiteralsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json, options));
        }

        [Fact]
        public void Named_floating_point_literals_feature_writes_the_same_three_strings_as_the_reference()
        {
            using var exhauster = new PooledUtf8Exhauster();
            NamedFloatingPointLiteralsFeatureSerializer.Serialize(
                exhauster, new FeatureSubject { F = float.NaN, D = double.PositiveInfinity, }
                );
            var ours = Encoding.UTF8.GetString(exhauster.ToArray());

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            var theirs = JsonSerializer.Serialize(new FeatureSubject { F = float.NaN, D = double.PositiveInfinity, }, options);

            Assert.Equal(theirs, ours);
        }

        // ---------- NumbersFromStrings ----------

        [Fact]
        public void Numbers_from_strings_are_rejected_by_default()
        {
            var json = Utf8("{\"Id\":\"1\"}");

            //та же причина, что у NamedFloatingPointLiterals выше - отказ на
            //сканере числа, а не на разборе содержимого строки
            Assert.Throws<JsonDocumentException>(
                () => PlainFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json));
        }

        [Theory]
        [InlineData("{\"Id\":\"1\"}")]
        [InlineData("{\"Id\":\"01\"}")]
        [InlineData("{\"Id\":\"+1\"}")]
        public void Numbers_from_strings_feature_accepts_what_the_reference_accepts(string text)
        {
            var json = Utf8(text);

            NumbersFromStringsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);

            Assert.Equal(theirs!.Id, ours!.Id);
        }

        [Theory]
        [InlineData("{\"Id\":\" 1 \"}")]
        [InlineData("{\"Id\":\"\"}")]
        public void Numbers_from_strings_feature_still_rejects_what_the_reference_rejects(string text)
        {
            var json = Utf8(text);

            Assert.ThrowsAny<System.Exception>(
                () => NumbersFromStringsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json, options));
        }

        [Fact]
        public void Numbers_from_strings_feature_accepts_a_quoted_decimal()
        {
            var json = Utf8("{\"M\":\"1.5\"}");

            NumbersFromStringsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1.5m, ours!.M);
        }

        /// <summary>
        /// Проба опровергла ожидание из названия теста (оставлено как
        /// документ находки, а не переписано молча): <c>AllowReadingFromString</c>
        /// сам по себе, без <c>AllowNamedFloatingPointLiterals</c>, у эталона
        /// тоже читает <c>"NaN"</c> для float/double - потому что за разбором
        /// строки в обоих случаях стоит один и тот же обычный числовой парсер
        /// платформы (<c>float.Parse</c>/<c>Utf8Parser.TryParse</c>), который
        /// принимает <c>NaN</c>/<c>Infinity</c> безусловно, независимо от
        /// какого-либо JSON-флага. У нас та же симметрия: инжектор по
        /// умолчанию так же зовёт <c>Utf8Parser.TryParse</c>, и как только
        /// строка доехала до него (стало возможным веткой
        /// <c>NumbersFromStrings</c>), <c>"NaN"</c> проходит сам, без всякой
        /// проверки <c>JsonNamedFloat</c>.
        /// </summary>
        [Fact]
        public void Numbers_from_strings_alone_also_happens_to_read_named_literals_via_the_shared_number_parser()
        {
            var json = Utf8("{\"F\":\"NaN\"}");

            NumbersFromStringsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.True(float.IsNaN(ours!.F));

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);
            Assert.True(float.IsNaN(theirs!.F));
        }

        [Fact]
        public void Named_floating_point_literals_alone_does_not_open_plain_numeric_strings()
        {
            var json = Utf8("{\"F\":\"1.5\"}");

            Assert.ThrowsAny<System.Exception>(
                () => NamedFloatingPointLiteralsFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out _)
                );

            var options = new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeatureSubject>(json, options));
        }

        [Fact]
        public void Combining_both_number_features_reads_both_a_plain_string_and_a_named_literal()
        {
            var plain = Utf8("{\"F\":\"1.5\"}");
            NamedFloatAndFromStringFeatureSerializer.Deserialize(DefaultInjector.Instance, plain, out var ours1);
            Assert.Equal(1.5f, ours1!.F);

            var named = Utf8("{\"F\":\"NaN\"}");
            NamedFloatAndFromStringFeatureSerializer.Deserialize(DefaultInjector.Instance, named, out var ours2);
            Assert.True(float.IsNaN(ours2!.F));
        }

        // ---------- CaseInsensitiveNames ----------

        [Fact]
        public void Lowercase_name_is_ignored_as_unknown_by_default()
        {
            var json = Utf8("{\"id\":1}");

            PlainFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(0, ours!.Id);

            //пробой: эталон без PropertyNameCaseInsensitive тоже не находит
            //член и молча пропускает
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json);
            Assert.Equal(0, theirs!.Id);
        }

        [Theory]
        [InlineData("{\"id\":1}")]
        [InlineData("{\"ID\":1}")]
        [InlineData("{\"iD\":1}")]
        public void Case_insensitive_names_feature_matches_any_ascii_case(string text)
        {
            var json = Utf8(text);

            CaseInsensitiveNamesFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);
            Assert.Equal(1, ours!.Id);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);
            Assert.Equal(1, theirs!.Id);
        }

        // ---------- SystemTextJsonCompatible ----------

        [Fact]
        public void Compatible_composite_reads_a_document_that_needs_every_feature_at_once()
        {
            var json = Utf8(
                "// leading\n{\"id\": \"1\", \"name\":\"a\", \"f\":\"NaN\", \"d\": \"1.5\", \"m\": \"2.5\", }"
                );

            CompatibleFeatureSerializer.Deserialize(DefaultInjector.Instance, json, out var ours);

            Assert.Equal(1, ours!.Id);
            Assert.Equal("a", ours.Name);
            Assert.True(float.IsNaN(ours.F));
            Assert.Equal(1.5, ours.D);
            Assert.Equal(2.5m, ours.M);

            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    | System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                PropertyNameCaseInsensitive = true,
            };
            var theirs = JsonSerializer.Deserialize<FeatureSubject>(json, options);

            Assert.Equal(theirs!.Id, ours.Id);
            Assert.Equal(theirs.Name, ours.Name);
            Assert.Equal(theirs.D, ours.D);
            Assert.Equal(theirs.M, ours.M);
        }
    }
}
