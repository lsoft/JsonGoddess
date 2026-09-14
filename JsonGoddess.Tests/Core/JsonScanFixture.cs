using System;
using System.Text;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    public class JsonScanFixture
    {
        private static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        [Theory]
        [InlineData("{", JsonTokenKind.StartObject)]
        [InlineData("  \t\r\n {", JsonTokenKind.StartObject)]
        [InlineData("}", JsonTokenKind.EndObject)]
        [InlineData("[", JsonTokenKind.StartArray)]
        [InlineData("]", JsonTokenKind.EndArray)]
        [InlineData("\"x\"", JsonTokenKind.String)]
        [InlineData("true", JsonTokenKind.True)]
        [InlineData("false", JsonTokenKind.False)]
        [InlineData("null", JsonTokenKind.Null)]
        [InlineData("-1", JsonTokenKind.Number)]
        [InlineData("", JsonTokenKind.EndOfInput)]
        [InlineData("   ", JsonTokenKind.EndOfInput)]
        public void PeekRecognizesTokens(string text, JsonTokenKind expected)
        {
            var json = Utf8(text);
            var position = 0;

            Assert.Equal(expected, JsonScan.Peek(json, ref position));
        }

        [Fact]
        public void OnlyRfcWhitespaceIsWhitespace()
        {
            //вертикальная табуляция и form feed пробельными в JSON не являются
            var json = new byte[] { 0x0B, 0x0C, (byte)(char)0x31, };
            var position = 0;
            JsonScan.SkipWhitespace(json, ref position);

            Assert.Equal(0, position);
        }

        [Theory]
        [InlineData("\"\"", "")]
        [InlineData("\"abc\"", "abc")]
        [InlineData("\"a\\\"b\"", "a\\\"b")]
        [InlineData("\"a\\\\\"", "a\\\\")]
        public void ReadStringContentReturnsRawInnerBytes(string text, string expectedRaw)
        {
            var json = Utf8(text);
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out _);

            Assert.Equal(expectedRaw, Encoding.UTF8.GetString(raw.ToArray()));
            Assert.Equal(json.Length, position);
        }

        [Theory]
        [InlineData("\"abc\"", false)]
        [InlineData("\"a\\nb\"", true)]
        [InlineData("\"\\\\\"", true)]
        public void HasEscapeIsReportedExactly(string text, bool expected)
        {
            var json = Utf8(text);
            var position = 0;
            JsonScan.ReadStringContent(json, ref position, out var hasEscape);

            Assert.Equal(expected, hasEscape);
        }

        [Fact]
        public void EscapedQuoteDoesNotEndTheString()
        {
            //"a\"b" - это шесть байт: кавычка, a, слэш, кавычка, b, кавычка
            var json = Utf8("\"a\\\"b\" tail");
            var position = 0;
            JsonScan.ReadStringContent(json, ref position, out _);

            Assert.Equal(6, position);
        }

        [Theory]
        [InlineData("\"unterminated")]
        [InlineData("\"escaped quote at end\\\"")]
        [InlineData("\"trailing backslash\\")]
        public void UnterminatedStringThrows(string text)
        {
            var json = Utf8(text);

            Assert.Throws<JsonDocumentException>(() =>
            {
                var position = 0;
                JsonScan.ReadStringContent(json, ref position, out _);
            });
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("-1.5e-3", 7)]
        [InlineData("123,", 3)]
        [InlineData("0}", 1)]
        public void ReadNumberRawTakesTheWholeLexeme(string text, int expectedLength)
        {
            var json = Utf8(text);
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);

            Assert.Equal(expectedLength, raw.Length);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"a\":1}")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"a\":{\"b\":[1,{\"c\":null}]}}")]
        [InlineData("\"just a string\"")]
        [InlineData("\"a string with } and ] inside\"")]
        [InlineData("true")]
        [InlineData("-1.5e3")]
        [InlineData("[]")]
        [InlineData("[[[[[[[[[[]]]]]]]]]]")]
        public void SkipValueConsumesExactlyOneValue(string text)
        {
            var json = Utf8(text + "  ,rest");
            var position = 0;
            JsonScan.SkipValue(json, ref position);

            Assert.Equal(text.Length, position);
        }

        [Fact]
        public void SkipValueWalksDeepInputWithoutRecursion()
        {
            const int Depth = 20000;
            var builder = new StringBuilder(Depth * 2);
            builder.Append('[', Depth);
            builder.Append(']', Depth);

            var json = Utf8(builder.ToString());
            var position = 0;
            JsonScan.SkipValue(json, ref position);

            Assert.Equal(json.Length, position);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("[1,")]
        [InlineData("{\"a\":")]
        [InlineData("")]
        public void SkipValueOnTruncatedInputThrowsInsteadOfReadingPastTheEnd(string text)
        {
            var json = Utf8(text);

            Assert.Throws<JsonDocumentException>(() =>
            {
                var position = 0;
                JsonScan.SkipValue(json, ref position);
            });
        }

        [Fact]
        public void ExpectReportsWhatItWanted()
        {
            var json = Utf8("[");

            var exception = Assert.Throws<JsonDocumentException>(() =>
            {
                var position = 0;
                JsonScan.Expect(json, ref position, JsonScan.OpenBrace);
            });

            Assert.Contains("{", exception.Message, StringComparison.Ordinal);
        }
    }
}
