using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// Чтение: на одном и том же тексте наш разбор даёт то же значение, что
    /// <c>System.Text.Json</c>.
    ///
    /// Сравнение идёт с результатом BCL, а не с исходным значением, и это
    /// важнее, чем кажется: round-trip "записали своё - прочли своё" проходит
    /// и у библиотеки, которая систематически ошибается в обе стороны.
    /// </summary>
    public class InjectorParityFixture
    {
        private static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Int32s), MemberType = typeof(LexicalParityFixture))]
        public void Int32(int value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out int actual);

            Assert.Equal(JsonSerializer.Deserialize<int>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Int64s), MemberType = typeof(LexicalParityFixture))]
        public void Int64(long value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out long actual);

            Assert.Equal(JsonSerializer.Deserialize<long>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Doubles), MemberType = typeof(LexicalParityFixture))]
        public void Double(double value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out double actual);

            Assert.Equal(JsonSerializer.Deserialize<double>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Singles), MemberType = typeof(LexicalParityFixture))]
        public void Single(float value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out float actual);

            Assert.Equal(JsonSerializer.Deserialize<float>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Decimals), MemberType = typeof(LexicalParityFixture))]
        public void Decimal(decimal value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out decimal actual);

            Assert.Equal(JsonSerializer.Deserialize<decimal>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Booleans), MemberType = typeof(LexicalParityFixture))]
        public void Boolean(bool value)
        {
            var json = Utf8(Reference.Write(value));
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, json, out bool actual);

            Assert.Equal(JsonSerializer.Deserialize<bool>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Strings), MemberType = typeof(LexicalParityFixture))]
        [MemberData(nameof(LexicalParityFixture.AstralStrings), MemberType = typeof(LexicalParityFixture))]
        public void String(string value)
        {
            //эталон пишет с дефолтным энкодером намеренно: на чтении мы обязаны
            //понимать и его вывод тоже, со всеми \uXXXX, которых сами не пишем
            var json = Utf8(Reference.WriteDefaultEncoder(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out string actual);

            Assert.Equal(JsonSerializer.Deserialize<string>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Chars), MemberType = typeof(LexicalParityFixture))]
        public void Char(char value)
        {
            var json = Utf8(Reference.WriteDefaultEncoder(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out char actual);

            Assert.Equal(JsonSerializer.Deserialize<char>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.Guids), MemberType = typeof(LexicalParityFixture))]
        public void Guid_(Guid value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out Guid actual);

            Assert.Equal(JsonSerializer.Deserialize<Guid>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.DateTimes), MemberType = typeof(LexicalParityFixture))]
        public void DateTime_(DateTime value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out DateTime actual);

            var expected = JsonSerializer.Deserialize<DateTime>(json);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Kind, actual.Kind);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.DateTimeOffsets), MemberType = typeof(LexicalParityFixture))]
        public void DateTimeOffset_(DateTimeOffset value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out DateTimeOffset actual);

            var expected = JsonSerializer.Deserialize<DateTimeOffset>(json);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Offset, actual.Offset);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.TimeSpans), MemberType = typeof(LexicalParityFixture))]
        public void TimeSpan_(TimeSpan value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out TimeSpan actual);

            Assert.Equal(JsonSerializer.Deserialize<TimeSpan>(json), actual);
        }

        [Theory]
        [MemberData(nameof(LexicalParityFixture.ByteArrays), MemberType = typeof(LexicalParityFixture))]
        public void Base64(byte[] value)
        {
            var json = Utf8(Reference.Write(value));
            var position = 0;
            var raw = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.ParseText(ref context, raw, hasEscape, out byte[] actual);

            Assert.Equal(JsonSerializer.Deserialize<byte[]>(json), actual);
        }

        public static IEnumerable<object[]> NumberLexemesStjAccepts()
        {
            //формы, которые пишет не наш сериализатор, а кто-то ещё
            yield return new object[] { "1e5", };
            yield return new object[] { "1E5", };
            yield return new object[] { "1e+5", };
            yield return new object[] { "-1.5e-3", };
            yield return new object[] { "0", };
            yield return new object[] { "-0", };
        }

        [Theory]
        [MemberData(nameof(NumberLexemesStjAccepts))]
        public void ForeignNumberForms(string lexeme)
        {
            var json = Utf8(lexeme);
            var position = 0;
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            var context = new JsonParseContext(json);
            DefaultInjector.Instance.Parse(ref context, raw, out double actual);

            Assert.Equal(JsonSerializer.Deserialize<double>(json), actual);
        }
    }
}
