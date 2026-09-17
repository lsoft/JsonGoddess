using System;
using System.Buffers.Binary;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// <b>Макет, порождённый скриптом</b> (scratchpad/GenPrefix).
    ///
    /// Форма под неизмеренный порог <c>KeySwitchThreshold</c> (§8.2 плана):
    /// две, четыре и восемь членов в одной корзине по длине, все имена по семь
    /// байт. При такой длине ключ <b>полон</b>, поэтому форма «по ключу»
    /// обходится вовсе без сравнения байтов - порог меряется в чистом виде,
    /// таблица переходов против цепочки.
    ///
    /// Восьмичленная корзина заодно несёт пару под вопрос O5: цепочка
    /// <c>EqualsIgnoreCase</c> против <c>switch</c> по <b>свёрнутому</b>
    /// ключу. Свёртка побайтовая с условием, как в
    /// <c>JsonAsciiName.EqualsIgnoreCase</c>: <c>b | 0x20</c> засчитывается,
    /// только если результат попал в <c>a..z</c>, - слепой <c>OR</c> превратил
    /// бы <c>_</c> в DEL и дал тихое неверное совпадение.
    /// </summary>
    public static class BucketSerializer
    {
        public static void DeserializeChain2(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket2? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadChain2(injector, json, ref position, ref context);
        }

        private static Bucket2? ReadChain2(
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
            var result = new Bucket2();

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
                        if (name.SequenceEqual("AlphaA1"u8))
                        {
                            result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaB2"u8))
                        {
                            result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeKey2(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket2? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadKey2(injector, json, ref position, ref context);
        }

        private static Bucket2? ReadKey2(
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
            var result = new Bucket2();

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
                        switch (JsonNameKey.Compute(name))
                        {
                            case 0x0731416168706C41UL: //AlphaA1 - ключ полон
                                result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0732426168706C41UL: //AlphaB2 - ключ полон
                                result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeChain4(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket4? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadChain4(injector, json, ref position, ref context);
        }

        private static Bucket4? ReadChain4(
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
            var result = new Bucket4();

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
                        if (name.SequenceEqual("AlphaA1"u8))
                        {
                            result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaB2"u8))
                        {
                            result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaC3"u8))
                        {
                            result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaD4"u8))
                        {
                            result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeKey4(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket4? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadKey4(injector, json, ref position, ref context);
        }

        private static Bucket4? ReadKey4(
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
            var result = new Bucket4();

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
                        switch (JsonNameKey.Compute(name))
                        {
                            case 0x0731416168706C41UL: //AlphaA1 - ключ полон
                                result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0732426168706C41UL: //AlphaB2 - ключ полон
                                result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0733436168706C41UL: //AlphaC3 - ключ полон
                                result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0734446168706C41UL: //AlphaD4 - ключ полон
                                result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeChain8(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket8? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadChain8(injector, json, ref position, ref context);
        }

        private static Bucket8? ReadChain8(
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
            var result = new Bucket8();

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
                        if (name.SequenceEqual("AlphaA1"u8))
                        {
                            result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaB2"u8))
                        {
                            result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaC3"u8))
                        {
                            result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaD4"u8))
                        {
                            result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaE5"u8))
                        {
                            result.AlphaE5 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaF6"u8))
                        {
                            result.AlphaF6 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaG7"u8))
                        {
                            result.AlphaG7 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("AlphaH8"u8))
                        {
                            result.AlphaH8 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeKey8(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket8? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadKey8(injector, json, ref position, ref context);
        }

        private static Bucket8? ReadKey8(
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
            var result = new Bucket8();

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
                        switch (JsonNameKey.Compute(name))
                        {
                            case 0x0731416168706C41UL: //AlphaA1 - ключ полон
                                result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0732426168706C41UL: //AlphaB2 - ключ полон
                                result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0733436168706C41UL: //AlphaC3 - ключ полон
                                result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0734446168706C41UL: //AlphaD4 - ключ полон
                                result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0735456168706C41UL: //AlphaE5 - ключ полон
                                result.AlphaE5 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0736466168706C41UL: //AlphaF6 - ключ полон
                                result.AlphaF6 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0737476168706C41UL: //AlphaG7 - ключ полон
                                result.AlphaG7 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0738486168706C41UL: //AlphaH8 - ключ полон
                                result.AlphaH8 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeFoldChain8(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket8? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadFoldChain8(injector, json, ref position, ref context);
        }

        private static Bucket8? ReadFoldChain8(
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
            var result = new Bucket8();

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
                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaA1"u8))
                        {
                            result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaB2"u8))
                        {
                            result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaC3"u8))
                        {
                            result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaD4"u8))
                        {
                            result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaE5"u8))
                        {
                            result.AlphaE5 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaF6"u8))
                        {
                            result.AlphaF6 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaG7"u8))
                        {
                            result.AlphaG7 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "AlphaH8"u8))
                        {
                            result.AlphaH8 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeFoldKey8(DefaultInjector injector, ReadOnlySpan<byte> json, out Bucket8? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadFoldKey8(injector, json, ref position, ref context);
        }

        private static Bucket8? ReadFoldKey8(
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
            var result = new Bucket8();

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
                        switch (Folded(name))
                        {
                            case 0x0731616168706C61UL: //AlphaA1 - свёрнутый ключ полон
                                result.AlphaA1 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0732626168706C61UL: //AlphaB2 - свёрнутый ключ полон
                                result.AlphaB2 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0733636168706C61UL: //AlphaC3 - свёрнутый ключ полон
                                result.AlphaC3 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0734646168706C61UL: //AlphaD4 - свёрнутый ключ полон
                                result.AlphaD4 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0735656168706C61UL: //AlphaE5 - свёрнутый ключ полон
                                result.AlphaE5 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0736666168706C61UL: //AlphaF6 - свёрнутый ключ полон
                                result.AlphaF6 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0737676168706C61UL: //AlphaG7 - свёрнутый ключ полон
                                result.AlphaG7 = ReadInt32(injector, json, ref position, ref context);
                                goto next;
                            case 0x0738686168706C61UL: //AlphaH8 - свёрнутый ключ полон
                                result.AlphaH8 = ReadInt32(injector, json, ref position, ref context);
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

        /// <summary>
        /// Ключ по свёрнутому имени. Свёртка побайтовая с условием - та же,
        /// что в <c>JsonAsciiName.EqualsIgnoreCase</c>: слепой <c>| 0x20</c>
        /// превратил бы <c>_</c> (0x5F) в DEL (0x7F), а при полном ключе
        /// сравнения после него нет, и это было бы тихое неверное совпадение.
        /// </summary>
        private static ulong Folded(scoped ReadOnlySpan<byte> name)
        {
            var key = (ulong)(byte)name.Length << 56;

            for (var i = 0; i < name.Length && i < 7; i++)
            {
                var b = name[i];
                var lower = (byte)(b | 0x20);
                key |= (ulong)(lower >= (byte)'a' && lower <= (byte)'z' ? lower : b) << (8 * i);
            }

            return key;
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
