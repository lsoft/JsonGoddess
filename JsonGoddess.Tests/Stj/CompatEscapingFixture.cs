using System;
using System.Text;
using Xunit;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// <c>CompatUtf8Exhauster</c> против энкодера <c>System.Text.Json</c> по
    /// умолчанию - байт в байт.
    ///
    /// <para>
    /// Это обратная сторона <see cref="EscapingDivergenceFixture"/>. Там
    /// закреплено, что мы с эталоном расходимся <b>намеренно</b>, и это верно
    /// для того, кто позвал JsonGoddess по имени. Здесь закреплено, что для
    /// того, кто подменил <c>JsonSerializer</c> фасадом, расхождения нет: он
    /// поменял пакет, а не код, и байты его документа меняться не должны.
    /// </para>
    ///
    /// <para>
    /// Главная проверка - перебором всего BMP, а не списком случаев. Набор
    /// экранируемого у эталона задан таблицей, а не правилом, и список,
    /// составленный мной, описывал бы то, что я про него думаю. Перебор
    /// описывает то, что он делает.
    /// </para>
    /// </summary>
    public class CompatEscapingFixture
    {
        private static string Write(string value)
        {
            using var exhauster = new CompatUtf8Exhauster();
            exhauster.Append(value);
            return exhauster.ToString();
        }

        /// <summary>
        /// Каждый символ BMP, кроме суррогатов, - в строке из одного символа.
        /// Шестьдесят три тысячи утверждений в одном тесте: разбивать их на
        /// теории значило бы шестьдесят три тысячи строк в отчёте.
        /// </summary>
        [Fact]
        public void Every_character_of_the_basic_plane_is_written_the_way_the_reference_writes_it()
        {
            var mismatches = new StringBuilder();
            var checkedCount = 0;

            for (var code = 0; code <= 0xFFFF; code++)
            {
                if (code >= 0xD800 && code <= 0xDFFF)
                {
                    //одиночный суррогат - отдельный случай, ниже
                    continue;
                }

                var value = ((char)code).ToString();
                var theirs = Reference.WriteDefaultEncoder(value);
                var ours = Write(value);

                checkedCount++;

                if (!string.Equals(theirs, ours, StringComparison.Ordinal))
                {
                    if (mismatches.Length < 2000)
                    {
                        mismatches.Append("U+").Append(code.ToString("X4"))
                            .Append(": эталон ").Append(theirs)
                            .Append(", мы ").Append(ours).Append('\n');
                    }
                }
            }

            Assert.True(checkedCount > 63000, "проверено символов: " + checkedCount);
            Assert.Equal(string.Empty, mismatches.ToString());
        }

        /// <summary>
        /// Вне BMP эталон пишет суррогатной парой, двумя escape'ами подряд, -
        /// и это единственное место, где escape съедает два <c>char</c>.
        /// Разорви мы пару - вышло бы два обрывка, то есть другой текст.
        /// </summary>
        [Theory]
        [InlineData("\U0001F600")]
        [InlineData("\U00010330")]
        [InlineData("a\U0001F600b")]
        [InlineData("\U0001F600\U0001F601")]
        public void Characters_outside_the_basic_plane_go_as_a_surrogate_pair(string value)
        {
            Assert.Equal(Reference.WriteDefaultEncoder(value), Write(value));
        }

        /// <summary>
        /// Непарный суррогат эталон подменяет на U+FFFD - не на свой код.
        /// Проверено пробой (scratchpad/EscapeProbe), а не выведено из того,
        /// что «так принято».
        /// </summary>
        [Theory]
        [InlineData("\uD800")]
        [InlineData("\uDC00")]
        [InlineData("a\uD800b")]
        [InlineData("\uD800\uD800")]
        [InlineData("\uDC00\uD83D\uDE00")]
        public void A_lone_surrogate_becomes_the_replacement_character(string value)
        {
            Assert.Equal(Reference.WriteDefaultEncoder(value), Write(value));
        }

        /// <summary>
        /// Строки, на которых спотыкается буфер: экранируемое в начале, в
        /// конце, подряд и вперемежку, плюс длинная - чтобы сработал рост.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("plain ascii")]
        [InlineData("Привет, мир")]
        [InlineData("<script>alert('x' + \"y\")</script>")]
        [InlineData("`backtick`")]
        [InlineData("\u007Fdel")]
        [InlineData("таб\tи\nперевод")]
        public void Strings_are_written_the_way_the_reference_writes_them(string value)
        {
            Assert.Equal(Reference.WriteDefaultEncoder(value), Write(value));
        }

        [Fact]
        public void A_long_non_ascii_string_grows_the_buffer_and_still_matches()
        {
            var value = new string('ё', 4096);

            Assert.Equal(Reference.WriteDefaultEncoder(value), Write(value));
        }

        /// <summary>
        /// <c>char</c> отдельной перегрузкой: у эталона одиночный символ - та
        /// же строка из одного символа.
        /// </summary>
        [Theory]
        [InlineData('a')]
        [InlineData('"')]
        [InlineData('<')]
        [InlineData('ё')]
        [InlineData('\n')]
        [InlineData('\uD800')]
        public void A_char_is_written_the_way_the_reference_writes_it(char value)
        {
            using var exhauster = new CompatUtf8Exhauster();
            exhauster.Append(value);

            Assert.Equal(Reference.WriteDefaultEncoder(value), exhauster.ToString());
        }

        /// <summary>
        /// Обычный sink не тронут. Проверка стоит здесь, а не в соседнем файле,
        /// потому что риск появился именно от этой правки: <c>Append</c>
        /// перестал быть <c>sealed</c>, и молча подменить набор экранируемого
        /// всем стало возможно.
        /// </summary>
        [Fact]
        public void The_ordinary_sink_keeps_its_own_set()
        {
            using var exhauster = new PooledUtf8Exhauster();
            exhauster.Append("Привет <b>");

            Assert.Equal(Reference.Write("Привет <b>"), exhauster.ToString());
        }
    }
}
