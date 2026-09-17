using System;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// <c>JsonFeature.NamedFloatingPointLiterals</c> (§6.2 плана): три особых
    /// значения <see cref="float"/>/<see cref="double"/>, у которых нет
    /// формы числа по RFC 8259, - <c>NaN</c>, <c>PositiveInfinity</c>,
    /// <c>NegativeInfinity</c> - и их строковое представление, byte-в-byte то
    /// же, что печатает <c>System.Text.Json</c> с
    /// <c>JsonNumberHandling.AllowNamedFloatingPointLiterals</c> (пробоем
    /// подтверждено).
    ///
    /// Вынесено сюда, а не в <c>JsonValueFormatter</c>, потому что вызывает
    /// это порождённый код напрямую (он живёт в чужой сборке и видит только
    /// публичную поверхность <c>IExhauster.AppendRaw</c>/сканер, а не
    /// протected-методы конкретного sink'а) - значит тип обязан быть
    /// публичным, как и весь <c>JsonGoddess.Internal</c>.
    ///
    /// Матч на чтении - точный, регистрозависимый: пробой показал, что
    /// <c>"nan"</c>, <c>"infinity"</c>, <c>"+Infinity"</c> эталон отвергает,
    /// хотя все они выглядят как разумные варианты написания.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonNamedFloat
    {
        /// <summary>
        /// Квалификация значения для <b>записи</b>: если у <paramref name="value"/>
        /// нет обычной числовой формы, <paramref name="literal"/> - готовый
        /// UTF-8-кусок в кавычках для <c>AppendRaw</c>, и метод возвращает
        /// <c>true</c>. На конечном значении возвращает <c>false</c> - тогда
        /// печатается обычный <c>exhauster.Append(value)</c>.
        /// </summary>
        public static bool TryGetLiteral(double value, out ReadOnlySpan<byte> literal)
        {
            if (double.IsNaN(value))
            {
                literal = "\"NaN\""u8;
                return true;
            }

            if (double.IsPositiveInfinity(value))
            {
                literal = "\"Infinity\""u8;
                return true;
            }

            if (double.IsNegativeInfinity(value))
            {
                literal = "\"-Infinity\""u8;
                return true;
            }

            literal = default;
            return false;
        }

        /// <summary>То же самое для <see cref="float"/> - набор особых значений у него тот же.</summary>
        public static bool TryGetLiteral(float value, out ReadOnlySpan<byte> literal)
        {
            if (float.IsNaN(value))
            {
                literal = "\"NaN\""u8;
                return true;
            }

            if (float.IsPositiveInfinity(value))
            {
                literal = "\"Infinity\""u8;
                return true;
            }

            if (float.IsNegativeInfinity(value))
            {
                literal = "\"-Infinity\""u8;
                return true;
            }

            literal = default;
            return false;
        }

        /// <summary>
        /// Квалификация содержимого JSON-строки (без кавычек, уже
        /// разэкранированного) для <b>чтения</b>. <c>true</c> - строка была
        /// одним из трёх особых имён, и <paramref name="value"/> присвоено;
        /// <c>false</c> - строка на особое имя не похожа, и вызывающий вправе
        /// либо отказать, либо (при <c>JsonFeature.NumbersFromStrings</c>)
        /// попробовать разобрать её как обычное число.
        /// </summary>
        public static bool TryParse(ReadOnlySpan<byte> raw, out double value)
        {
            if (raw.SequenceEqual("NaN"u8))
            {
                value = double.NaN;
                return true;
            }

            if (raw.SequenceEqual("Infinity"u8))
            {
                value = double.PositiveInfinity;
                return true;
            }

            if (raw.SequenceEqual("-Infinity"u8))
            {
                value = double.NegativeInfinity;
                return true;
            }

            value = default;
            return false;
        }
    }
}
