using System;
#if NET8_0_OR_GREATER
using System.Buffers;
#endif

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Экранирование строки на запись.
    ///
    /// Набор экранируемого - минимум RFC 8259 §7: кавычка, обратный слэш и
    /// управляющие символы ниже U+0020. Ничего сверх этого. Это осознанное
    /// расхождение с <c>System.Text.Json</c>, чей энкодер по умолчанию
    /// (<c>JavaScriptEncoder.Default</c>) дополнительно экранирует
    /// HTML-значимые символы и весь не-ASCII: то поведение защищает вставку
    /// JSON в HTML, стоит прохода по каждой строке и раздувает документ.
    /// Эквивалент нашего набора у них -
    /// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c>, и именно с ним
    /// сверяется дифференциальный тест.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonStringEncoder
    {
        /// <summary>
        /// Максимум байтов, в который может развернуться один символ:
        /// <c>\u00XX</c>. Верхняя граница для резервирования места под
        /// escape-последовательность.
        /// </summary>
        public const int MaxBytesPerEscape = 6;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool NeedsEscape(char c)
        {
            return c == '"' || c == '\\' || c < 0x20;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Экранируемые символы одним множеством: 32 управляющих плюс кавычка и
        /// обратный слэш. Все ниже U+0080, поэтому <see cref="SearchValues{T}"/>
        /// уложит их в ASCII-битмап и поиск пойдёт векторно.
        ///
        /// System.Text.Json делает то же самое с точностью до знака: у них
        /// список <b>разрешённых</b> символов и <c>IndexOfAnyExcept</c>, потому
        /// что их набор зависит от выбранного энкодера и в общем случае
        /// огромен. У нас набор фиксирован RFC 8259 §7 и мал, поэтому список
        /// запрещённых короче и не требует дополнения.
        /// </summary>
        private static readonly SearchValues<char> EscapableChars = SearchValues.Create(CreateEscapableSet());

        private static char[] CreateEscapableSet()
        {
            var set = new char[34];
            for (var i = 0; i < 32; i++)
            {
                set[i] = (char)i;
            }

            set[32] = '"';
            set[33] = '\\';
            return set;
        }
#endif

        /// <summary>
        /// Индекс первого символа, требующего экранирования, или -1.
        /// Подавляющее большинство строк отвечает -1 и уходит одним
        /// транскодированием, поэтому цена этого поиска - это и есть цена
        /// записи строки.
        /// </summary>
        public static int IndexOfEscapable(ReadOnlySpan<char> text)
        {
#if NET8_0_OR_GREATER
            return text.IndexOfAny(EscapableChars);
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
        /// Пишет escape-последовательность для одного символа. В
        /// <paramref name="destination"/> обязано быть не меньше
        /// <see cref="MaxBytesPerEscape"/> байт.
        /// </summary>
        public static int WriteEscape(char c, Span<byte> destination)
        {
            switch (c)
            {
                case '"':
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'"';
                    return 2;
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
                default:
                    destination[0] = (byte)'\\';
                    destination[1] = (byte)'u';
                    destination[2] = (byte)'0';
                    destination[3] = (byte)'0';
                    destination[4] = HexDigit((c >> 4) & 0xF);
                    destination[5] = HexDigit(c & 0xF);
                    return 6;
            }
        }

        /// <summary>
        /// Верхний регистр - не вкусовщина: <c>System.Text.Json</c> пишет
        /// hex-escape с большой буквы, и расхождение в регистре цифры
        /// сделало бы вывод не байт-в-байт совпадающим с эталоном на ровном
        /// месте. Снято прогоном, а не прочитано в документации.
        /// </summary>
        private static byte HexDigit(int value)
        {
            return (byte)(value < 10 ? '0' + value : 'A' + (value - 10));
        }

#if NET8_0_OR_GREATER
        public static int Transcode(ReadOnlySpan<char> text, Span<byte> destination)
        {
            return System.Text.Encoding.UTF8.GetBytes(text, destination);
        }
#else
        public static unsafe int Transcode(ReadOnlySpan<char> text, Span<byte> destination)
        {
            if (text.Length == 0)
            {
                return 0;
            }

            fixed (char* src = text)
            fixed (byte* dst = destination)
            {
                return System.Text.Encoding.UTF8.GetBytes(src, text.Length, dst, destination.Length);
            }
        }
#endif
    }
}
