using System;
#if NET8_0_OR_GREATER
using System.Buffers;
#endif

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Экранирование строки <b>так, как это делает энкодер
    /// <c>System.Text.Json</c> по умолчанию</b>.
    ///
    /// <para>
    /// Нужен ровно одному потребителю - Compat-слою. Тот, кто позвал JsonGoddess
    /// по имени, получает <see cref="JsonStringEncoder"/>: минимум RFC 8259 §7,
    /// и это осознанный выбор (PLAN.md §8.4). Тот, кто поменял пакет, а не код,
    /// ничего не выбирал, и байты его документа меняться не должны - а они
    /// менялись на любой не-ASCII строке, то есть на всяком русском имени.
    /// </para>
    ///
    /// <para>
    /// Набор снят <b>прогоном</b>, а не вычитан из их исходников
    /// (scratchpad/EscapeProbe): каждому символу BMP задан вопрос
    /// «что выйдет на твоём месте». Ответ:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>U+0000–U+001F - экранируются все, из них шесть короткой формой
    /// (<c>\b \t \n \f \r</c>) и <c>\\</c>, остальные <c>\uXXXX</c>;</item>
    /// <item><c>"</c> - <b>не</b> <c>\"</c>, а <c>\u0022</c>. Это первое, что
    /// ломает наивное «возьмём наш энкодер и добавим не-ASCII»;</item>
    /// <item><c>&amp;</c>, <c>'</c>, <c>+</c>, <c>&lt;</c>, <c>&gt;</c>,
    /// <c>`</c> - <c>\uXXXX</c> (защита от вставки JSON прямо в HTML);</item>
    /// <item>U+007F и <b>весь</b> не-ASCII - <c>\uXXXX</c>, заглавными
    /// шестнадцатеричными. Неэкранированных символов выше U+007E у них нет ни
    /// одного - проверено перебором всех 63 тысяч;</item>
    /// <item>вне BMP - суррогатной парой, двумя escape'ами подряд: эмодзи
    /// U+1F600 даёт <c>\uD83D\uDE00</c>;</item>
    /// <item>непарный суррогат - <c>\uFFFD</c>, а не свой код.</item>
    /// </list>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonReferenceStringEncoder
    {
        /// <summary>
        /// Максимум байтов на <b>один символ</b>: <c>\uXXXX</c>. Суррогатная
        /// пара - два символа и двенадцать байт, то есть шесть на символ, и
        /// бюджет, посчитанный по этой константе, её покрывает.
        /// </summary>
        public const int MaxBytesPerEscape = 6;

        /// <summary>
        /// Символ, которым эталон заменяет непарный суррогат. Не наш выбор:
        /// снято прогоном, <c>Serialize("\uD800")</c> даёт <c>"\uFFFD"</c>.
        /// </summary>
        private const char Replacement = '\uFFFD';

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool NeedsEscape(char c)
        {
            //всё, что вне печатного ASCII, - разом: сюда попадают и управляющие,
            //и U+007F, и весь не-ASCII, которого у эталона не остаётся сырым ни
            //одного символа
            if (c < 0x20 || c > 0x7E)
            {
                return true;
            }

            return c == '"' || c == '\\' || c == '&' || c == '\'' || c == '+' || c == '<' || c == '>' || c == '`';
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Список <b>разрешённых</b>, а не запрещённых, и поиск через
        /// <c>IndexOfAnyExcept</c>. У <see cref="JsonStringEncoder"/> наоборот,
        /// и причина ровно в размере: там запрещённых 34, здесь - все 63 тысячи
        /// символов выше U+007E. Перечислять их нечем, а разрешённых 87, и они
        /// укладываются в ASCII-битмап, по которому поиск идёт векторно.
        ///
        /// Это же, с точностью до знака, делает и сам <c>System.Text.Json</c>.
        /// </summary>
        private static readonly SearchValues<char> AllowedChars = SearchValues.Create(CreateAllowedSet());

        private static string CreateAllowedSet()
        {
            var allowed = new System.Text.StringBuilder(96);
            for (var c = (char)0x20; c <= 0x7E; c++)
            {
                if (!NeedsEscape(c))
                {
                    allowed.Append(c);
                }
            }

            return allowed.ToString();
        }
#endif

        /// <summary>
        /// Индекс первого символа, требующего экранирования, или -1.
        /// </summary>
        public static int IndexOfEscapable(ReadOnlySpan<char> text)
        {
#if NET8_0_OR_GREATER
            return text.IndexOfAnyExcept(AllowedChars);
#else
            for (var i = 0; i < text.Length; i++)
            {
                if (NeedsEscape(text[i]))
                {
                    return i;
                }
            }

            return -1;
#endif
        }

        /// <summary>
        /// Пишет escape для символа <paramref name="text"/><c>[0]</c> и
        /// сообщает, сколько символов при этом съедено: суррогатная пара - это
        /// один символ Unicode, записанный двумя <c>char</c>, и разорвать её
        /// нельзя.
        ///
        /// <para>
        /// В <paramref name="destination"/> обязано быть не меньше
        /// <c>2 *</c> <see cref="MaxBytesPerEscape"/> байт.
        /// </para>
        /// </summary>
        public static int WriteEscape(ReadOnlySpan<char> text, Span<byte> destination, out int consumed)
        {
            var c = text[0];
            consumed = 1;

            switch (c)
            {
                case '\\':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'\\';
                    return 2;
                case '\b':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'b';
                    return 2;
                case '\f':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'f';
                    return 2;
                case '\n':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'n';
                    return 2;
                case '\r':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'r';
                    return 2;
                case '\t':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'t';
                    return 2;
            }

            //кавычка сюда и попадает: у эталона она \u0022, а не \"

            if (char.IsHighSurrogate(c))
            {
                if (text.Length > 1 && char.IsLowSurrogate(text[1]))
                {
                    consumed = 2;
                    WriteHex(c, destination);
                    WriteHex(text[1], destination.Slice(6));
                    return 12;
                }

                WriteHex(Replacement, destination);
                return 6;
            }

            if (char.IsLowSurrogate(c))
            {
                //низкий суррогат без высокого перед ним - такой же обрывок
                WriteHex(Replacement, destination);
                return 6;
            }

            WriteHex(c, destination);
            return 6;
        }

        private static void WriteHex(char c, Span<byte> destination)
        {
            destination[0] = (byte)'\\';
            destination[1] = (byte)'u';
            destination[2] = HexDigit((c >> 12) & 0xF);
            destination[3] = HexDigit((c >> 8) & 0xF);
            destination[4] = HexDigit((c >> 4) & 0xF);
            destination[5] = HexDigit(c & 0xF);
        }

        /// <summary>
        /// Заглавные - как у эталона. Снято прогоном: <c>U+0467</c> даёт
        /// <c>ѧ</c>, <c>U+4E2D</c> - <c>中</c>.
        /// </summary>
        private static byte HexDigit(int value)
        {
            return (byte)(value < 10 ? '0' + value : 'A' + (value - 10));
        }
    }
}
