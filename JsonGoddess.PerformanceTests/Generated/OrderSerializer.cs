using System;
using System.Collections.Generic;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// <b>Макет сгенерированного кода, порождённый скриптом.</b>
    ///
    /// Смысл в том, чтобы отвечать на вопросы о форме порождаемого кода до
    /// того, как написан эмиттер: генератор порождает код, рукописный
    /// эквивалент того же кода меряет ровно то же самое. Поэтому здесь нельзя
    /// ничего, чего не сможет эмиттер - ни замыканий, ни словарей, ни
    /// рефлексии.
    ///
    /// Диспетчер имён присутствует в <b>двух</b> формах, чтобы их можно было
    /// сравнить в одном процессе. Сравнивать формы между процессами нельзя:
    /// абсолютное время уезжает на проценты само по себе.
    /// <list type="bullet">
    /// <item><c>ByLength</c> - switch по длине имени, внутри цепочка
    /// SequenceEqual;</item>
    /// <item><c>ByKey</c> - switch по <see cref="JsonNameKey"/>; для имени до
    /// семи байт сравнения нет вовсе.</item>
    /// </list>
    /// Сериализация от формы не зависит и существует в одном экземпляре.
    /// </summary>
    public static class OrderSerializer
    {
        public static void Serialize(PooledUtf8Exhauster exhauster, Order? value)
        {
            if (value is null)
            {
                exhauster.AppendNull();
                return;
            }

            exhauster.AppendRaw("{\"Id\":"u8);
            exhauster.Append(value.Id);
            exhauster.AppendRaw(",\"Customer\":"u8);
            exhauster.Append(value.Customer);
            exhauster.AppendRaw(",\"Created\":"u8);
            exhauster.Append(value.Created);
            exhauster.AppendRaw(",\"Total\":"u8);
            exhauster.Append(value.Total);
            exhauster.AppendRaw(",\"Paid\":"u8);
            exhauster.Append(value.Paid);
            exhauster.AppendRaw(",\"Reference\":"u8);
            exhauster.Append(value.Reference);
            exhauster.AppendRaw(",\"Lines\":"u8);

            var lines = value.Lines;
            if (lines is null)
            {
                exhauster.AppendNull();
            }
            else
            {
                exhauster.AppendRaw("["u8);
                for (var i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        exhauster.AppendRaw(","u8);
                    }

                    SerializeLine(exhauster, lines[i]);
                }

                exhauster.AppendRaw("]"u8);
            }

            exhauster.AppendRaw("}"u8);
        }

        private static void SerializeLine(PooledUtf8Exhauster exhauster, OrderLine? value)
        {
            if (value is null)
            {
                exhauster.AppendNull();
                return;
            }

            exhauster.AppendRaw("{\"Sku\":"u8);
            exhauster.Append(value.Sku);
            exhauster.AppendRaw(",\"Quantity\":"u8);
            exhauster.Append(value.Quantity);
            exhauster.AppendRaw(",\"Price\":"u8);
            exhauster.Append(value.Price);
            exhauster.AppendRaw(",\"Note\":"u8);
            exhauster.Append(value.Note);
            exhauster.AppendRaw("}"u8);
        }

        public static void DeserializeByLength(DefaultInjector injector, ReadOnlySpan<byte> json, out Order? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadOrderByLength(injector, json, ref position, ref context);
        }

        public static void DeserializeByKey(DefaultInjector injector, ReadOnlySpan<byte> json, out Order? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadOrderByKey(injector, json, ref position, ref context);
        }

        private static Order? ReadOrderByLength(
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
            var result = new Order();

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
                    case 2:
                        if (name.SequenceEqual("Id"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Id = value;
                            goto next;
                        }

                        break;

                    case 4:
                        if (name.SequenceEqual("Paid"u8))
                        {
                            var raw = JsonScan.ReadLiteralRaw(json, ref position);
                            injector.Parse(ref context, raw, out bool value);
                            result.Paid = value;
                            goto next;
                        }

                        break;

                    case 5:
                        if (name.SequenceEqual("Total"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out decimal value);
                            result.Total = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Lines"u8))
                        {
                            result.Lines = ReadLinesByLength(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 7:
                        if (name.SequenceEqual("Created"u8))
                        {
                            var raw = JsonScan.ReadStringContent(json, ref position, out var escaped);
                            injector.ParseText(ref context, raw, escaped, out DateTime value);
                            result.Created = value;
                            goto next;
                        }

                        break;

                    case 8:
                        if (name.SequenceEqual("Customer"u8))
                        {
                            result.Customer = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 9:
                        if (name.SequenceEqual("Reference"u8))
                        {
                            var raw = JsonScan.ReadStringContent(json, ref position, out var escaped);
                            injector.ParseText(ref context, raw, escaped, out Guid value);
                            result.Reference = value;
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

        private static OrderLine? ReadOrderLineByLength(
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
            var result = new OrderLine();

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
                    case 3:
                        if (name.SequenceEqual("Sku"u8))
                        {
                            result.Sku = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 4:
                        if (name.SequenceEqual("Note"u8))
                        {
                            result.Note = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;

                    case 5:
                        if (name.SequenceEqual("Price"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out decimal value);
                            result.Price = value;
                            goto next;
                        }

                        break;

                    case 8:
                        if (name.SequenceEqual("Quantity"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Quantity = value;
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

        private static List<OrderLine>? ReadLinesByLength(
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

            JsonScan.Expect(json, ref position, JsonScan.OpenBracket);
            var result = new List<OrderLine>();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBracket))
            {
                return result;
            }

            while (true)
            {
                result.Add(ReadOrderLineByLength(injector, json, ref position, ref context)!);

                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBracket);
            return result;
        }

        private static Order? ReadOrderByKey(
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
            var result = new Order();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (JsonNameKey.Compute(name))
                {
                    case 0x0200000000006449UL: //Id
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Id = value;
                        goto next;
                    }

                    case 0x0400000064696150UL: //Paid
                    {
                        var raw = JsonScan.ReadLiteralRaw(json, ref position);
                        injector.Parse(ref context, raw, out bool value);
                        result.Paid = value;
                        goto next;
                    }

                    case 0x0500006C61746F54UL: //Total
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out decimal value);
                        result.Total = value;
                        goto next;
                    }

                    case 0x05000073656E694CUL: //Lines
                    {
                        result.Lines = ReadLinesByKey(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x0764657461657243UL: //Created
                    {
                        var raw = JsonScan.ReadStringContent(json, ref position, out var escaped);
                        injector.ParseText(ref context, raw, escaped, out DateTime value);
                        result.Created = value;
                        goto next;
                    }

                    case 0x08656D6F74737543UL: //Customer - префильтр
                    {
                        if (name.SequenceEqual("Customer"u8))
                        {
                            result.Customer = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        break;
                    }

                    case 0x096E657265666552UL: //Reference - префильтр
                    {
                        if (name.SequenceEqual("Reference"u8))
                        {
                            var raw = JsonScan.ReadStringContent(json, ref position, out var escaped);
                            injector.ParseText(ref context, raw, escaped, out Guid value);
                            result.Reference = value;
                            goto next;
                        }

                        break;
                    }

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

        private static OrderLine? ReadOrderLineByKey(
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
            var result = new OrderLine();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBrace))
            {
                return result;
            }

            while (true)
            {
                var name = JsonScan.ReadStringContent(json, ref position, out _);
                JsonScan.Expect(json, ref position, JsonScan.Colon);

                switch (JsonNameKey.Compute(name))
                {
                    case 0x0300000000756B53UL: //Sku
                    {
                        result.Sku = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x0400000065746F4EUL: //Note
                    {
                        result.Note = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x0500006563697250UL: //Price
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out decimal value);
                        result.Price = value;
                        goto next;
                    }

                    case 0x087469746E617551UL: //Quantity - префильтр
                    {
                        if (name.SequenceEqual("Quantity"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Quantity = value;
                            goto next;
                        }

                        break;
                    }

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

        private static List<OrderLine>? ReadLinesByKey(
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

            JsonScan.Expect(json, ref position, JsonScan.OpenBracket);
            var result = new List<OrderLine>();

            if (JsonScan.TryConsume(json, ref position, JsonScan.CloseBracket))
            {
                return result;
            }

            while (true)
            {
                result.Add(ReadOrderLineByKey(injector, json, ref position, ref context)!);

                if (!JsonScan.TryConsume(json, ref position, JsonScan.Comma))
                {
                    break;
                }
            }

            JsonScan.Expect(json, ref position, JsonScan.CloseBracket);
            return result;
        }

        private static string? ReadString(
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

            var raw = JsonScan.ReadStringContent(json, ref position, out var escaped);
            injector.ParseText(ref context, raw, escaped, out string value);
            return value;
        }
    }
}
