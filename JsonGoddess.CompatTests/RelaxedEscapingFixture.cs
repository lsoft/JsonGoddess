using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using JsonGoddess.Compat;
using Xunit;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// <see cref="EncoderUtf8Exhauster"/> против эталона - байт в байт, на
    /// любом энкодере.
    ///
    /// <para>
    /// Проверка перебором, а не списком случаев, и это здесь не дотошность.
    /// Набор релаксированного энкодера задан <b>таблицей Unicode</b>: кроме
    /// управляющих, кавычки и слэша он экранирует U+007F-U+00A0 и все
    /// незанятые кодовые точки - около восьми тысяч символов в одном BMP.
    /// Список, составленный человеком, описывал бы то, что человек про этот
    /// набор думает; перебор описывает то, что эталон делает.
    /// </para>
    ///
    /// <para>
    /// Раковина этот набор не повторяет, а <b>спрашивает</b> - зовёт тот же
    /// <see cref="JavaScriptEncoder"/>, которым пишет эталон. Поэтому тест
    /// проверяет не таблицу, а стык: правильно ли мы режем строку, считаем
    /// место и склеиваем куски вокруг чужого экранирования.
    /// </para>
    /// </summary>
    public class RelaxedEscapingFixture
    {
        private static readonly JavaScriptEncoder Relaxed = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        private static readonly JavaScriptEncoder Strict = JavaScriptEncoder.Default;

        /// <summary>
        /// Третий участник - и он не для полноты. Оба встроенных энкодера
        /// написаны одной рукой и могли бы делить общую ошибку, которую наш
        /// стык повторил бы вместе с ними; произвольный набор разрешённого её
        /// не разделяет.
        /// </summary>
        private static readonly JavaScriptEncoder Narrow =
            JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic);

        private static string Write(string value, JavaScriptEncoder encoder)
        {
            using (var exhauster = new EncoderUtf8Exhauster(encoder))
            {
                exhauster.Append(value);
                return exhauster.ToString();
            }
        }

        private static string Theirs(string value, JavaScriptEncoder encoder)
        {
            return Reference.Serialize(value, new JsonSerializerOptions { Encoder = encoder, });
        }

        public static TheoryData<string> Encoders()
        {
            return new TheoryData<string> { "relaxed", "strict", "narrow", };
        }

        private static JavaScriptEncoder Pick(string name)
        {
            switch (name)
            {
                case "relaxed": return Relaxed;
                case "strict": return Strict;
                default: return Narrow;
            }
        }

        /// <summary>
        /// Каждый символ BMP, кроме суррогатов, - и сам по себе, и внутри
        /// текста. Внутри текста нужен отдельно: там экранируемое стои́т не с
        /// нулевого байта, то есть работает склейка префикса с хвостом, а
        /// это и есть наш код.
        /// </summary>
        [Theory]
        [MemberData(nameof(Encoders))]
        public void Every_character_of_the_basic_plane_is_written_the_way_the_reference_writes_it(string which)
        {
            var encoder = Pick(which);
            var mismatches = new StringBuilder();
            var checkedCount = 0;

            for (var code = 0; code <= 0xFFFF; code++)
            {
                if (code >= 0xD800 && code <= 0xDFFF)
                {
                    //одиночный суррогат - отдельный случай, ниже
                    continue;
                }

                foreach (var value in new[] { ((char)code).ToString(), "a" + (char)code + "b", })
                {
                    checkedCount++;

                    var theirs = Theirs(value, encoder);
                    var ours = Write(value, encoder);

                    if (!string.Equals(theirs, ours, StringComparison.Ordinal))
                    {
                        if (mismatches.Length < 2000)
                        {
                            mismatches
                                .Append("U+").Append(code.ToString("X4"))
                                .Append(": theirs ").Append(theirs)
                                .Append(", ours ").Append(ours)
                                .AppendLine();
                        }
                    }
                }
            }

            Assert.Equal(2 * (0x10000 - 0x800), checkedCount);
            Assert.Equal(string.Empty, mismatches.ToString());
        }

        /// <summary>
        /// Астральные плоскости - шагом, а не подряд: их миллион, и каждая
        /// точка требует двух сериализаций. Шаг выбран так, чтобы попасть в
        /// каждую плоскость и в каждый её блок.
        /// </summary>
        [Theory]
        [MemberData(nameof(Encoders))]
        public void Characters_outside_the_basic_plane_are_written_the_way_the_reference_writes_them(string which)
        {
            var encoder = Pick(which);
            var mismatches = new StringBuilder();

            for (var code = 0x10000; code <= 0x10FFFF; code += 0x40)
            {
                var value = char.ConvertFromUtf32(code);

                var theirs = Theirs(value, encoder);
                var ours = Write(value, encoder);

                if (!string.Equals(theirs, ours, StringComparison.Ordinal) && mismatches.Length < 2000)
                {
                    mismatches
                        .Append("U+").Append(code.ToString("X5"))
                        .Append(": theirs ").Append(theirs)
                        .Append(", ours ").Append(ours)
                        .AppendLine();
                }
            }

            Assert.Equal(string.Empty, mismatches.ToString());
        }

        /// <summary>
        /// Все сочетания трёх кусочков, среди которых есть и корректная пара,
        /// и оба непарных суррогата, и сам символ замены.
        ///
        /// <para>
        /// Перебор нужен потому, что непарный суррогат режет строку, и резать
        /// её приходится в любом месте: в начале, в конце, подряд с другим
        /// таким же, между двумя кусками, требующими экранирования.
        /// </para>
        /// </summary>
        [Theory]
        [MemberData(nameof(Encoders))]
        public void Surrogates_in_every_arrangement_are_written_the_way_the_reference_writes_them(string which)
        {
            var encoder = Pick(which);
            var pieces = new[] { "", "a", "\uFFFD", "\uD83D\uDE00", "\uD800", "\uDC00", "Ж", "<", "\"", };
            var mismatches = new StringBuilder();

            foreach (var a in pieces)
            {
                foreach (var b in pieces)
                {
                    foreach (var c in pieces)
                    {
                        var value = a + b + c;

                        var theirs = Theirs(value, encoder);
                        var ours = Write(value, encoder);

                        if (!string.Equals(theirs, ours, StringComparison.Ordinal) && mismatches.Length < 2000)
                        {
                            mismatches
                                .Append(Show(value))
                                .Append(": theirs ").Append(theirs)
                                .Append(", ours ").Append(ours)
                                .AppendLine();
                        }
                    }
                }
            }

            Assert.Equal(string.Empty, mismatches.ToString());
        }

        /// <summary>
        /// Случай, ради которого суррогаты разбираются <b>до</b>
        /// транскодирования.
        ///
        /// <para>
        /// Непарный суррогат эталон печатает экранированным, а настоящий
        /// U+FFFD - сырым. В UTF-8 у них одни и те же три байта, и после
        /// транскодирования различить их нечем: раковина, которая сперва
        /// переводит строку в UTF-8 и только потом зовёт энкодер, написала бы
        /// вместо <c>\uFFFD</c> сырой символ замены. Тест держит именно это.
        /// </para>
        /// </summary>
        [Fact]
        public void A_lone_surrogate_is_escaped_although_the_replacement_character_itself_is_not()
        {
            Assert.Equal("\"\\uFFFD\"", Write("\uD800", Relaxed));
            Assert.Equal("\"\uFFFD\"", Write("\uFFFD", Relaxed));

            //и то же самое - словами эталона, чтобы утверждение не зависело от
            //того, что я про него помню
            Assert.Equal(Theirs("\uD800", Relaxed), Write("\uD800", Relaxed));
            Assert.Equal(Theirs("\uFFFD", Relaxed), Write("\uFFFD", Relaxed));
            Assert.NotEqual(Write("\uD800", Relaxed), Write("\uFFFD", Relaxed));
        }

        /// <summary>
        /// Одна раковина, два энкодера подряд. Держит контракт
        /// <see cref="EncoderUtf8Exhauster.Reset(JavaScriptEncoder)"/>:
        /// раковина живёт на потоке и переиспользуется, а энкодер приходит с
        /// каждым документом.
        /// </summary>
        [Fact]
        public void The_sink_takes_a_new_encoder_on_every_reset()
        {
            using (var exhauster = new EncoderUtf8Exhauster(Strict))
            {
                exhauster.Append("Ёж");
                Assert.Equal(Theirs("Ёж", Strict), exhauster.ToString());

                exhauster.Reset(Relaxed);
                exhauster.Append("Ёж");
                Assert.Equal(Theirs("Ёж", Relaxed), exhauster.ToString());

                exhauster.Reset(Strict);
                exhauster.Append("Ёж");
                Assert.Equal(Theirs("Ёж", Strict), exhauster.ToString());
            }
        }

        /// <summary>
        /// Длинная строка целиком из экранируемого: буфер обязан вырасти
        /// посреди записи, и префикс, уже лежащий в нём, обязан это пережить.
        /// </summary>
        [Theory]
        [MemberData(nameof(Encoders))]
        public void A_long_string_that_escapes_everywhere_survives_the_buffer_growing(string which)
        {
            var encoder = Pick(which);

            foreach (var length in new[] { 1, 2, 63, 64, 65, 511, 1024, 4096, })
            {
                var value = "prefix" + new string('\u0007', length) + "suffix";
                Assert.Equal(Theirs(value, encoder), Write(value, encoder));

                var cyrillic = "prefix" + new string('Ж', length) + "suffix";
                Assert.Equal(Theirs(cyrillic, encoder), Write(cyrillic, encoder));
            }
        }

        /// <summary>
        /// <c>char</c> отдельной перегрузкой - в том числе половина
        /// суррогатной пары, которой пары не досталось по построению.
        /// </summary>
        [Theory]
        [MemberData(nameof(Encoders))]
        public void A_single_char_is_written_the_way_the_reference_writes_it(string which)
        {
            var encoder = Pick(which);
            var mismatches = new StringBuilder();

            for (var code = 0; code <= 0xFFFF; code++)
            {
                var c = (char)code;

                using (var exhauster = new EncoderUtf8Exhauster(encoder))
                {
                    exhauster.Append(c);

                    var theirs = Theirs(c.ToString(), encoder);
                    var ours = exhauster.ToString();

                    if (!string.Equals(theirs, ours, StringComparison.Ordinal) && mismatches.Length < 2000)
                    {
                        mismatches
                            .Append("U+").Append(code.ToString("X4"))
                            .Append(": theirs ").Append(theirs)
                            .Append(", ours ").Append(ours)
                            .AppendLine();
                    }
                }
            }

            Assert.Equal(string.Empty, mismatches.ToString());
        }

        [Fact]
        public void A_null_string_is_the_literal_null()
        {
            Assert.Equal("null", Write(null!, Relaxed));
        }

        private static string Show(string value)
        {
            var builder = new StringBuilder();

            foreach (var c in value)
            {
                builder.Append(c < 0x20 || c > 0x7E ? "U+" + ((int)c).ToString("X4") : c.ToString());
            }

            return builder.ToString();
        }
    }
}
