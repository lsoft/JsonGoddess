using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Лексическая форма скалярных значений, прямо в UTF-8.
    ///
    /// Везде инвариантная культура - явно, а не по умолчанию потока: у JSON
    /// лексическое пространство фиксировано, и десятичная запятая турецкой
    /// локали в нём не значит ничего хорошего.
    ///
    /// Почти всё делает <see cref="Utf8Formatter"/>, который есть на всех
    /// таргетах и пишет байты без промежуточной строки. Исключение -
    /// <see cref="float"/>/<see cref="double"/>: кратчайшее round-trip
    /// представление на net8+ даёт <c>TryFormat</c> в байты, а на
    /// netstandard2.0 приходится идти через <c>ToString("R")</c>. Это одна из
    /// тех #else-веток, ради исполнения которых тесты гоняются под net472.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonValueFormatter
    {
        /// <summary>Запас под любое целое со знаком.</summary>
        public const int MaxIntegerBytes = 24;

        /// <summary>Запас под double/float/decimal.</summary>
        public const int MaxRealBytes = 40;

        /// <summary>Запас под "yyyy-MM-ddTHH:mm:ss.fffffff+hh:mm" в кавычках.</summary>
        public const int MaxDateTimeBytes = 40;

        /// <summary>Запас под "-10675199.02:48:05.4775808" в кавычках.</summary>
        public const int MaxTimeSpanBytes = 32;

        /// <summary>Запас под GUID формата D в кавычках.</summary>
        public const int MaxGuidBytes = 40;

        private static readonly StandardFormat RoundTripFormat = new StandardFormat('O');
        private static readonly StandardFormat TimeSpanFormat = new StandardFormat('c');

        public static int WriteBoolean(bool value, Span<byte> destination)
        {
            if (value)
            {
                destination[0] = (byte)'t';
                destination[1] = (byte)'r';
                destination[2] = (byte)'u';
                destination[3] = (byte)'e';
                return 4;
            }

            destination[0] = (byte)'f';
            destination[1] = (byte)'a';
            destination[2] = (byte)'l';
            destination[3] = (byte)'s';
            destination[4] = (byte)'e';
            return 5;
        }

        public static int WriteNull(Span<byte> destination)
        {
            destination[0] = (byte)'n';
            destination[1] = (byte)'u';
            destination[2] = (byte)'l';
            destination[3] = (byte)'l';
            return 4;
        }

        public static int Write(long value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var written))
            {
                throw new InvalidOperationException("Destination is too small for an integer.");
            }

            return written;
        }

        public static int Write(ulong value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var written))
            {
                throw new InvalidOperationException("Destination is too small for an integer.");
            }

            return written;
        }

        public static int Write(decimal value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var written))
            {
                throw new InvalidOperationException("Destination is too small for a decimal.");
            }

            return written;
        }

        public static int Write(double value, Span<byte> destination)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException(
                    "'" + value.ToString(CultureInfo.InvariantCulture) + "' has no JSON number form. RFC 8259 has no NaN and no Infinity."
                    );
            }

#if NET8_0_OR_GREATER
            if (!value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("Destination is too small for a double.");
            }

            return written;
#else
            //G17, а не R. "R" на .NET Framework - тот самый сломанный "R",
            //который иногда не переживает round-trip: double.Epsilon уходит как
            //4.94065645841247E-324 и читается уже другим числом. G17
            //round-trip гарантирует всегда, ценой более длинного текста
            //(12345.678900000001 вместо 12345.6789). System.Text.Json на
            //нетфреймворке делает ровно это же - снято прогоном, а значит
            //вывод совпадает с эталоном и здесь
            return WriteAscii(value.ToString("G17", CultureInfo.InvariantCulture), destination);
#endif
        }

        public static int Write(float value, Span<byte> destination)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException(
                    "'" + value.ToString(CultureInfo.InvariantCulture) + "' has no JSON number form. RFC 8259 has no NaN and no Infinity."
                    );
            }

#if NET8_0_OR_GREATER
            if (!value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("Destination is too small for a float.");
            }

            return written;
#else
            //G9 для float по той же причине, что G17 для double
            return WriteAscii(value.ToString("G9", CultureInfo.InvariantCulture), destination);
#endif
        }

        /// <summary>
        /// GUID в кавычках, формат D (без скобок) - как пишет
        /// System.Text.Json.
        /// </summary>
        public static int WriteQuoted(Guid value, Span<byte> destination)
        {
            destination[0] = (byte)'"';
            if (!Utf8Formatter.TryFormat(value, destination.Slice(1), out var written))
            {
                throw new InvalidOperationException("Destination is too small for a Guid.");
            }

            destination[written + 1] = (byte)'"';
            return written + 2;
        }

        /// <summary>
        /// DateTime в кавычках по ISO 8601. Формат 'O' даёт семь знаков дробной
        /// части всегда; хвостовые нули и осиротевшая точка снимаются, потому
        /// что так пишет System.Text.Json: <c>2020-01-02T03:04:05</c>, а не
        /// <c>2020-01-02T03:04:05.0000000</c>.
        /// </summary>
        public static int WriteQuoted(DateTime value, Span<byte> destination)
        {
            destination[0] = (byte)'"';
            if (!Utf8Formatter.TryFormat(value, destination.Slice(1), out var written, RoundTripFormat))
            {
                throw new InvalidOperationException("Destination is too small for a DateTime.");
            }

            written = TrimFraction(destination.Slice(1, written));
            destination[written + 1] = (byte)'"';
            return written + 2;
        }

        public static int WriteQuoted(DateTimeOffset value, Span<byte> destination)
        {
            destination[0] = (byte)'"';
            if (!Utf8Formatter.TryFormat(value, destination.Slice(1), out var written, RoundTripFormat))
            {
                throw new InvalidOperationException("Destination is too small for a DateTimeOffset.");
            }

            written = TrimFraction(destination.Slice(1, written));
            destination[written + 1] = (byte)'"';
            return written + 2;
        }

        public static int WriteQuoted(TimeSpan value, Span<byte> destination)
        {
            destination[0] = (byte)'"';
            if (!Utf8Formatter.TryFormat(value, destination.Slice(1), out var written, TimeSpanFormat))
            {
                throw new InvalidOperationException("Destination is too small for a TimeSpan.");
            }

            destination[written + 1] = (byte)'"';
            return written + 2;
        }

        /// <summary>
        /// Снимает хвостовые нули дробной части и саму точку, если после
        /// снятия ничего не осталось. Суффикс ('Z' либо смещение) остаётся на
        /// месте - он идёт после дробной части и переезжает влево вместе с ней.
        /// </summary>
        private static int TrimFraction(Span<byte> text)
        {
            //дробная часть у формата 'O' стоит сразу после секунд и ровно на
            //19-й позиции: yyyy-MM-ddTHH:mm:ss
            const int DotIndex = 19;
            const int FractionLength = 7;

            if (text.Length < DotIndex + 1 + FractionLength || text[DotIndex] != (byte)'.')
            {
                return text.Length;
            }

            var lastSignificant = DotIndex + FractionLength; //индекс последней цифры дроби
            while (lastSignificant > DotIndex && text[lastSignificant] == (byte)'0')
            {
                lastSignificant--;
            }

            //lastSignificant == DotIndex означает "вся дробь нулевая", тогда
            //уходит и точка
            var keep = lastSignificant == DotIndex ? DotIndex : lastSignificant + 1;
            var suffixLength = text.Length - (DotIndex + 1 + FractionLength);
            if (suffixLength > 0)
            {
                text.Slice(DotIndex + 1 + FractionLength, suffixLength).CopyTo(text.Slice(keep));
            }

            return keep + suffixLength;
        }

#if !NET8_0_OR_GREATER
        private static int WriteAscii(string text, Span<byte> destination)
        {
            if (text.Length > destination.Length)
            {
                throw new InvalidOperationException("Destination is too small.");
            }

            for (var i = 0; i < text.Length; i++)
            {
                destination[i] = (byte)text[i];
            }

            return text.Length;
        }
#endif
    }
}
