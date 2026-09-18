using System.Text;

namespace JsonGoddess.Generator.Shared
{
    /// <summary>
    /// Экранирование текста, который печатается в порождённый код
    /// <b>константой</b>, - так, как его экранирует энкодер
    /// <c>System.Text.Json</c> по умолчанию.
    ///
    /// <para>
    /// Нужно Compat-слою и только ему: имя свойства, имя enum'а и значение
    /// дискриминатора известны на компиляции, и экранировать их в рантайме
    /// значило бы платить за то, за что платить не надо. Строки значений так не
    /// закрыть - их содержимое известно только в рантайме, - и ими занимается
    /// <c>JsonGoddess.CompatUtf8Exhauster</c>.
    /// </para>
    ///
    /// <para>
    /// Правила обязаны совпасть с ним один в один, и это <b>контракт между
    /// двумя копиями</b> - той же природы, что у <c>JsonGuard</c> и
    /// <c>JsonFeature</c>: генератор не ссылается на рантайм-сборку, и общего
    /// кода у них быть не может. Совпадение проверяется не глазами, а прогоном
    /// их корпуса поверх фасада (PLAN.md §11.1, маршрут B) и отдельным тестом
    /// на именах.
    /// </para>
    /// </summary>
    public static class ReferenceEscaping
    {
        /// <summary>
        /// Тело JSON-строки - без обрамляющих кавычек.
        ///
        /// <para>
        /// Набор снят прогоном (scratchpad/EscapeProbe): управляющие,
        /// <c>"</c> (именно <c>\u0022</c>, а не <c>\"</c>), <c>&amp;</c>,
        /// <c>'</c>, <c>+</c>, <c>&lt;</c>, <c>&gt;</c>, <c>`</c>, U+007F и
        /// весь не-ASCII. Вне BMP - суррогатной парой, непарный суррогат -
        /// <c>\uFFFD</c>.
        /// </para>
        /// </summary>
        public static string Body(string text)
        {
            StringBuilder? builder = null;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (!NeedsEscape(c))
                {
                    builder?.Append(c);
                    continue;
                }

                if (builder is null)
                {
                    //подавляющее большинство имён - обычные идентификаторы, и
                    //для них не заводится даже буфера
                    builder = new StringBuilder(text.Length + 8);
                    builder.Append(text, 0, i);
                }

                switch (c)
                {
                    case '\\': builder.Append("\\\\"); continue;
                    case '\b': builder.Append("\\b"); continue;
                    case '\f': builder.Append("\\f"); continue;
                    case '\n': builder.Append("\\n"); continue;
                    case '\r': builder.Append("\\r"); continue;
                    case '\t': builder.Append("\\t"); continue;
                }

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        AppendHex(builder, c);
                        AppendHex(builder, text[i + 1]);
                        i++;
                        continue;
                    }

                    AppendHex(builder, '\uFFFD');
                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    AppendHex(builder, '\uFFFD');
                    continue;
                }

                AppendHex(builder, c);
            }

            return builder is null ? text : builder.ToString();
        }

        /// <summary>
        /// Готовый JSON-литерал: либо строка в кавычках - тогда экранируется
        /// её тело, - либо что-то другое (число дискриминатора), и тогда
        /// трогать нечего.
        /// </summary>
        public static string Literal(string literal)
        {
            if (literal.Length < 2 || literal[0] != '"' || literal[literal.Length - 1] != '"')
            {
                return literal;
            }

            return "\"" + Body(literal.Substring(1, literal.Length - 2)) + "\"";
        }

        private static bool NeedsEscape(char c)
        {
            if (c < 0x20 || c > 0x7E)
            {
                return true;
            }

            return c == '"' || c == '\\' || c == '&' || c == '\'' || c == '+' || c == '<' || c == '>' || c == '`';
        }

        private static void AppendHex(StringBuilder builder, char c)
        {
            builder.Append("\\u");
            builder.Append(HexDigit((c >> 12) & 0xF));
            builder.Append(HexDigit((c >> 8) & 0xF));
            builder.Append(HexDigit((c >> 4) & 0xF));
            builder.Append(HexDigit(c & 0xF));
        }

        /// <summary>Заглавные - как у эталона, снято прогоном.</summary>
        private static char HexDigit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'A' + (value - 10));
        }
    }
}
