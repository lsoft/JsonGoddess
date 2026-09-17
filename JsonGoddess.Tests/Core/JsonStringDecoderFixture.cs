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

#if NET8_0_OR_GREATER
        /// <summary>
        /// <c>JsonGuard.InvalidUtf8</c> проверяет строку, а строит её sink, -
        /// значит проверка обязана не выделять <b>ничего</b>.
        ///
        /// Тест заведён по следам замера: сперва <c>EnsureValidUtf8</c> звал
        /// <c>DecodeStrict</c>, тот материализовал <c>string</c> и выбрасывал
        /// его, и страж выходил в полтора раза дороже по аллокациям на чистом
        /// документе (896 B против 616 B на REGULAR). Поймалось это по
        /// аллокациям, а не по времени, - они детерминированы, и потому же
        /// закрепляются тестом, а не строкой в отчёте.
        ///
        /// Только net8+: <c>GC.GetAllocatedBytesForCurrentThread</c> на
        /// netstandard2.0 нет, а поведение проверяется тут не рантайма, а
        /// нашего кода - оно общее для всех таргетов.
        /// </summary>
        [Fact]
        public void Validating_utf8_allocates_nothing()
        {
            var plain = Encoding.UTF8.GetBytes("Hello, world");
            var escaped = Encoding.UTF8.GetBytes("Hello\\u0021 \\uD83D\\uDE00");

            //прогрев: первый проход платит за JIT и за таблицы кодировщика
            for (var i = 0; i < 16; i++)
            {
                JsonStringDecoder.EnsureValidUtf8(plain, false);
                JsonStringDecoder.EnsureValidUtf8(escaped, true);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 64; i++)
            {
                JsonStringDecoder.EnsureValidUtf8(plain, false);
                JsonStringDecoder.EnsureValidUtf8(escaped, true);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(
                allocated == 0,
                "проверка UTF-8 выделила " + allocated + " байт на 128 вызовов, а должна ноль"
                );
        }
#endif
    }
}
