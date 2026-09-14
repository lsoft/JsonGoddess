using System;
using System.Buffers;
using System.Buffers.Text;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// base64 в обе стороны, прямо по UTF-8. Строки здесь не появляются вовсе:
    /// <c>Convert.ToBase64String</c> на записи означал бы строку, существующую
    /// только чтобы её тут же скопировали в буфер.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonBase64
    {
        /// <summary>
        /// Сколько байт займёт закодированное значение вместе с кавычками.
        /// </summary>
        public static int GetMaxEncodedLength(int byteCount)
        {
            return Base64.GetMaxEncodedToUtf8Length(byteCount) + 2;
        }

        /// <summary>
        /// Пишет <c>"BASE64"</c> вместе с кавычками.
        /// </summary>
        public static int WriteQuoted(ReadOnlySpan<byte> value, Span<byte> destination)
        {
            destination[0] = (byte)'"';
            var status = Base64.EncodeToUtf8(value, destination.Slice(1), out _, out var written);
            if (status != OperationStatus.Done)
            {
                throw new InvalidOperationException("Destination is too small for a base64 value.");
            }

            destination[written + 1] = (byte)'"';
            return written + 2;
        }

        /// <summary>
        /// Читает содержимое строки (без кавычек) в массив точного размера.
        /// </summary>
        public static byte[] Decode(ReadOnlySpan<byte> raw)
        {
            if (raw.Length == 0)
            {
                return
#if NET8_0_OR_GREATER
                    Array.Empty<byte>();
#else
                    EmptyBytes;
#endif
            }

            var max = Base64.GetMaxDecodedFromUtf8Length(raw.Length);
            var rented = ArrayPool<byte>.Shared.Rent(max);
            try
            {
                var status = Base64.DecodeFromUtf8(raw, rented, out var consumed, out var written);
                if (status != OperationStatus.Done || consumed != raw.Length)
                {
                    throw new FormatException("The value is not a valid base64 lexeme.");
                }

                var result = new byte[written];
                Array.Copy(rented, 0, result, 0, written);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

#if !NET8_0_OR_GREATER
        private static readonly byte[] EmptyBytes = new byte[0];
#endif
    }
}
