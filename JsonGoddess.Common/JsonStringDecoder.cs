using System;
using System.Buffers;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// UTF-8 + escape-последовательности RFC 8259 §7 в <see cref="string"/>.
    ///
    /// Промежуточной строки здесь нет и быть не должно: содержимое
    /// раскодируется сразу в буфер (стек до
    /// <see cref="StackThreshold"/> символов, <see cref="ArrayPool{T}"/> выше),
    /// и материализуется ровно один раз - в ту строку, которую положат в POCO.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonStringDecoder
    {
        public const int StackThreshold = 256;

        /// <summary>
        /// Содержимое строки (без кавычек) в <see cref="string"/>.
        /// <paramref name="hasEscape"/> приходит из
        /// <see cref="JsonScan.ReadStringContent"/>: false - обратного слэша в
        /// содержимом нет, и весь escape-код можно не исполнять вовсе.
        /// </summary>
        public static string Decode(ReadOnlySpan<byte> raw, bool hasEscape)
        {
            if (raw.Length == 0)
            {
                return string.Empty;
            }

            if (!hasEscape)
            {
                return Utf8ToString(raw);
            }

            //верхняя граница: ни одна конструкция не даёт символов больше, чем
            //байтов. \uXXXX - 6 байт на символ, 4-байтовая последовательность
            //UTF-8 - два символа суррогатной пары, остальные - один к одному
            var maxChars = raw.Length;

            char[]? rented = null;
            try
            {
                Span<char> buffer = maxChars <= StackThreshold
                    ? stackalloc char[StackThreshold]
                    : (rented = ArrayPool<char>.Shared.Rent(maxChars));

                var written = DecodeToBuffer(raw, hasEscape, buffer);
                return SpanToString(buffer.Slice(0, written));
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }

        /// <summary>
        /// То же, но в готовый буфер. Возвращает число записанных символов.
        /// Буфер обязан быть не короче <c>raw.Length</c>.
        /// </summary>
        public static int DecodeToBuffer(ReadOnlySpan<byte> raw, bool hasEscape, Span<char> destination)
        {
            var decoded = Transcode(raw, destination);
            if (!hasEscape)
            {
                return decoded;
            }

            return Unescape(destination.Slice(0, decoded));
        }

        /// <summary>
        /// Разворачивает escape-последовательности на месте: выход не длиннее
        /// входа, поэтому второй буфер не нужен.
        ///
        /// Куски между escape'ами переносятся целиком, а не посимвольно: в
        /// строке с одним <c>\n</c> посередине посимвольный проход прошёл бы по
        /// всей строке ради одного символа. Так же устроен
        /// <c>JsonReaderHelper.Unescape</c> у System.Text.Json.
        /// <see cref="Span{T}.CopyTo"/> корректен при перекрытии - источник и
        /// приёмник здесь один буфер, и приёмник всегда левее.
        /// </summary>
        private static int Unescape(Span<char> text)
        {
            var write = 0;
            var read = 0;

            while (read < text.Length)
            {
                var rest = text.Slice(read);
                var slash = rest.IndexOf('\\');
                if (slash < 0)
                {
                    if (write != read)
                    {
                        rest.CopyTo(text.Slice(write));
                    }

                    return write + rest.Length;
                }

                if (slash > 0)
                {
                    if (write != read)
                    {
                        rest.Slice(0, slash).CopyTo(text.Slice(write));
                    }

                    write += slash;
                    read += slash;
                }

                read++;
                if (read >= text.Length)
                {
                    throw new JsonDocumentException("Truncated escape sequence.");
                }

                var e = text[read++];
                switch (e)
                {
                    case '"':
                        text[write++] = '"';
                        break;
                    case '\\':
                        text[write++] = '\\';
                        break;
                    case '/':
                        text[write++] = '/';
                        break;
                    case 'b':
                        text[write++] = '\b';
                        break;
                    case 'f':
                        text[write++] = '\f';
                        break;
                    case 'n':
                        text[write++] = '\n';
                        break;
                    case 'r':
                        text[write++] = '\r';
                        break;
                    case 't':
                        text[write++] = '\t';
                        break;
                    case 'u':
                        if (read + 4 > text.Length)
                        {
                            throw new JsonDocumentException("Truncated \\u escape sequence.");
                        }

                        text[write++] = (char)(
                            (Hex(text[read]) << 12)
                            | (Hex(text[read + 1]) << 8)
                            | (Hex(text[read + 2]) << 4)
                            | Hex(text[read + 3])
                            );
                        read += 4;
                        break;

                    default:
                        throw new JsonDocumentException("Unrecognized escape sequence '\\" + e + "'.");
                }
            }

            return write;
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            throw new JsonDocumentException("'" + c + "' is not a hexadecimal digit.");
        }

#if NET8_0_OR_GREATER
        private static string Utf8ToString(ReadOnlySpan<byte> raw)
        {
            return System.Text.Encoding.UTF8.GetString(raw);
        }

        private static string SpanToString(ReadOnlySpan<char> text)
        {
            return new string(text);
        }

        public static int Transcode(ReadOnlySpan<byte> raw, Span<char> destination)
        {
            return System.Text.Encoding.UTF8.GetChars(raw, destination);
        }
#else
        private static unsafe string Utf8ToString(ReadOnlySpan<byte> raw)
        {
            fixed (byte* p = raw)
            {
                return System.Text.Encoding.UTF8.GetString(p, raw.Length);
            }
        }

        private static unsafe string SpanToString(ReadOnlySpan<char> text)
        {
            fixed (char* p = text)
            {
                return new string(p, 0, text.Length);
            }
        }

        public static unsafe int Transcode(ReadOnlySpan<byte> raw, Span<char> destination)
        {
            if (raw.Length == 0)
            {
                return 0;
            }

            fixed (byte* src = raw)
            fixed (char* dst = destination)
            {
                return System.Text.Encoding.UTF8.GetChars(src, raw.Length, dst, destination.Length);
            }
        }
#endif
    }
}
