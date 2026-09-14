using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    public class JsonStringDecoderFixture
    {
        /// <summary>
        /// Раскодировать содержимое строки так, как его увидит POCO.
        /// <paramref name="jsonLiteral"/> - строка вместе с кавычками, то есть
        /// ровно то, что стоит в документе.
        /// </summary>
        private static string Decode(string jsonLiteral)
        {
            var json = Encoding.UTF8.GetBytes(jsonLiteral);
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            return JsonStringDecoder.Decode(raw, hasEscape);
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("\"plain\"")]
        [InlineData("\"\\\"\"")]
        [InlineData("\"\\\\\"")]
        [InlineData("\"\\/\"")]
        [InlineData("\"\\b\\f\\n\\r\\t\"")]
        [InlineData("\"\\u0041\"")]
        [InlineData("\"\\u00e9\"")]
        [InlineData("\"\\u0439\"")]
        [InlineData("\"\\uD83D\\uDE00\"")]
        [InlineData("\"mixed \\u0041 and \\n and plain\"")]
        [InlineData("\"кириллица без escape\"")]
        [InlineData("\"\\u0000\"")]
        public void EveryEscapeFormMatchesTheReference(string jsonLiteral)
        {
            Assert.Equal(JsonSerializer.Deserialize<string>(jsonLiteral), Decode(jsonLiteral));
        }

        [Fact]
        public void LongStringGoesThroughThePooledPathAndComesOutTheSame()
        {
            //длиннее StackThreshold, чтобы исполнилась ветка с арендой буфера
            var value = new string('я', JsonStringDecoder.StackThreshold * 3) + "\\n";
            var jsonLiteral = "\"" + value + "\"";

            Assert.Equal(JsonSerializer.Deserialize<string>(jsonLiteral), Decode(jsonLiteral));
        }

        [Theory]
        [InlineData("\"\\x\"")]
        [InlineData("\"\\u00\"")]
        [InlineData("\"\\uZZZZ\"")]
        public void MalformedEscapeThrowsInsteadOfProducingGarbage(string jsonLiteral)
        {
            Assert.Throws<JsonDocumentException>(() => Decode(jsonLiteral));
        }

        [Fact]
        public void SurrogatePairSurvivesAsOneCodePoint()
        {
            var decoded = Decode("\"\\uD83D\\uDE00\"");

            Assert.Equal(2, decoded.Length);
            Assert.True(char.IsSurrogatePair(decoded[0], decoded[1]));
            Assert.Equal(0x1F600, char.ConvertToUtf32(decoded[0], decoded[1]));
        }
    }
}
