using System;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Разэкранирование имени свойства <b>в байтах</b>.
    ///
    /// Отличается от <see cref="JsonStringDecoder"/> целью, а не аккуратностью.
    /// Там результат нужен строкой, поэтому выгоднее сперва транскодировать, а
    /// потом разэкранировать в символах: суррогатная пара тогда складывается
    /// сама. Здесь результат нужен ровно в той форме, в которой с ним сравнивает
    /// диспетчер, - в UTF-8, - и уходить в UTF-16 и обратно значило бы
    /// проделать лишнюю работу ради худшего результата.
    ///
    /// Выходной буфер никогда не бывает длиннее входа: <c>\uXXXX</c> - это шесть
    /// байт на входе и не больше трёх на выходе, суррогатная пара - двенадцать
    /// на четыре, короткий escape - два на один. Поэтому размер буфера известен
    /// заранее и равен длине сырого имени.
    /// </summary>
    public static class JsonNameUnescape
    {
        private const byte Backslash = (byte)'\\';

        /// <summary>
        /// U+FFFD. Непарный суррогат в имени - не повод отказать документу:
        /// такое имя просто не совпадёт ни с одним членом и уедет в пропуск.
        /// Отказывать за него будет страж <c>InvalidUtf8</c>, когда появится.
        /// </summary>
        private const int ReplacementCharacter = 0xFFFD;

        public static int Decode(scoped ReadOnlySpan<byte> raw, scoped Span<byte> destination)
        {
            if (destination.Length < raw.Length)
            {
                throw new ArgumentException(
                    "The destination must be at least as long as the raw name.",
                    nameof(destination)
                    );
            }

            var written = 0;
            var i = 0;

            while (i < raw.Length)
            {
                //куски между escape'ами переносятся целиком, а не побайтово
                var next = raw.Slice(i).IndexOf(Backslash);
                if (next < 0)
                {
                    raw.Slice(i).CopyTo(destination.Slice(written));
                    return written + (raw.Length - i);
                }

                if (next > 0)
                {
                    raw.Slice(i, next).CopyTo(destination.Slice(written));
                    written += next;
                    i += next;
                }

                i++;
                if (i >= raw.Length)
                {
                    throw new JsonDocumentException("Truncated escape sequence in a property name.");
                }

                var escaped = raw[i++];
                switch (escaped)
                {
                    case (byte)'"': destination[written++] = (byte)'"'; break;
                    case Backslash: destination[written++] = Backslash; break;
                    case (byte)'/': destination[written++] = (byte)'/'; break;
                    case (byte)'b': destination[written++] = 0x08; break;
                    case (byte)'f': destination[written++] = 0x0C; break;
                    case (byte)'n': destination[written++] = 0x0A; break;
                    case (byte)'r': destination[written++] = 0x0D; break;
                    case (byte)'t': destination[written++] = 0x09; break;

                    case (byte)'u':
                        written += WriteCodePoint(raw, ref i, destination.Slice(written));
                        break;

                    default:
                        throw new JsonDocumentException("Unknown escape sequence in a property name.");
                }
            }

            return written;
        }

        private static int WriteCodePoint(scoped ReadOnlySpan<byte> raw, ref int i, scoped Span<byte> destination)
        {
            var code = ReadHex4(raw, ref i);

            if (code >= 0xDC00 && code <= 0xDFFF)
            {
                //младший суррогат без старшего перед ним
                return WriteUtf8(ReplacementCharacter, destination);
            }

            if (code < 0xD800 || code > 0xDBFF)
            {
                return WriteUtf8(code, destination);
            }

            //старший суррогат: пара имеет смысл только вместе со следующим
            //\uXXXX, и только если тот действительно младший
            if (i + 1 < raw.Length && raw[i] == Backslash && raw[i + 1] == (byte)'u')
            {
                var restore = i;
                i += 2;

                var low = ReadHex4(raw, ref i);
                if (low >= 0xDC00 && low <= 0xDFFF)
                {
                    return WriteUtf8(0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00), destination);
                }

                i = restore;
            }

            return WriteUtf8(ReplacementCharacter, destination);
        }

        private static int ReadHex4(scoped ReadOnlySpan<byte> raw, ref int i)
        {
            if (i + 4 > raw.Length)
            {
                throw new JsonDocumentException("Truncated \\u escape sequence in a property name.");
            }

            var value = 0;
            for (var digit = 0; digit < 4; digit++)
            {
                value = (value << 4) | Hex(raw[i + digit]);
            }

            i += 4;
            return value;
        }

        private static int Hex(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                return b - (byte)'0';
            }

            if (b >= (byte)'a' && b <= (byte)'f')
            {
                return b - (byte)'a' + 10;
            }

            if (b >= (byte)'A' && b <= (byte)'F')
            {
                return b - (byte)'A' + 10;
            }

            throw new JsonDocumentException("Invalid hexadecimal digit in a \\u escape sequence.");
        }

        private static int WriteUtf8(int codePoint, scoped Span<byte> destination)
        {
            if (codePoint < 0x80)
            {
                destination[0] = (byte)codePoint;
                return 1;
            }

            if (codePoint < 0x800)
            {
                destination[0] = (byte)(0xC0 | (codePoint >> 6));
                destination[1] = (byte)(0x80 | (codePoint & 0x3F));
                return 2;
            }

            if (codePoint < 0x10000)
            {
                destination[0] = (byte)(0xE0 | (codePoint >> 12));
                destination[1] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                destination[2] = (byte)(0x80 | (codePoint & 0x3F));
                return 3;
            }

            destination[0] = (byte)(0xF0 | (codePoint >> 18));
            destination[1] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
            destination[2] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
            destination[3] = (byte)(0x80 | (codePoint & 0x3F));
            return 4;
        }
    }
}
