using System;
using System.Buffers.Binary;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// <b>Макет, порождённый скриптом</b> (scratchpad/GenPrefix).
    ///
    /// Форма под калибровку стенда и под дешёвое звено (§12.5 плана). Здесь
    /// ЧЕТЫРЕ читателя, но вопросов они решают два:
    /// <list type="bullet">
    /// <item><c>CallsA</c> и <c>CallsB</c> - цепочка вызовов
    /// <c>SequenceEqual</c>, то есть то, что эмиттер печатает сегодня;</item>
    /// <item><c>WordsA</c> и <c>WordsB</c> - то же самое, но звено - одно или
    /// два сравнения <c>ulong</c> вместо вызова.</item>
    /// </list>
    /// Половины каждой пары порождены одним и тем же кодом и совпадают до
    /// последнего байта IL. Значит разница <b>внутри</b> пары - это не код, а
    /// раскладка: выравнивание, попадание в наборы кэша команд, алиасинг
    /// предсказателя. Она и есть полоса неразличимости прогона, измеренная в
    /// самом прогоне, а не унаследованная из прошлого.
    ///
    /// Читать таблицу надо так: сперва <c>|A-B|</c> внутри каждой пары - это
    /// полоса; и только потом разницу между парами - это эффект. Если эффект
    /// не больше полосы, замер не сказал ничего, сколько бы ни было итераций.
    /// </summary>
    public static class LinkSerializer
    {
        public static void DeserializeCallsA(DefaultInjector injector, ReadOnlySpan<byte> json, out Link? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadCallsA(injector, json, ref position, ref context);
        }

        private static Link? ReadCallsA(
            DefaultInjector injector,
            scoped ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref JsonParseContext context
            )
        {
            if (JsonScan.TryReadNull(json, ref position))
            {
                return null;
            }

            JsonScan.Expect(json, ref position, JsonScan.OpenBrace);
            var result = new Link();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (name.Length)
                {
                    case 8:
                        if (name.SequenceEqual("Quantity"u8))
                        {
                            result.Quantity = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 9:
                        if (name.SequenceEqual("Reference"u8))
                        {
                            result.Reference = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 10:
                        if (name.SequenceEqual("CustomerId"u8))
                        {
                            result.CustomerId = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 11:
                        if (name.SequenceEqual("Description"u8))
                        {
                            result.Description = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 12:
                        if (name.SequenceEqual("DeliveryDate"u8))
                        {
                            result.DeliveryDate = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 13:
                        if (name.SequenceEqual("InvoiceNumber"u8))
                        {
                            result.InvoiceNumber = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 14:
                        if (name.SequenceEqual("ShippingMethod"u8))
                        {
                            result.ShippingMethod = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 15:
                        if (name.SequenceEqual("PaymentProvider"u8))
                        {
                            result.PaymentProvider = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                }

                JsonScan.SkipValue(json, ref position);

            next:
                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBrace);
            return result;
        }

        public static void DeserializeCallsB(DefaultInjector injector, ReadOnlySpan<byte> json, out Link? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadCallsB(injector, json, ref position, ref context);
        }

        private static Link? ReadCallsB(
            DefaultInjector injector,
            scoped ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref JsonParseContext context
            )
        {
            if (JsonScan.TryReadNull(json, ref position))
            {
                return null;
            }

            JsonScan.Expect(json, ref position, JsonScan.OpenBrace);
            var result = new Link();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (name.Length)
                {
                    case 8:
                        if (name.SequenceEqual("Quantity"u8))
                        {
                            result.Quantity = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 9:
                        if (name.SequenceEqual("Reference"u8))
                        {
                            result.Reference = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 10:
                        if (name.SequenceEqual("CustomerId"u8))
                        {
                            result.CustomerId = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 11:
                        if (name.SequenceEqual("Description"u8))
                        {
                            result.Description = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 12:
                        if (name.SequenceEqual("DeliveryDate"u8))
                        {
                            result.DeliveryDate = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 13:
                        if (name.SequenceEqual("InvoiceNumber"u8))
                        {
                            result.InvoiceNumber = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 14:
                        if (name.SequenceEqual("ShippingMethod"u8))
                        {
                            result.ShippingMethod = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 15:
                        if (name.SequenceEqual("PaymentProvider"u8))
                        {
                            result.PaymentProvider = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                }

                JsonScan.SkipValue(json, ref position);

            next:
                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBrace);
            return result;
        }

        public static void DeserializeWordsA(DefaultInjector injector, ReadOnlySpan<byte> json, out Link? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWordsA(injector, json, ref position, ref context);
        }

        private static Link? ReadWordsA(
            DefaultInjector injector,
            scoped ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref JsonParseContext context
            )
        {
            if (JsonScan.TryReadNull(json, ref position))
            {
                return null;
            }

            JsonScan.Expect(json, ref position, JsonScan.OpenBrace);
            var result = new Link();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (name.Length)
                {
                    case 8:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x797469746E617551UL)
                        {
                            result.Quantity = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 9:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x636E657265666552UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(1)) == 0x65636E6572656665UL)
                        {
                            result.Reference = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 10:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x72656D6F74737543UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(2)) == 0x644972656D6F7473UL)
                        {
                            result.CustomerId = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 11:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x7470697263736544UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(3)) == 0x6E6F697470697263UL)
                        {
                            result.Description = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 12:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x79726576696C6544UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(4)) == 0x6574614479726576UL)
                        {
                            result.DeliveryDate = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 13:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x4E6563696F766E49UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(5)) == 0x7265626D754E6563UL)
                        {
                            result.InvoiceNumber = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 14:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x676E697070696853UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(6)) == 0x646F6874654D676EUL)
                        {
                            result.ShippingMethod = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 15:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x50746E656D796150UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(7)) == 0x72656469766F7250UL)
                        {
                            result.PaymentProvider = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                }

                JsonScan.SkipValue(json, ref position);

            next:
                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBrace);
            return result;
        }

        public static void DeserializeWordsB(DefaultInjector injector, ReadOnlySpan<byte> json, out Link? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWordsB(injector, json, ref position, ref context);
        }

        private static Link? ReadWordsB(
            DefaultInjector injector,
            scoped ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref JsonParseContext context
            )
        {
            if (JsonScan.TryReadNull(json, ref position))
            {
                return null;
            }

            JsonScan.Expect(json, ref position, JsonScan.OpenBrace);
            var result = new Link();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (name.Length)
                {
                    case 8:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x797469746E617551UL)
                        {
                            result.Quantity = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 9:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x636E657265666552UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(1)) == 0x65636E6572656665UL)
                        {
                            result.Reference = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 10:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x72656D6F74737543UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(2)) == 0x644972656D6F7473UL)
                        {
                            result.CustomerId = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 11:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x7470697263736544UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(3)) == 0x6E6F697470697263UL)
                        {
                            result.Description = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 12:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x79726576696C6544UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(4)) == 0x6574614479726576UL)
                        {
                            result.DeliveryDate = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 13:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x4E6563696F766E49UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(5)) == 0x7265626D754E6563UL)
                        {
                            result.InvoiceNumber = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 14:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x676E697070696853UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(6)) == 0x646F6874654D676EUL)
                        {
                            result.ShippingMethod = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 15:
                        if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x50746E656D796150UL && BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(7)) == 0x72656469766F7250UL)
                        {
                            result.PaymentProvider = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                }

                JsonScan.SkipValue(json, ref position);

            next:
                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBrace);
            return result;
        }

        private static int ReadInt32(
            DefaultInjector injector,
            scoped ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref JsonParseContext context
            )
        {
            var raw = JsonScan.ReadNumberRaw(json, ref position);
            injector.Parse(ref context, raw, out int value);
            return value;
        }
    }
}
