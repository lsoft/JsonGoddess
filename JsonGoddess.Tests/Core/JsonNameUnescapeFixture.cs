using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// Разэкранирование имени свойства в байтах.
    ///
    /// Ожидание берётся из <c>Utf8JsonReader</c> внутри теста: он читает ту же
    /// строку и отдаёт разэкранированный результат, так что сверяется не наше
    /// представление о RFC 8259 §7, а поведение эталона.
    /// </summary>
    public class JsonNameUnescapeFixture
    {
        [Theory]
        [InlineData("plain")]
        [InlineData("with\\\"quote")]
        [InlineData("with\\\\backslash")]
        [InlineData("with\\/slash")]
        [InlineData("tab\\there")]
        [InlineData("line\\nbreak")]
        [InlineData("\\b\\f\\n\\r\\t")]
        [InlineData("\\u0049d")]
        [InlineData("I\\u0064")]
        [InlineData("\\u0438\\u043c\\u044f")]
        [InlineData("\\u0438мя")]
        [InlineData("\\ud83d\\ude00")]
        [InlineData("prefix\\ud83d\\ude00suffix")]
        [InlineData("\\u0000")]
        [InlineData("\\uFFFF")]
        [InlineData("")]
        public void Matches_the_bcl_reader(string content)
        {
            Assert.Equal(ExpectedFromBcl(content), Unescape(content));
        }

        /// <summary>
        /// Выход никогда не длиннее входа - на этом держится размер буфера.
        /// </summary>
        [Theory]
        [InlineData("\\u0049d")]
        [InlineData("\\ud83d\\ude00")]
        [InlineData("\\n\\n\\n\\n")]
        [InlineData("\\uFFFF\\uFFFF")]
        public void Never_grows(string content)
        {
            var raw = Encoding.UTF8.GetBytes(content);
            var buffer = new byte[raw.Length];

            var written = JsonNameUnescape.Decode(raw, buffer);

            Assert.True(written <= raw.Length, "unescaping must never grow the name");
        }

        /// <summary>
        /// Непарный суррогат - не повод отказать: такое имя просто не совпадёт
        /// ни с одним членом и уедет в пропуск. Отказ за него - дело будущего
        /// стража <c>InvalidUtf8</c>, а не разэкранирования.
        ///
        /// Эталон здесь не спросить: <c>Utf8JsonReader</c> на непарном
        /// суррогате бросает, а нам нужно продолжить.
        /// </summary>
        [Theory]
        [InlineData("\\ud83d")]
        [InlineData("\\ude00")]
        [InlineData("\\ud83dx")]
        [InlineData("\\ud83d\\u0041")]
        public void Lone_surrogate_becomes_the_replacement_character(string content)
        {
            Assert.Contains("�", Unescape(content), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("\\")]
        [InlineData("\\u")]
        [InlineData("\\u00")]
        [InlineData("\\u004")]
        [InlineData("\\uZZZZ")]
        [InlineData("\\q")]
        public void Malformed_escape_is_a_document_error(string content)
        {
            Assert.Throws<JsonDocumentException>(() => Unescape(content));
        }

        /// <summary>
        /// Буфер короче входа - ошибка вызывающего, а не документа, и она
        /// обязана быть названа так, а не выпасть выходом за границу.
        /// </summary>
        [Fact]
        public void Too_small_a_destination_is_rejected_by_name()
        {
            var raw = Encoding.UTF8.GetBytes("abcdef");

            Assert.Throws<ArgumentException>(() => JsonNameUnescape.Decode(raw, new byte[raw.Length - 1]));
        }

        private static string Unescape(string content)
        {
            var raw = Encoding.UTF8.GetBytes(content);
            var buffer = new byte[raw.Length];

            var written = JsonNameUnescape.Decode(raw, buffer);
            return Encoding.UTF8.GetString(buffer, 0, written);
        }

        private static string ExpectedFromBcl(string content)
        {
            var document = Encoding.UTF8.GetBytes("\"" + content + "\"");

            var reader = new Utf8JsonReader(document);
            Assert.True(reader.Read());

            return reader.GetString()!;
        }
    }
}
