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
    public static class WideSerializer
    {
        public static void Serialize(PooledUtf8Exhauster exhauster, Wide? value)
        {
            if (value is null)
            {
                exhauster.AppendNull();
                return;
            }

            exhauster.AppendRaw("{\"Field00\":"u8);
            exhauster.Append(value.Field00);
            exhauster.AppendRaw(",\"Field01\":"u8);
            exhauster.Append(value.Field01);
            exhauster.AppendRaw(",\"Field02\":"u8);
            exhauster.Append(value.Field02);
            exhauster.AppendRaw(",\"Field03\":"u8);
            exhauster.Append(value.Field03);
            exhauster.AppendRaw(",\"Field04\":"u8);
            exhauster.Append(value.Field04);
            exhauster.AppendRaw(",\"Field05\":"u8);
            exhauster.Append(value.Field05);
            exhauster.AppendRaw(",\"Field06\":"u8);
            exhauster.Append(value.Field06);
            exhauster.AppendRaw(",\"Field07\":"u8);
            exhauster.Append(value.Field07);
            exhauster.AppendRaw(",\"Field08\":"u8);
            exhauster.Append(value.Field08);
            exhauster.AppendRaw(",\"Field09\":"u8);
            exhauster.Append(value.Field09);
            exhauster.AppendRaw(",\"Field10\":"u8);
            exhauster.Append(value.Field10);
            exhauster.AppendRaw(",\"Field11\":"u8);
            exhauster.Append(value.Field11);
            exhauster.AppendRaw(",\"Field12\":"u8);
            exhauster.Append(value.Field12);
            exhauster.AppendRaw(",\"Field13\":"u8);
            exhauster.Append(value.Field13);
            exhauster.AppendRaw(",\"Field14\":"u8);
            exhauster.Append(value.Field14);
            exhauster.AppendRaw(",\"Field15\":"u8);
            exhauster.Append(value.Field15);
            exhauster.AppendRaw(",\"Field16\":"u8);
            exhauster.Append(value.Field16);
            exhauster.AppendRaw(",\"Field17\":"u8);
            exhauster.Append(value.Field17);
            exhauster.AppendRaw(",\"Field18\":"u8);
            exhauster.Append(value.Field18);
            exhauster.AppendRaw(",\"Field19\":"u8);
            exhauster.Append(value.Field19);
            exhauster.AppendRaw(",\"Field20\":"u8);
            exhauster.Append(value.Field20);
            exhauster.AppendRaw(",\"Field21\":"u8);
            exhauster.Append(value.Field21);
            exhauster.AppendRaw(",\"Field22\":"u8);
            exhauster.Append(value.Field22);
            exhauster.AppendRaw(",\"Field23\":"u8);
            exhauster.Append(value.Field23);
            exhauster.AppendRaw(",\"Field24\":"u8);
            exhauster.Append(value.Field24);
            exhauster.AppendRaw(",\"Field25\":"u8);
            exhauster.Append(value.Field25);
            exhauster.AppendRaw(",\"Field26\":"u8);
            exhauster.Append(value.Field26);
            exhauster.AppendRaw(",\"Field27\":"u8);
            exhauster.Append(value.Field27);
            exhauster.AppendRaw(",\"Field28\":"u8);
            exhauster.Append(value.Field28);
            exhauster.AppendRaw(",\"Field29\":"u8);
            exhauster.Append(value.Field29);
            exhauster.AppendRaw("}"u8);
        }

        public static void DeserializeByLength(DefaultInjector injector, ReadOnlySpan<byte> json, out Wide? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWideByLength(injector, json, ref position, ref context);
        }

        public static void DeserializeByKey(DefaultInjector injector, ReadOnlySpan<byte> json, out Wide? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWideByKey(injector, json, ref position, ref context);
        }

        private static Wide? ReadWideByLength(
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
            var result = new Wide();

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
                    case 7:
                        if (name.SequenceEqual("Field00"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field00 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field01"u8))
                        {
                            result.Field01 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field02"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field02 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field03"u8))
                        {
                            result.Field03 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field04"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field04 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field05"u8))
                        {
                            result.Field05 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field06"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field06 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field07"u8))
                        {
                            result.Field07 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field08"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field08 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field09"u8))
                        {
                            result.Field09 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field10"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field10 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field11"u8))
                        {
                            result.Field11 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field12"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field12 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field13"u8))
                        {
                            result.Field13 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field14"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field14 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field15"u8))
                        {
                            result.Field15 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field16"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field16 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field17"u8))
                        {
                            result.Field17 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field18"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field18 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field19"u8))
                        {
                            result.Field19 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field20"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field20 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field21"u8))
                        {
                            result.Field21 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field22"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field22 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field23"u8))
                        {
                            result.Field23 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field24"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field24 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field25"u8))
                        {
                            result.Field25 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field26"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field26 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field27"u8))
                        {
                            result.Field27 = ReadString(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Field28"u8))
                        {
                            var raw = JsonScan.ReadNumberRaw(json, ref position);
                            injector.Parse(ref context, raw, out int value);
                            result.Field28 = value;
                            goto next;
                        }

                        if (name.SequenceEqual("Field29"u8))
                        {
                            result.Field29 = ReadString(injector, json, ref position, ref context);
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

        private static Wide? ReadWideByKey(
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
            var result = new Wide();

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
                    case 0x073030646C656946UL: //Field00
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field00 = value;
                        goto next;
                    }

                    case 0x073031646C656946UL: //Field10
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field10 = value;
                        goto next;
                    }

                    case 0x073032646C656946UL: //Field20
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field20 = value;
                        goto next;
                    }

                    case 0x073130646C656946UL: //Field01
                    {
                        result.Field01 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073131646C656946UL: //Field11
                    {
                        result.Field11 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073132646C656946UL: //Field21
                    {
                        result.Field21 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073230646C656946UL: //Field02
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field02 = value;
                        goto next;
                    }

                    case 0x073231646C656946UL: //Field12
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field12 = value;
                        goto next;
                    }

                    case 0x073232646C656946UL: //Field22
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field22 = value;
                        goto next;
                    }

                    case 0x073330646C656946UL: //Field03
                    {
                        result.Field03 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073331646C656946UL: //Field13
                    {
                        result.Field13 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073332646C656946UL: //Field23
                    {
                        result.Field23 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073430646C656946UL: //Field04
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field04 = value;
                        goto next;
                    }

                    case 0x073431646C656946UL: //Field14
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field14 = value;
                        goto next;
                    }

                    case 0x073432646C656946UL: //Field24
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field24 = value;
                        goto next;
                    }

                    case 0x073530646C656946UL: //Field05
                    {
                        result.Field05 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073531646C656946UL: //Field15
                    {
                        result.Field15 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073532646C656946UL: //Field25
                    {
                        result.Field25 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073630646C656946UL: //Field06
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field06 = value;
                        goto next;
                    }

                    case 0x073631646C656946UL: //Field16
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field16 = value;
                        goto next;
                    }

                    case 0x073632646C656946UL: //Field26
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field26 = value;
                        goto next;
                    }

                    case 0x073730646C656946UL: //Field07
                    {
                        result.Field07 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073731646C656946UL: //Field17
                    {
                        result.Field17 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073732646C656946UL: //Field27
                    {
                        result.Field27 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073830646C656946UL: //Field08
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field08 = value;
                        goto next;
                    }

                    case 0x073831646C656946UL: //Field18
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field18 = value;
                        goto next;
                    }

                    case 0x073832646C656946UL: //Field28
                    {
                        var raw = JsonScan.ReadNumberRaw(json, ref position);
                        injector.Parse(ref context, raw, out int value);
                        result.Field28 = value;
                        goto next;
                    }

                    case 0x073930646C656946UL: //Field09
                    {
                        result.Field09 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073931646C656946UL: //Field19
                    {
                        result.Field19 = ReadString(injector, json, ref position, ref context);
                        goto next;
                    }

                    case 0x073932646C656946UL: //Field29
                    {
                        result.Field29 = ReadString(injector, json, ref position, ref context);
                        goto next;
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
