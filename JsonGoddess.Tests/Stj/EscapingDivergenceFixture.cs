using System.Text.Json;
using Xunit;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// Расхождения с <c>System.Text.Json</c> на записи строк. Их ровно два, оба
    /// осознанные, и оба закреплены так, чтобы отмена решения красила тест
    /// намеренно.
    ///
    /// <b>Первое - набор экранируемого.</b> RFC 8259 §7 требует экранировать
    /// кавычку, обратный слэш и управляющие символы, и всё. Энкодер STJ по
    /// умолчанию сверх этого экранирует HTML-значимые символы, апостроф, плюс и
    /// весь не-ASCII, а кавычку пишет не как <c>\"</c>, а шестизначным escape.
    /// Это защита от вставки JSON прямо в HTML: она стоит прохода по каждой
    /// строке и раздувает кириллицу в шесть раз. Нам такая защита не по адресу -
    /// вставкой в HTML занимается тот, кто вставляет. Их эквивалент нашего
    /// набора - <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c>.
    ///
    /// <b>Второе - символы вне BMP.</b> Даже расслабленный энкодер STJ
    /// экранирует их суррогатной парой: его список разрешённого -
    /// <c>UnicodeRanges.All</c>, а это весь <b>базовый</b> плоскость, то есть
    /// до U+FFFF. Эмодзи у них уезжает двенадцатью байтами ASCII, у нас -
    /// четырьмя байтами UTF-8. Это обнаружено прогоном, а не вычитано.
    ///
    /// В обоих случаях расхождение только в байтах: ниже проверяется, что наш
    /// вывод BCL читает и получает ровно исходную строку.
    /// </summary>
    public class EscapingDivergenceFixture
    {
        private static string Write(string value)
        {
            using var exhauster = new PooledUtf8Exhauster();
            exhauster.Append(value);
            return exhauster.ToString();
        }

        [Theory]
        [InlineData("<script>")]
        [InlineData("a & b")]
        [InlineData("it's")]
        [InlineData("2 + 2")]
        [InlineData("кириллица")]
        [InlineData("with \"quote\"")]
        public void StjDefaultEncoderEscapesMoreThanWeDo(string value)
        {
            var ours = Write(value);

            //с расслабленным энкодером совпадаем байт в байт
            Assert.Equal(Reference.Write(value), ours);

            //с энкодером по умолчанию - нет; это и есть первое расхождение
            Assert.NotEqual(Reference.WriteDefaultEncoder(value), ours);

            //но значит то же самое
            Assert.Equal(value, JsonSerializer.Deserialize<string>(ours));
        }

        [Theory]
        [InlineData("\U0001F600")]
        [InlineData("\U0001F600 emoji")]
        [InlineData("𐍈 gothic")]
        public void CharactersAboveTheBasicPlaneStayRawInOurOutput(string value)
        {
            var ours = Write(value);

            //здесь расходимся уже и с расслабленным энкодером: UnicodeRanges.All
            //кончается на U+FFFF
            Assert.NotEqual(Reference.Write(value), ours);

            //и всё же это тот же документ по смыслу
            Assert.Equal(value, JsonSerializer.Deserialize<string>(ours));

            //наш вывод короче ровно потому, что не разворачивает суррогатную пару
            Assert.True(ours.Length < Reference.Write(value).Length);
        }

        /// <summary>
        /// Третье расхождение, и до сих пор оно было незаписанным: U+007F
        /// (DEL) расслабленный энкодер экранирует, а мы нет.
        ///
        /// <para>
        /// Утверждение «наш набор совпадает с
        /// <c>UnsafeRelaxedJsonEscaping</c>» верно с точностью до этого одного
        /// символа, и оно стояло и в PLAN.md §8.4, и в
        /// <c>docs/stj-divergences.md</c> 1.1. Нашлось перебором всего BMP,
        /// затеянным ради Compat-слоя (PLAN.md §15 O9), - то есть тем же
        /// способом, каким вообще положено узнавать про эталон.
        /// </para>
        ///
        /// <para>
        /// Ни одна форма дифференциального харнесса U+007F не содержит,
        /// поэтому направление <c>=</c> зелено и без этого знания. Тест стоит
        /// здесь ровно затем, чтобы знание перестало быть неписаным.
        /// </para>
        /// </summary>
        [Fact]
        public void DelIsEscapedByTheRelaxedEncoderAndNotByUs()
        {
            const string value = "ab";

            var ours = Write(value);

            Assert.Equal("\"ab\"", ours);
            Assert.Equal("\"a\\u007Fb\"", Reference.Write(value));
            Assert.Equal("\"a\\u007Fb\"", Reference.WriteDefaultEncoder(value));

            //документ тот же по смыслу - расходятся только байты
            Assert.Equal(value, JsonSerializer.Deserialize<string>(ours));
        }

        [Theory]
        [InlineData("plain text")]
        [InlineData("with \\ backslash")]
        [InlineData("with \n newline")]
        [InlineData("with \t tab")]
        public void OnEverythingElseBothEncodersAgreeWithUs(string value)
        {
            var ours = Write(value);

            Assert.Equal(Reference.Write(value), ours);
            Assert.Equal(Reference.WriteDefaultEncoder(value), ours);
        }
    }
}
