using System;
using System.Text;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// <see cref="JsonTryScan"/> против <see cref="JsonScan"/>.
    ///
    /// <para>
    /// Проверяются не отдельные случаи, а два утверждения - на каждом префиксе
    /// каждого образца:
    /// </para>
    ///
    /// <list type="number">
    /// <item>
    /// <b>На закрытой трубе семейства неотличимы.</b> <c>final: true</c> обязан
    /// дать то же значение, ту же позицию и то же исключение с тем же
    /// смещением, что и обычный сканер на том же самом вводе - обрезанном в том
    /// числе. Это и есть смысл флага: он не «режим помягче», он выключает
    /// единственное отличие.
    /// </item>
    /// <item>
    /// <b>Под окном - либо «не хватило», либо целая лексема.</b>
    /// <c>final: false</c> на префиксе валидного документа не имеет права ни
    /// бросить, ни вернуть укороченный ответ: <c>123</c> на краю окна может
    /// оказаться началом <c>1234</c>, и принять его за число - это молча
    /// испорченные данные, которых не увидит никто.
    /// </item>
    /// </list>
    ///
    /// <para>
    /// Второе утверждение - то самое, ради чего фикстура заведена: первая
    /// версия прототипа его нарушала на числах, и ни падения, ни предупреждения
    /// это не давало.
    /// </para>
    /// </summary>
    public class JsonTryScanFixture
    {
        /// <summary>Обычный сканер: отдаёт свой результат строкой или бросает.</summary>
        private delegate string Straight(ReadOnlySpan<byte> json, ref int position);

        /// <summary><c>Try</c>-сканер: то же плюс исход «не хватило».</summary>
        private delegate bool Tried(ReadOnlySpan<byte> json, ref int position, bool final, out string rendered);

        private static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static string Text(ReadOnlySpan<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Оба утверждения на каждом префиксе - от пустого до целого.
        /// </summary>
        private static void BothFamiliesAgree(string text, Straight straight, Tried tried)
        {
            var full = Utf8(text);

            var wholePosition = 0;
            var whole = straight(full, ref wholePosition);

            for (var length = 0; length <= full.Length; length++)
            {
                var prefix = new ReadOnlySpan<byte>(full, 0, length);
                var where = "«" + text + "», префикс " + length;

                //(1) закрытая труба - полное совпадение, включая отказ
                var straightPosition = 0;
                string? straightRendered = null;
                JsonDocumentException? straightError = null;

                try
                {
                    straightRendered = straight(prefix, ref straightPosition);
                }
                catch (JsonDocumentException error)
                {
                    straightError = error;
                }

                var closedPosition = 0;
                var closedRendered = default(string);
                JsonDocumentException? closedError = null;
                var closedEnough = false;

                try
                {
                    closedEnough = tried(prefix, ref closedPosition, true, out closedRendered);
                }
                catch (JsonDocumentException error)
                {
                    closedError = error;
                }

                if (straightError is not null)
                {
                    Assert.True(closedError is not null, where + ": обычный сканер отказал, Try - нет");
                    Assert.Equal(straightError.Reason, closedError!.Reason);
                    Assert.Equal(straightError.BytePosition, closedError.BytePosition);
                }
                else
                {
                    Assert.True(closedError is null, where + ": Try отказал там, где обычный сканер прошёл");
                    Assert.True(closedEnough, where + ": на закрытой трубе «не хватило» невозможно");
                    Assert.Equal(straightRendered, closedRendered);
                    Assert.Equal(straightPosition, closedPosition);
                }

                //(2) открытая труба - либо ложь, либо ответ целого документа
                var openPosition = 0;
                var openEnough = tried(prefix, ref openPosition, false, out var openRendered);

                if (openEnough)
                {
                    Assert.Equal(whole, openRendered);
                    Assert.Equal(wholePosition, openPosition);
                }
            }
        }

        [Theory]
        [InlineData("   x")]
        [InlineData("\t\r\n x")]
        [InlineData("x")]
        public void WhitespaceAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    JsonScan.SkipWhitespace(json, ref position);
                    return "ws";
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = "ws";
                    return JsonTryScan.SkipWhitespace(json, ref position, final);
                });
        }

        [Theory]
        [InlineData("// строчный\n x")]
        [InlineData("/* блочный */ x")]
        [InlineData("  /*a*/ /*b*/  x")]
        [InlineData("// до конца ввода")]
        [InlineData("/ x")]
        public void CommentsAgree(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    JsonScan.SkipWhitespaceAndComments(json, ref position);
                    return "wsc";
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = "wsc";
                    return JsonTryScan.SkipWhitespaceAndComments(json, ref position, final);
                });
        }

        [Theory]
        [InlineData("{")]
        [InlineData("  [")]
        [InlineData("\"s\"")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("null")]
        [InlineData("-12")]
        public void PeekAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => JsonScan.Peek(json, ref position).ToString(),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.Peek(json, ref position, final, out var kind))
                    {
                        return false;
                    }

                    rendered = kind.ToString();
                    return true;
                });
        }

        [Theory]
        [InlineData("{x", JsonScan.OpenBrace)]
        [InlineData("   {x", JsonScan.OpenBrace)]
        [InlineData(" , ", JsonScan.Comma)]
        public void ExpectAgrees(string text, byte expected)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    JsonScan.Expect(json, ref position, expected);
                    return "expect";
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = "expect";
                    return JsonTryScan.Expect(json, ref position, expected, final);
                });
        }

        [Theory]
        [InlineData(",x", JsonScan.Comma)]
        [InlineData("  ,x", JsonScan.Comma)]
        [InlineData("  }", JsonScan.Comma)]
        public void TryConsumeAgrees(string text, byte expected)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                    JsonScan.TryConsume(json, ref position, expected).ToString(),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.TryConsume(json, ref position, expected, final, out var consumed))
                    {
                        return false;
                    }

                    rendered = consumed.ToString();
                    return true;
                });
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("\"abc\"")]
        [InlineData("  \"abc\"  ")]
        [InlineData("\"a\\\"b\"")]
        [InlineData("\"a\\\\\"")]
        [InlineData("\"кириллица и 😀\"")]
        public void StringAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    var content = JsonScan.ReadStringContent(json, ref position, out var hasEscape);
                    return Text(content) + "|" + hasEscape;
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadStringContent(json, ref position, final, out var content, out var hasEscape))
                    {
                        return false;
                    }

                    rendered = Text(content) + "|" + hasEscape;
                    return true;
                });
        }

        [Theory]
        [InlineData("\"abc\"")]
        [InlineData("\"a\\tb\"")]
        [InlineData("\"кириллица\"")]
        public void StrictStringAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    var content = JsonScan.ReadStringContentStrict(json, ref position, out var hasEscape);
                    return Text(content) + "|" + hasEscape;
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadStringContentStrict(json, ref position, final, out var content, out var hasEscape))
                    {
                        return false;
                    }

                    rendered = Text(content) + "|" + hasEscape;
                    return true;
                });
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1234")]
        [InlineData("1.5")]
        [InlineData("1e9")]
        [InlineData("-1.5E-3")]
        [InlineData("  42  ")]
        public void LooseNumberAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => Text(JsonScan.ReadNumberRaw(json, ref position)),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadNumberRaw(json, ref position, final, out var raw))
                    {
                        return false;
                    }

                    rendered = Text(raw);
                    return true;
                });
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1234")]
        [InlineData("1.5")]
        [InlineData("1e9")]
        [InlineData("-1.5E-3")]
        [InlineData("1234,")]
        [InlineData("0.125}")]
        public void StrictNumberAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => Text(JsonScan.ReadNumberRawStrict(json, ref position)),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadNumberRawStrict(json, ref position, final, out var raw))
                    {
                        return false;
                    }

                    rendered = Text(raw);
                    return true;
                });
        }

        [Theory]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("null")]
        [InlineData("  true,")]
        public void LiteralAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => Text(JsonScan.ReadLiteralRaw(json, ref position)),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadLiteralRaw(json, ref position, final, out var raw))
                    {
                        return false;
                    }

                    rendered = Text(raw);
                    return true;
                });
        }

        [Theory]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("  true ")]
        public void BooleanAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => JsonScan.ReadBoolean(json, ref position).ToString(),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.ReadBoolean(json, ref position, final, out var value))
                    {
                        return false;
                    }

                    rendered = value.ToString();
                    return true;
                });
        }

        [Theory]
        [InlineData("null")]
        [InlineData("  null ")]
        [InlineData("nope")]
        [InlineData("123")]
        public void NullAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) => JsonScan.TryReadNull(json, ref position).ToString(),
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = string.Empty;
                    if (!JsonTryScan.TryReadNull(json, ref position, final, out var matched))
                    {
                        return false;
                    }

                    rendered = matched.ToString();
                    return true;
                });
        }

        [Theory]
        [InlineData("1")]
        [InlineData("\"s\"")]
        [InlineData("true")]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("{\"a\":[1,2,{\"b\":\"c\"}],\"d\":null}")]
        [InlineData("[[[[1]]]]")]
        [InlineData("{\"a\":\"}\"}")]
        public void SkipValueAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    JsonScan.SkipValue(json, ref position);
                    return "skipped";
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    rendered = "skipped";
                    return JsonTryScan.SkipValue(json, ref position, final);
                });
        }

        [Theory]
        [InlineData("{\"a\":[1,2,{\"b\":\"c\"}],\"d\":null}")]
        [InlineData("[[[[1]]]]")]
        public void GuardedSkipValueAgrees(string text)
        {
            BothFamiliesAgree(
                text,
                (ReadOnlySpan<byte> json, ref int position) =>
                {
                    var depth = 0;
                    JsonScan.SkipValueGuarded(json, ref position, ref depth, 64);
                    return "depth " + depth;
                },
                (ReadOnlySpan<byte> json, ref int position, bool final, out string rendered) =>
                {
                    var depth = 0;
                    rendered = string.Empty;

                    if (!JsonTryScan.SkipValueGuarded(json, ref position, ref depth, 64, final))
                    {
                        //счётчик обязан вернуться к тому, чем был: попытку
                        //переиграют с начала элемента, и недосчитанная
                        //глубина отказала бы там, куда документ не заходил
                        Assert.Equal(0, depth);
                        return false;
                    }

                    rendered = "depth " + depth;
                    return true;
                });
        }

        /// <summary>
        /// Кривизна, уже лежащая в окне, - отказ, и открытая труба этого не
        /// отменяет. Иначе флаг <c>final</c> превратился бы в «режим помягче»,
        /// а обрезанный документ - в молча принятый.
        /// </summary>
        [Theory]
        [InlineData("+1")]
        [InlineData(".5")]
        [InlineData("01")]
        [InlineData("1.x")]
        public void MalformedNumbersAreRefusedEvenWithMoreToCome(string text)
        {
            var json = Utf8(text);
            var position = 0;

            Assert.Throws<JsonDocumentException>(
                () => JsonTryScan.ReadNumberRawStrict(json, ref position, false, out _)
                );
        }

        [Fact]
        public void AControlCharacterInsideTheWindowIsRefusedEvenWithMoreToCome()
        {
            var json = new byte[] { (byte)'"', (byte)'a', 0x09, (byte)'b', };
            var position = 0;

            Assert.Throws<JsonDocumentException>(
                () => JsonTryScan.ReadStringContentStrict(json, ref position, false, out _, out _)
                );
        }

        /// <summary>
        /// Проверка самой проверки: префиксы обязаны действительно давать
        /// «не хватило», иначе <see cref="BothFamiliesAgree"/> зелен и
        /// бесполезен - второе утверждение в нём условное.
        /// </summary>
        [Fact]
        public void ThePrefixesReallyDoRunOut()
        {
            var json = Utf8("\"abcdef\"");
            var ran = 0;

            for (var length = 0; length < json.Length; length++)
            {
                var position = 0;
                if (!JsonTryScan.ReadStringContent(new ReadOnlySpan<byte>(json, 0, length), ref position, false, out _, out _))
                {
                    ran++;
                }
            }

            Assert.Equal(json.Length, ran);
        }
    }
}
