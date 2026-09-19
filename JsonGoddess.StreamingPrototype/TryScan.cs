using System;
using JsonGoddess;
using JsonGoddess.Internal;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// <c>Try</c>-вариант примитивов сканера: то же, что <c>JsonScan</c>, но с
    /// одним новым исходом - «байты кончились, дайте добавки».
    ///
    /// <para>
    /// Правило, на котором держится вся схема: <b>ложь возвращается только на
    /// нехватку данных, кривизна по-прежнему бросает</b>. И различать эти два
    /// исхода обязан не автор кода, а флаг <paramref name="final"/>: пока труба
    /// не закрыта, «кончилось» значит «подожди»; закрыта - то же место
    /// отказывает ровно тем же исключением, что и сегодня. Поэтому ошибка в
    /// классификации не может дать молча принятый обрезанный документ - она
    /// стои́т лишнего круга по трубе.
    /// </para>
    ///
    /// <para>
    /// Позиция при возврате лжи не восстанавливается: вызывающий всё равно
    /// откатывается к началу элемента, а не к началу лексемы.
    /// </para>
    /// </summary>
    internal static class TryScan
    {
        internal const byte Quote = (byte)'"';
        internal const byte Backslash = (byte)'\\';
        internal const byte OpenBrace = (byte)'{';
        internal const byte CloseBrace = (byte)'}';
        internal const byte OpenBracket = (byte)'[';
        internal const byte CloseBracket = (byte)']';
        internal const byte Colon = (byte)':';
        internal const byte Comma = (byte)',';

        private static ReadOnlySpan<byte> True => "true"u8;

        private static ReadOnlySpan<byte> False => "false"u8;

        private static ReadOnlySpan<byte> NullWord => "null"u8;

        /// <summary>
        /// Пробелы. Упереться в конец буфера здесь - это ещё не конец
        /// документа: следующий байт может оказаться каким угодно.
        /// </summary>
        internal static bool Whitespace(ReadOnlySpan<byte> json, ref int position, bool final)
        {
            while (position < json.Length)
            {
                var b = json[position];
                if (b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                {
                    position++;
                    continue;
                }

                return true;
            }

            return final;
        }

        internal static bool Expect(ReadOnlySpan<byte> json, ref int position, byte expected, bool final)
        {
            if (!Whitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                if (final)
                {
                    throw Truncated(position, expected);
                }

                return false;
            }

            if (json[position] != expected)
            {
                throw new JsonDocumentException(
                    "Expected '" + (char)expected + "', but found '" + (char)json[position] + "'.",
                    position
                    );
            }

            position++;
            return true;
        }

        /// <summary>
        /// Съесть байт, если он тот самый. Исход здесь тройной, поэтому
        /// «съели» уезжает в <paramref name="consumed"/>, а возвращаемое
        /// значение означает только «хватило ли данных, чтобы это решить».
        /// </summary>
        internal static bool TryConsume(
            ReadOnlySpan<byte> json,
            ref int position,
            byte expected,
            bool final,
            out bool consumed
            )
        {
            consumed = false;

            if (!Whitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                return final;
            }

            if (json[position] != expected)
            {
                return true;
            }

            position++;
            consumed = true;
            return true;
        }

        internal static bool String(
            ReadOnlySpan<byte> json,
            ref int position,
            bool final,
            out ReadOnlySpan<byte> content,
            out bool hasEscape
            )
        {
            content = default;
            hasEscape = false;

            if (!Whitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                if (final)
                {
                    throw Truncated(position, Quote);
                }

                return false;
            }

            if (json[position] != Quote)
            {
                throw new JsonDocumentException(
                    "Expected '\"', but found '" + (char)json[position] + "'.",
                    position
                    );
            }

            var start = position + 1;
            var i = start;

            while (true)
            {
                if (i >= json.Length)
                {
                    return Unterminated(start, final);
                }

                var hit = json.Slice(i).IndexOfAny(Quote, Backslash);
                if (hit < 0)
                {
                    return Unterminated(start, final);
                }

                i += hit;

                if (json[i] == Quote)
                {
                    content = json.Slice(start, i - start);
                    position = i + 1;
                    return true;
                }

                //обратный слэш: следующий байт экранирован, каким бы он ни был
                hasEscape = true;
                i += 2;
            }
        }

        private static bool Unterminated(int start, bool final)
        {
            if (final)
            {
                throw new JsonDocumentException("Unterminated string.", start);
            }

            return false;
        }

        /// <summary>
        /// Число по грамматике RFC 8259 §6 - то есть <c>ReadNumberRawStrict</c>,
        /// потому что <c>Compat</c> включает <see cref="JsonGuard.StrictNumbers"/>.
        ///
        /// <para>
        /// Вот то самое место «сорта Б»: сегодня конец буфера означает законный
        /// конец лексемы, а в окне - двусмысленность, потому что <c>123</c> на
        /// краю может оказаться началом <c>1234</c>, <c>1.5</c> или <c>1e9</c>.
        /// Поэтому «упёрлись в край» здесь проверяется <b>после каждой части</b>
        /// грамматики, а не один раз в конце.
        /// </para>
        /// </summary>
        internal static bool Number(
            ReadOnlySpan<byte> json,
            ref int position,
            bool final,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = default;

            if (!Whitespace(json, ref position, final))
            {
                return false;
            }

            var start = position;
            var i = start;

            if (i < json.Length && json[i] == (byte)'-')
            {
                i++;
            }

            if (i >= json.Length)
            {
                return Need(final, "Invalid number: expected a digit.", i);
            }

            if (json[i] == (byte)'0')
            {
                i++;
            }
            else if (json[i] >= (byte)'1' && json[i] <= (byte)'9')
            {
                i++;
                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }
            }
            else
            {
                throw new JsonDocumentException("Invalid number: expected a digit.", i);
            }

            if (i >= json.Length)
            {
                //дальше могли бы стоять цифра, точка или экспонента
                return final && Finish(json, start, i, ref position, out raw);
            }

            if (json[i] == (byte)'.')
            {
                i++;
                var fracStart = i;

                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }

                if (i == fracStart)
                {
                    return Need(final, "Invalid number: expected a digit after '.'.", i);
                }

                if (i >= json.Length)
                {
                    return final && Finish(json, start, i, ref position, out raw);
                }
            }

            if (json[i] == (byte)'e' || json[i] == (byte)'E')
            {
                i++;

                if (i < json.Length && (json[i] == (byte)'+' || json[i] == (byte)'-'))
                {
                    i++;
                }

                var expStart = i;

                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }

                if (i == expStart)
                {
                    return Need(final, "Invalid number: expected a digit in the exponent.", i);
                }

                if (i >= json.Length)
                {
                    return final && Finish(json, start, i, ref position, out raw);
                }
            }

            //висящая цифра сразу после того, как грамматика решила, что число
            //кончилось, - это и есть '01'
            if (Digit(json[i]))
            {
                throw new JsonDocumentException("Invalid number: unexpected extra digit.", i);
            }

            return Finish(json, start, i, ref position, out raw);
        }

        private static bool Digit(byte b) => b >= (byte)'0' && b <= (byte)'9';

        private static bool Finish(
            ReadOnlySpan<byte> json,
            int start,
            int end,
            ref int position,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = json.Slice(start, end - start);
            position = end;
            return true;
        }

        /// <summary>
        /// Нехватка или кривизна - решает <paramref name="final"/>, и только он.
        /// </summary>
        private static bool Need(bool final, string message, int at)
        {
            if (final)
            {
                throw new JsonDocumentException(message, at);
            }

            return false;
        }

        internal static bool Boolean(ReadOnlySpan<byte> json, ref int position, bool final, out bool value)
        {
            value = false;

            if (!Keyword(json, ref position, True, final, out var matched))
            {
                return false;
            }

            if (matched)
            {
                value = true;
                return true;
            }

            if (!Keyword(json, ref position, False, final, out matched))
            {
                return false;
            }

            if (!matched)
            {
                throw new JsonDocumentException("Expected 'true' or 'false'.", position);
            }

            return true;
        }

        internal static bool Null(ReadOnlySpan<byte> json, ref int position, bool final, out bool matched)
        {
            return Keyword(json, ref position, NullWord, final, out matched);
        }

        /// <summary>
        /// Слово целиком. Если в окне лежит его начало и больше ничего -
        /// это нехватка, а не несовпадение: <c>tru</c> на краю станет
        /// <c>true</c>, когда доедет четвёртый байт.
        /// </summary>
        private static bool Keyword(
            ReadOnlySpan<byte> json,
            ref int position,
            ReadOnlySpan<byte> word,
            bool final,
            out bool matched
            )
        {
            matched = false;

            if (!Whitespace(json, ref position, final))
            {
                return false;
            }

            var rest = json.Slice(position);

            if (rest.Length >= word.Length)
            {
                if (rest.StartsWith(word))
                {
                    position += word.Length;
                    matched = true;
                }

                return true;
            }

            if (!final && word.StartsWith(rest))
            {
                return false;
            }

            return true;
        }

        private static JsonDocumentException Truncated(int position, byte expected)
        {
            return new JsonDocumentException(
                "Expected '" + (char)expected + "', but instead reached end of data.",
                position
                );
        }
    }
}
