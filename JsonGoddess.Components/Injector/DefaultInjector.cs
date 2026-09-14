using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using JsonGoddess.Internal;

namespace JsonGoddess
{
    /// <summary>
    /// Разбор скаляров по умолчанию. Синглтон: состояния нет, а лишний объект
    /// на каждый разбор - лишняя аллокация.
    ///
    /// Инвариантная культура везде и явно. Числа читаются
    /// <see cref="Utf8Parser"/> прямо из байтов; даты и промежутки - через
    /// короткий стековый буфер символов, потому что байтового парсера ISO 8601,
    /// принимающего обрезанную дробную часть, в BCL нет (свой - кандидат в
    /// оптимизации, когда бенчмарк скажет, что он нужен).
    /// </summary>
    public sealed class DefaultInjector : InjectorBase
    {
        public static readonly DefaultInjector Instance = new DefaultInjector();

        private const int TextStackThreshold = 128;

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out bool value)
        {
            if (raw.Length == 4 && raw[0] == (byte)'t' && raw[1] == (byte)'r' && raw[2] == (byte)'u' && raw[3] == (byte)'e')
            {
                value = true;
                return;
            }

            if (raw.Length == 5 && raw[0] == (byte)'f' && raw[1] == (byte)'a' && raw[2] == (byte)'l' && raw[3] == (byte)'s' && raw[4] == (byte)'e')
            {
                value = false;
                return;
            }

            throw NotA("boolean", raw);
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out sbyte value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("sbyte", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out byte value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("byte", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out short value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("short", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ushort value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("ushort", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out int value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("int", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out uint value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("uint", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out long value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("long", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ulong value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("ulong", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out float value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("float", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out double value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("double", raw);
            }
        }

        public override void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out decimal value)
        {
            if (!Utf8Parser.TryParse(raw, out value, out var consumed) || consumed != raw.Length)
            {
                throw NotA("decimal", raw);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out string value)
        {
            value = JsonStringDecoder.Decode(raw, hasEscape);
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out char value)
        {
            Span<char> buffer = stackalloc char[TextStackThreshold];
            var length = ToChars(raw, hasEscape, buffer, out var rented);
            try
            {
                var text = rented is null ? (ReadOnlySpan<char>)buffer.Slice(0, length) : new ReadOnlySpan<char>(rented, 0, length);
                if (text.Length != 1)
                {
                    throw NotA("char", raw);
                }

                value = text[0];
            }
            finally
            {
                Return(rented);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTime value)
        {
            Span<char> buffer = stackalloc char[TextStackThreshold];
            var length = ToChars(raw, hasEscape, buffer, out var rented);
            try
            {
                var text = rented is null ? (ReadOnlySpan<char>)buffer.Slice(0, length) : new ReadOnlySpan<char>(rented, 0, length);
                if (!TryParseDateTime(text, out value))
                {
                    throw NotA("DateTime", raw);
                }
            }
            finally
            {
                Return(rented);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTimeOffset value)
        {
            Span<char> buffer = stackalloc char[TextStackThreshold];
            var length = ToChars(raw, hasEscape, buffer, out var rented);
            try
            {
                var text = rented is null ? (ReadOnlySpan<char>)buffer.Slice(0, length) : new ReadOnlySpan<char>(rented, 0, length);
                if (!TryParseDateTimeOffset(text, out value))
                {
                    throw NotA("DateTimeOffset", raw);
                }
            }
            finally
            {
                Return(rented);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out TimeSpan value)
        {
            Span<char> buffer = stackalloc char[TextStackThreshold];
            var length = ToChars(raw, hasEscape, buffer, out var rented);
            try
            {
                var text = rented is null ? (ReadOnlySpan<char>)buffer.Slice(0, length) : new ReadOnlySpan<char>(rented, 0, length);
                if (!TryParseTimeSpan(text, out value))
                {
                    throw NotA("TimeSpan", raw);
                }
            }
            finally
            {
                Return(rented);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out Guid value)
        {
            if (!hasEscape)
            {
                if (!Utf8Parser.TryParse(raw, out value, out var consumed, 'D') || consumed != raw.Length)
                {
                    throw NotA("Guid", raw);
                }

                return;
            }

            Span<char> buffer = stackalloc char[TextStackThreshold];
            var length = ToChars(raw, hasEscape, buffer, out var rented);
            try
            {
                var text = rented is null ? (ReadOnlySpan<char>)buffer.Slice(0, length) : new ReadOnlySpan<char>(rented, 0, length);
                if (!TryParseGuid(text, out value))
                {
                    throw NotA("Guid", raw);
                }
            }
            finally
            {
                Return(rented);
            }
        }

        public override void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out byte[] value)
        {
            if (hasEscape)
            {
                throw NotA("base64", raw);
            }

            value = JsonBase64.Decode(raw);
        }

        /// <summary>
        /// Раскодирует содержимое строки в символы. Короткие значения - а даты,
        /// GUID'ы и промежутки все короткие - укладываются в стековый буфер;
        /// более длинное берётся из пула, и тогда <paramref name="rented"/> не
        /// null и его обязан вернуть вызывающий.
        /// </summary>
        private static int ToChars(scoped ReadOnlySpan<byte> raw, bool hasEscape, Span<char> buffer, out char[]? rented)
        {
            if (raw.Length <= buffer.Length)
            {
                rented = null;
                return JsonStringDecoder.DecodeToBuffer(raw, hasEscape, buffer);
            }

            rented = ArrayPool<char>.Shared.Rent(raw.Length);
            return JsonStringDecoder.DecodeToBuffer(raw, hasEscape, rented);
        }

        private static void Return(char[]? rented)
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        private static FormatException NotA(string typeName, scoped ReadOnlySpan<byte> raw)
        {
            return new FormatException(
                "'" + JsonStringDecoder.Decode(raw, false) + "' is not a valid " + typeName + " value."
                );
        }

#if NET8_0_OR_GREATER
        private static bool TryParseDateTime(ReadOnlySpan<char> text, out DateTime value)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }

        private static bool TryParseDateTimeOffset(ReadOnlySpan<char> text, out DateTimeOffset value)
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }

        private static bool TryParseTimeSpan(ReadOnlySpan<char> text, out TimeSpan value)
        {
            return TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseGuid(ReadOnlySpan<char> text, out Guid value)
        {
            return Guid.TryParseExact(text, "D", out value);
        }
#else
        private static bool TryParseDateTime(ReadOnlySpan<char> text, out DateTime value)
        {
            return DateTime.TryParse(text.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }

        private static bool TryParseDateTimeOffset(ReadOnlySpan<char> text, out DateTimeOffset value)
        {
            return DateTimeOffset.TryParse(text.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
        }

        private static bool TryParseTimeSpan(ReadOnlySpan<char> text, out TimeSpan value)
        {
            return TimeSpan.TryParseExact(text.ToString(), "c", CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseGuid(ReadOnlySpan<char> text, out Guid value)
        {
            return Guid.TryParseExact(text.ToString(), "D", out value);
        }
#endif
    }
}
