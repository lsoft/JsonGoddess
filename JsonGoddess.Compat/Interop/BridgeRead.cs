using System;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;

namespace JsonGoddess.Compat.Interop
{
    /// <summary>
    /// Чтение значения из читателя эталона - так, как прочёл бы его сам
    /// эталон, включая отказ.
    ///
    /// <para>
    /// Помощники, а не печать проверок по месту: соответствие эталону здесь
    /// проверяется тестом на каждый метод, и проверять его надо в одном месте,
    /// а не в каждом порождённом хосте. Порождённый код от этого становится
    /// строчкой <c>result.Id = BridgeRead.Int32(ref reader);</c> - его ещё и
    /// читать можно.
    /// </para>
    ///
    /// <para>
    /// Форма отказа установлена пробой (<c>scratchpad/ReaderMsgProbe</c>):
    /// эталон на негодном значении говорит
    /// <c>The JSON value could not be converted to {тип}.</c>, где тип -
    /// <see cref="Type.ToString"/> CLR-типа, а путь и позицию дописывает сам,
    /// снаружи конвертера.
    /// </para>
    ///
    /// <para>
    /// <b>Оговорка, которую нельзя обойти.</b> Путь эталон дописывает только
    /// до входа в наш тип: <c>$.Inner</c> там, где его собственный читатель
    /// сказал бы <c>$.Inner.Id</c>. Дописать остаток нечем -
    /// <c>JsonException.Path</c> доступен только на чтение, а заданный
    /// конструктором эталон не дополняет, а оставляет как есть, теряя свой
    /// префикс (проверено пробой). Из двух неполных путей выбран тот, у
    /// которого верен префикс: он ведёт к месту, а не от него.
    /// </para>
    /// </summary>
    public static class BridgeRead
    {
        public static JsonException Fail(Type type)
        {
            return new JsonException("The JSON value could not be converted to " + type + ".");
        }

        /// <summary>
        /// Число, приехавшее строкой (<c>JsonNumberHandling.AllowReadingFromString</c>,
        /// он же веб-профиль ASP.NET Core).
        ///
        /// <para>
        /// Отдаёт сырые байты внутри кавычек, чтобы разобрать их тем же
        /// <c>Utf8Parser</c>, которым разбирается обычное число. Это не
        /// экономия, а требование совместимости: правило «разобралось целиком
        /// или не разобралось» даёт ровно тот набор принимаемых форм, который
        /// установлен пробой у эталона - <c>"42"</c> и <c>"+42"</c> да,
        /// <c>" 42"</c>, <c>"42 "</c>, <c>""</c>, <c>"0x2A"</c> нет.
        /// </para>
        ///
        /// <para>
        /// Экранированную или разрезанную строку сюда пускать нельзя - там
        /// сырые байты не равны содержимому, - и такая форма честно уходит в
        /// отказ. Числа с экранированием внутри (<c>"42"</c>) - случай,
        /// которого в жизни не бывает, а молча прочесть его иначе, чем эталон,
        /// нельзя.
        /// </para>
        /// </summary>
        private static bool TryNumericText(ref Utf8JsonReader reader, out ReadOnlySpan<byte> raw)
        {
            raw = default;

            if (reader.TokenType != JsonTokenType.String || reader.HasValueSequence)
            {
                return false;
            }

            var span = reader.ValueSpan;
            if (span.Length == 0 || span.IndexOf((byte)'\\') >= 0)
            {
                return false;
            }

            raw = span;
            return true;
        }

        private static JsonException Fail<T>()
        {
            return Fail(typeof(T));
        }

        /// <summary>
        /// Токен - <c>null</c>. Вынесено в метод, потому что спрашивается
        /// перед каждым значением, которое умеет быть <c>null</c>.
        /// </summary>
        public static bool IsNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null;
        }

        public static bool Boolean(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;

                case JsonTokenType.False:
                    return false;

                default:
                    throw Fail<bool>();
            }
        }

        public static bool? BooleanOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Boolean(ref reader);
        }

        public static sbyte SByte(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSByte(out var value))
            {
                throw Fail<sbyte>();
            }

            return value;
        }

        public static sbyte? SByteOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : SByte(ref reader);
        }

        public static byte Byte(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetByte(out var value))
            {
                throw Fail<byte>();
            }

            return value;
        }

        public static byte? ByteOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Byte(ref reader);
        }

        public static short Int16(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt16(out var value))
            {
                throw Fail<short>();
            }

            return value;
        }

        public static short? Int16OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int16(ref reader);
        }

        public static ushort UInt16(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt16(out var value))
            {
                throw Fail<ushort>();
            }

            return value;
        }

        public static ushort? UInt16OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt16(ref reader);
        }

        public static int Int32(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
            {
                throw Fail<int>();
            }

            return value;
        }

        public static int? Int32OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int32(ref reader);
        }

        public static uint UInt32(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt32(out var value))
            {
                throw Fail<uint>();
            }

            return value;
        }

        public static uint? UInt32OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt32(ref reader);
        }

        public static long Int64(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var value))
            {
                throw Fail<long>();
            }

            return value;
        }

        public static long? Int64OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int64(ref reader);
        }

        public static ulong UInt64(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetUInt64(out var value))
            {
                throw Fail<ulong>();
            }

            return value;
        }

        public static ulong? UInt64OrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt64(ref reader);
        }

        public static float Single(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out var value))
            {
                throw Fail<float>();
            }

            return value;
        }

        public static float? SingleOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Single(ref reader);
        }

        public static double Double(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value))
            {
                throw Fail<double>();
            }

            return value;
        }

        public static double? DoubleOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Double(ref reader);
        }

        public static decimal Decimal(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value))
            {
                throw Fail<decimal>();
            }

            return value;
        }

        public static decimal? DecimalOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Decimal(ref reader);
        }

        public static string? String(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.String:
                    return reader.GetString();

                default:
                    throw Fail<string>();
            }
        }

        /// <summary>
        /// Строка ровно из одного символа. Эталон читает <c>char</c> именно
        /// так, и пустая строка или строка длиннее для него отказ.
        /// </summary>
        public static char Char(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw Fail<char>();
            }

            var text = reader.GetString();
            if (text is null || text.Length != 1)
            {
                throw Fail<char>();
            }

            return text[0];
        }

        public static char? CharOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Char(ref reader);
        }

        public static DateTime DateTime(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTime(out var value))
            {
                throw Fail<DateTime>();
            }

            return value;
        }

        public static DateTime? DateTimeOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : DateTime(ref reader);
        }

        public static DateTimeOffset DateTimeOffset(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out var value))
            {
                throw Fail<DateTimeOffset>();
            }

            return value;
        }

        public static DateTimeOffset? DateTimeOffsetOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : DateTimeOffset(ref reader);
        }

        /// <summary>
        /// У читателя эталона нет <c>TryGetTimeSpan</c>: <c>TimeSpan</c>
        /// обслуживает отдельный конвертер, разбирающий строку форматом
        /// <c>"c"</c> по инвариантной культуре. Повторяем его, а не
        /// изобретаем.
        /// </summary>
        public static TimeSpan TimeSpan(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw Fail<TimeSpan>();
            }

            var text = reader.GetString();
            if (text is null
                || !System.TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var value))
            {
                throw Fail<TimeSpan>();
            }

            return value;
        }

        public static TimeSpan? TimeSpanOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : TimeSpan(ref reader);
        }

        public static Guid Guid(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var value))
            {
                throw Fail<Guid>();
            }

            return value;
        }

        public static Guid? GuidOrNull(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Guid(ref reader);
        }

        public static byte[]? ByteArray(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.String:
                    if (!reader.TryGetBytesFromBase64(out var value))
                    {
                        throw Fail<byte[]>();
                    }

                    return value;

                default:
                    throw Fail<byte[]>();
            }
        }

        /// <summary>
        /// Начало объекта, массива или <c>null</c> - три токена, на которые
        /// порождённый читатель смотрит перед тем, как войти внутрь.
        /// Проверка вынесена сюда, чтобы отказ выглядел одинаково у всех.
        /// </summary>
        public static void ExpectStartObject(ref Utf8JsonReader reader, Type type)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw Fail(type);
            }
        }

        public static void ExpectStartArray(ref Utf8JsonReader reader, Type type)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw Fail(type);
            }
        }

        /// <summary>
        /// Имя свойства годится для быстрого разбора: лежит одним куском и не
        /// содержит экранирования.
        ///
        /// <para>
        /// Оба условия настоящие, а не теоретические. Кусками имя приезжает на
        /// async-потоке, где читатель работает поверх
        /// <c>ReadOnlySequence</c>; экранированным - от всякого, кто написал
        /// <c>"Id"</c> вместо <c>"Id"</c>. В обоих случаях сравнивать
        /// сырые байты с литералом нельзя: совпадения не будет, и член молча
        /// остался бы непрочитанным. Поэтому медленный путь не оптимизация
        /// наоборот, а условие правильности.
        /// </para>
        /// </summary>
        public static bool NameIsPlain(ref Utf8JsonReader reader)
        {
            if (reader.HasValueSequence)
            {
                return false;
            }

            return reader.ValueSpan.IndexOf((byte)'\\') < 0;
        }

        // ---------- число, которое разрешено прислать строкой ----------
        //
        // Веб-профиль (JsonSerializerDefaults.Web) включает
        // JsonNumberHandling.AllowReadingFromString, и под ним генератор
        // печатает эти методы вместо строгих. Отдельные методы, а не флаг
        // внутри строгих: флаг стоил бы ветки на каждое значение у всех, ради
        // поведения, которого у большинства нет.

        public static sbyte SByteLenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out sbyte fromText, out var used) || used != raw.Length)
                {
                    throw Fail<sbyte>();
                }

                return fromText;
            }

            return SByte(ref reader);
        }

        public static sbyte? SByteOrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : SByteLenient(ref reader);
        }

        public static byte ByteLenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out byte fromText, out var used) || used != raw.Length)
                {
                    throw Fail<byte>();
                }

                return fromText;
            }

            return Byte(ref reader);
        }

        public static byte? ByteOrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : ByteLenient(ref reader);
        }

        public static short Int16Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out short fromText, out var used) || used != raw.Length)
                {
                    throw Fail<short>();
                }

                return fromText;
            }

            return Int16(ref reader);
        }

        public static short? Int16OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int16Lenient(ref reader);
        }

        public static ushort UInt16Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out ushort fromText, out var used) || used != raw.Length)
                {
                    throw Fail<ushort>();
                }

                return fromText;
            }

            return UInt16(ref reader);
        }

        public static ushort? UInt16OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt16Lenient(ref reader);
        }

        public static int Int32Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out int fromText, out var used) || used != raw.Length)
                {
                    throw Fail<int>();
                }

                return fromText;
            }

            return Int32(ref reader);
        }

        public static int? Int32OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int32Lenient(ref reader);
        }

        public static uint UInt32Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out uint fromText, out var used) || used != raw.Length)
                {
                    throw Fail<uint>();
                }

                return fromText;
            }

            return UInt32(ref reader);
        }

        public static uint? UInt32OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt32Lenient(ref reader);
        }

        public static long Int64Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out long fromText, out var used) || used != raw.Length)
                {
                    throw Fail<long>();
                }

                return fromText;
            }

            return Int64(ref reader);
        }

        public static long? Int64OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : Int64Lenient(ref reader);
        }

        public static ulong UInt64Lenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out ulong fromText, out var used) || used != raw.Length)
                {
                    throw Fail<ulong>();
                }

                return fromText;
            }

            return UInt64(ref reader);
        }

        public static ulong? UInt64OrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : UInt64Lenient(ref reader);
        }

        public static float SingleLenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out float fromText, out var used) || used != raw.Length)
                {
                    throw Fail<float>();
                }

                return fromText;
            }

            return Single(ref reader);
        }

        public static float? SingleOrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : SingleLenient(ref reader);
        }

        public static double DoubleLenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out double fromText, out var used) || used != raw.Length)
                {
                    throw Fail<double>();
                }

                return fromText;
            }

            return Double(ref reader);
        }

        public static double? DoubleOrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : DoubleLenient(ref reader);
        }

        public static decimal DecimalLenient(ref Utf8JsonReader reader)
        {
            if (TryNumericText(ref reader, out var raw))
            {
                if (!Utf8Parser.TryParse(raw, out decimal fromText, out var used) || used != raw.Length)
                {
                    throw Fail<decimal>();
                }

                return fromText;
            }

            return Decimal(ref reader);
        }

        public static decimal? DecimalOrNullLenient(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Null ? null : DecimalLenient(ref reader);
        }
    }
}
