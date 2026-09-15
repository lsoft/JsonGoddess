using System;
using System.Text;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// Сворачивание регистра в байтах. Ожидание берётся у BCL:
    /// <c>string.Equals(OrdinalIgnoreCase)</c> - это то, чем сворачивает
    /// регистр <c>Enum.TryParse</c>, а значит и эталон.
    ///
    /// Совпадать с ним мы обязаны <b>только на ASCII</b> - и ровно поэтому имя
    /// члена enum'а вне ASCII генератор отвергает. Расхождение здесь не
    /// спрятано в комментарий, а закреплено тестом: на кириллице BCL
    /// сворачивает, а мы нет, и если это когда-нибудь изменится, тест об этом
    /// скажет.
    /// </summary>
    public class JsonAsciiNameFixture
    {
        [Theory]
        [InlineData("Draft", "Draft")]
        [InlineData("draft", "Draft")]
        [InlineData("DRAFT", "Draft")]
        [InlineData("dRaFt", "Draft")]
        [InlineData("Draf", "Draft")]
        [InlineData("Drafts", "Draft")]
        [InlineData("Draft1", "Draft1")]
        [InlineData("sent-out", "sent-out")]
        [InlineData("SENT-OUT", "sent-out")]
        [InlineData("_a_", "_A_")]
        [InlineData("", "")]
        public void Ascii_folding_agrees_with_the_bcl(string value, string literal)
        {
            Assert.Equal(
                string.Equals(value, literal, StringComparison.OrdinalIgnoreCase),
                JsonAsciiName.EqualsIgnoreCase(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(literal))
                );
        }

        /// <summary>
        /// Байт, отличающийся от литерала только пятым битом, но не являющийся
        /// буквой, совпадением не становится: <c>'@'</c> и <c>'`'</c>, <c>'['</c>
        /// и <c>'{'</c> - именно такие пары, и наивное <c>| 0x20</c> склеило бы
        /// их.
        /// </summary>
        [Theory]
        [InlineData("@", "`")]
        [InlineData("[", "{")]
        [InlineData("]", "}")]
        [InlineData("^", "~")]
        public void Non_letters_are_not_folded(string value, string literal)
        {
            Assert.False(string.Equals(value, literal, StringComparison.OrdinalIgnoreCase));
            Assert.False(
                JsonAsciiName.EqualsIgnoreCase(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(literal))
                );
        }

        /// <summary>
        /// Осознанное расхождение, а не недосмотр - и потому генератор не даёт
        /// в него попасть.
        /// </summary>
        [Fact]
        public void Non_ascii_is_where_we_diverge_from_the_bcl_on_purpose()
        {
            var lower = Encoding.UTF8.GetBytes("отправлено");
            var upper = Encoding.UTF8.GetBytes("Отправлено");

            Assert.Equal("отправлено", "Отправлено", StringComparer.OrdinalIgnoreCase);
            Assert.False(JsonAsciiName.EqualsIgnoreCase(lower, upper));

            //при точном совпадении байтов расхождения нет никакого
            Assert.True(JsonAsciiName.EqualsIgnoreCase(upper, upper));
        }
    }
}
