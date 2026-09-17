using System;
using System.Buffers.Binary;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// <b>Макет, порождённый скриптом</b> (scratchpad/GenPrefix) - руками
    /// такое не пишут и глазами не вычитывают: сто двадцать веток.
    ///
    /// Пять форм конфликтной ветки диспетчера для вопроса O7 (§15 плана).
    /// Все тридцать имён PREFIX дают <b>один</b> ключ по первым семи байтам,
    /// то есть сегодняшний диспетчер вырождается здесь в цепочку из тридцати
    /// сравнений, и именно её тут и сравнивают с альтернативами:
    /// <list type="bullet">
    /// <item><c>ChainEqual</c> - как сейчас: цепочка <c>SequenceEqual</c>;</item>
    /// <item><c>ChainWords</c> - та же цепочка, но звено - два сравнения
    /// <c>ulong</c> вместо вызова: байты 0..7 и 1..8 перекрываются и покрывают
    /// имя целиком, а проверять длину не надо - корзина по ней и отобрана;</item>
    /// <item><c>WindowEqual</c> - <c>switch</c> по окну (байты 2..8), цепочки
    /// нет вовсе, но остаётся одно <c>SequenceEqual</c>;</item>
    /// <item><c>WindowWord</c> - то же окно плюс одно сравнение <c>ulong</c>
    /// по байтам 0..7. Окно доказывает 2..8, слово - 0..7, вместе это всё имя.</item>
    /// <item><c>WindowRaw</c> - окно, которое читается ОДНОЙ инструкцией:
    /// восемь сырых байт по смещению, выбранному на компиляции, без длины в
    /// старшем байте и без сборки из трёх чтений. Смещение ищет генератор
    /// (<c>PickWindow</c>), а доказать остаётся лишь то, чего окно не
    /// накрыло, - для девятибайтового имени при смещении 1 это один байт.
    /// Форма заведена после первого замера: три предыдущих проиграли не
    /// потому, что окно бесполезно, а потому, что оно было собрано дорого,
    /// и это надо было разделить.</item>
    /// </list>
    /// Сравнение после окна <b>обязательно</b> в обеих оконных формах, и это
    /// не перестраховка: окно доказывает совпадение окна, а не имени. Имя в
    /// документе произвольно, и без проверки чужое свойство было бы прочитано
    /// как своё - худший исход по §1 плана.
    /// </summary>
    public static class PrefixSerializer
    {
        public static void DeserializeChainEqual(DefaultInjector injector, ReadOnlySpan<byte> json, out Prefix? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadChainEqual(injector, json, ref position, ref context);
        }

        private static Prefix? ReadChainEqual(
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
            var result = new Prefix();

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
                    case 9:
                        if (name.SequenceEqual("Payload00"u8))
                        {
                            result.Payload00 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload01"u8))
                        {
                            result.Payload01 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload02"u8))
                        {
                            result.Payload02 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload03"u8))
                        {
                            result.Payload03 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload04"u8))
                        {
                            result.Payload04 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload05"u8))
                        {
                            result.Payload05 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload06"u8))
                        {
                            result.Payload06 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload07"u8))
                        {
                            result.Payload07 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload08"u8))
                        {
                            result.Payload08 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload09"u8))
                        {
                            result.Payload09 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload10"u8))
                        {
                            result.Payload10 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload11"u8))
                        {
                            result.Payload11 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload12"u8))
                        {
                            result.Payload12 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload13"u8))
                        {
                            result.Payload13 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload14"u8))
                        {
                            result.Payload14 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload15"u8))
                        {
                            result.Payload15 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload16"u8))
                        {
                            result.Payload16 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload17"u8))
                        {
                            result.Payload17 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload18"u8))
                        {
                            result.Payload18 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload19"u8))
                        {
                            result.Payload19 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload20"u8))
                        {
                            result.Payload20 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload21"u8))
                        {
                            result.Payload21 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload22"u8))
                        {
                            result.Payload22 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload23"u8))
                        {
                            result.Payload23 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload24"u8))
                        {
                            result.Payload24 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload25"u8))
                        {
                            result.Payload25 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload26"u8))
                        {
                            result.Payload26 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload27"u8))
                        {
                            result.Payload27 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload28"u8))
                        {
                            result.Payload28 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (name.SequenceEqual("Payload29"u8))
                        {
                            result.Payload29 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeChainWords(DefaultInjector injector, ReadOnlySpan<byte> json, out Prefix? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadChainWords(injector, json, ref position, ref context);
        }

        private static Prefix? ReadChainWords(
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
            var result = new Prefix();

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
                    case 9:
                    {
                        var head = BinaryPrimitives.ReadUInt64LittleEndian(name);
                        var tail = BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(1));

                        if (head == 0x3064616F6C796150UL && tail == 0x303064616F6C7961UL)
                        {
                            result.Payload00 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x313064616F6C7961UL)
                        {
                            result.Payload01 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x323064616F6C7961UL)
                        {
                            result.Payload02 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x333064616F6C7961UL)
                        {
                            result.Payload03 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x343064616F6C7961UL)
                        {
                            result.Payload04 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x353064616F6C7961UL)
                        {
                            result.Payload05 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x363064616F6C7961UL)
                        {
                            result.Payload06 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x373064616F6C7961UL)
                        {
                            result.Payload07 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x383064616F6C7961UL)
                        {
                            result.Payload08 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3064616F6C796150UL && tail == 0x393064616F6C7961UL)
                        {
                            result.Payload09 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x303164616F6C7961UL)
                        {
                            result.Payload10 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x313164616F6C7961UL)
                        {
                            result.Payload11 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x323164616F6C7961UL)
                        {
                            result.Payload12 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x333164616F6C7961UL)
                        {
                            result.Payload13 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x343164616F6C7961UL)
                        {
                            result.Payload14 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x353164616F6C7961UL)
                        {
                            result.Payload15 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x363164616F6C7961UL)
                        {
                            result.Payload16 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x373164616F6C7961UL)
                        {
                            result.Payload17 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x383164616F6C7961UL)
                        {
                            result.Payload18 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3164616F6C796150UL && tail == 0x393164616F6C7961UL)
                        {
                            result.Payload19 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x303264616F6C7961UL)
                        {
                            result.Payload20 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x313264616F6C7961UL)
                        {
                            result.Payload21 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x323264616F6C7961UL)
                        {
                            result.Payload22 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x333264616F6C7961UL)
                        {
                            result.Payload23 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x343264616F6C7961UL)
                        {
                            result.Payload24 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x353264616F6C7961UL)
                        {
                            result.Payload25 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x363264616F6C7961UL)
                        {
                            result.Payload26 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x373264616F6C7961UL)
                        {
                            result.Payload27 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x383264616F6C7961UL)
                        {
                            result.Payload28 = ReadInt32(injector, json, ref position, ref context);
                            goto next;
                        }

                        if (head == 0x3264616F6C796150UL && tail == 0x393264616F6C7961UL)
                        {
                            result.Payload29 = ReadInt32(injector, json, ref position, ref context);
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

        public static void DeserializeWindowEqual(DefaultInjector injector, ReadOnlySpan<byte> json, out Prefix? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWindowEqual(injector, json, ref position, ref context);
        }

        private static Prefix? ReadWindowEqual(
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
            var result = new Prefix();

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
                    case 9:
                        switch (Window(name))
                        {
                            case 0x09303064616F6C79UL: //Payload00
                                if (name.SequenceEqual("Payload00"u8))
                                {
                                    result.Payload00 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313064616F6C79UL: //Payload01
                                if (name.SequenceEqual("Payload01"u8))
                                {
                                    result.Payload01 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323064616F6C79UL: //Payload02
                                if (name.SequenceEqual("Payload02"u8))
                                {
                                    result.Payload02 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333064616F6C79UL: //Payload03
                                if (name.SequenceEqual("Payload03"u8))
                                {
                                    result.Payload03 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343064616F6C79UL: //Payload04
                                if (name.SequenceEqual("Payload04"u8))
                                {
                                    result.Payload04 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353064616F6C79UL: //Payload05
                                if (name.SequenceEqual("Payload05"u8))
                                {
                                    result.Payload05 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363064616F6C79UL: //Payload06
                                if (name.SequenceEqual("Payload06"u8))
                                {
                                    result.Payload06 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373064616F6C79UL: //Payload07
                                if (name.SequenceEqual("Payload07"u8))
                                {
                                    result.Payload07 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383064616F6C79UL: //Payload08
                                if (name.SequenceEqual("Payload08"u8))
                                {
                                    result.Payload08 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393064616F6C79UL: //Payload09
                                if (name.SequenceEqual("Payload09"u8))
                                {
                                    result.Payload09 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09303164616F6C79UL: //Payload10
                                if (name.SequenceEqual("Payload10"u8))
                                {
                                    result.Payload10 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313164616F6C79UL: //Payload11
                                if (name.SequenceEqual("Payload11"u8))
                                {
                                    result.Payload11 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323164616F6C79UL: //Payload12
                                if (name.SequenceEqual("Payload12"u8))
                                {
                                    result.Payload12 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333164616F6C79UL: //Payload13
                                if (name.SequenceEqual("Payload13"u8))
                                {
                                    result.Payload13 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343164616F6C79UL: //Payload14
                                if (name.SequenceEqual("Payload14"u8))
                                {
                                    result.Payload14 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353164616F6C79UL: //Payload15
                                if (name.SequenceEqual("Payload15"u8))
                                {
                                    result.Payload15 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363164616F6C79UL: //Payload16
                                if (name.SequenceEqual("Payload16"u8))
                                {
                                    result.Payload16 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373164616F6C79UL: //Payload17
                                if (name.SequenceEqual("Payload17"u8))
                                {
                                    result.Payload17 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383164616F6C79UL: //Payload18
                                if (name.SequenceEqual("Payload18"u8))
                                {
                                    result.Payload18 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393164616F6C79UL: //Payload19
                                if (name.SequenceEqual("Payload19"u8))
                                {
                                    result.Payload19 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09303264616F6C79UL: //Payload20
                                if (name.SequenceEqual("Payload20"u8))
                                {
                                    result.Payload20 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313264616F6C79UL: //Payload21
                                if (name.SequenceEqual("Payload21"u8))
                                {
                                    result.Payload21 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323264616F6C79UL: //Payload22
                                if (name.SequenceEqual("Payload22"u8))
                                {
                                    result.Payload22 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333264616F6C79UL: //Payload23
                                if (name.SequenceEqual("Payload23"u8))
                                {
                                    result.Payload23 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343264616F6C79UL: //Payload24
                                if (name.SequenceEqual("Payload24"u8))
                                {
                                    result.Payload24 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353264616F6C79UL: //Payload25
                                if (name.SequenceEqual("Payload25"u8))
                                {
                                    result.Payload25 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363264616F6C79UL: //Payload26
                                if (name.SequenceEqual("Payload26"u8))
                                {
                                    result.Payload26 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373264616F6C79UL: //Payload27
                                if (name.SequenceEqual("Payload27"u8))
                                {
                                    result.Payload27 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383264616F6C79UL: //Payload28
                                if (name.SequenceEqual("Payload28"u8))
                                {
                                    result.Payload28 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393264616F6C79UL: //Payload29
                                if (name.SequenceEqual("Payload29"u8))
                                {
                                    result.Payload29 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

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

        public static void DeserializeWindowWord(DefaultInjector injector, ReadOnlySpan<byte> json, out Prefix? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWindowWord(injector, json, ref position, ref context);
        }

        private static Prefix? ReadWindowWord(
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
            var result = new Prefix();

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
                    case 9:
                        switch (Window(name))
                        {
                            case 0x09303064616F6C79UL: //Payload00
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload00 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313064616F6C79UL: //Payload01
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload01 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323064616F6C79UL: //Payload02
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload02 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333064616F6C79UL: //Payload03
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload03 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343064616F6C79UL: //Payload04
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload04 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353064616F6C79UL: //Payload05
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload05 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363064616F6C79UL: //Payload06
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload06 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373064616F6C79UL: //Payload07
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload07 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383064616F6C79UL: //Payload08
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload08 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393064616F6C79UL: //Payload09
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3064616F6C796150UL)
                                {
                                    result.Payload09 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09303164616F6C79UL: //Payload10
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload10 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313164616F6C79UL: //Payload11
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload11 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323164616F6C79UL: //Payload12
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload12 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333164616F6C79UL: //Payload13
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload13 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343164616F6C79UL: //Payload14
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload14 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353164616F6C79UL: //Payload15
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload15 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363164616F6C79UL: //Payload16
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload16 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373164616F6C79UL: //Payload17
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload17 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383164616F6C79UL: //Payload18
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload18 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393164616F6C79UL: //Payload19
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3164616F6C796150UL)
                                {
                                    result.Payload19 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09303264616F6C79UL: //Payload20
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload20 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09313264616F6C79UL: //Payload21
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload21 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09323264616F6C79UL: //Payload22
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload22 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09333264616F6C79UL: //Payload23
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload23 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09343264616F6C79UL: //Payload24
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload24 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09353264616F6C79UL: //Payload25
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload25 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09363264616F6C79UL: //Payload26
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload26 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09373264616F6C79UL: //Payload27
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload27 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09383264616F6C79UL: //Payload28
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload28 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x09393264616F6C79UL: //Payload29
                                if (BinaryPrimitives.ReadUInt64LittleEndian(name) == 0x3264616F6C796150UL)
                                {
                                    result.Payload29 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

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

        public static void DeserializeWindowRaw(DefaultInjector injector, ReadOnlySpan<byte> json, out Prefix? result)
        {
            var position = 0;
            var context = new JsonParseContext(json);
            result = ReadWindowRaw(injector, json, ref position, ref context);
        }

        private static Prefix? ReadWindowRaw(
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
            var result = new Prefix();

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
                    case 9:
                        switch (BinaryPrimitives.ReadUInt64LittleEndian(name.Slice(1)))
                        {
                            case 0x303064616F6C7961UL: //Payload00
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload00 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x313064616F6C7961UL: //Payload01
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload01 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x323064616F6C7961UL: //Payload02
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload02 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x333064616F6C7961UL: //Payload03
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload03 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x343064616F6C7961UL: //Payload04
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload04 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x353064616F6C7961UL: //Payload05
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload05 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x363064616F6C7961UL: //Payload06
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload06 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x373064616F6C7961UL: //Payload07
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload07 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x383064616F6C7961UL: //Payload08
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload08 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x393064616F6C7961UL: //Payload09
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload09 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x303164616F6C7961UL: //Payload10
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload10 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x313164616F6C7961UL: //Payload11
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload11 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x323164616F6C7961UL: //Payload12
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload12 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x333164616F6C7961UL: //Payload13
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload13 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x343164616F6C7961UL: //Payload14
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload14 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x353164616F6C7961UL: //Payload15
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload15 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x363164616F6C7961UL: //Payload16
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload16 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x373164616F6C7961UL: //Payload17
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload17 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x383164616F6C7961UL: //Payload18
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload18 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x393164616F6C7961UL: //Payload19
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload19 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x303264616F6C7961UL: //Payload20
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload20 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x313264616F6C7961UL: //Payload21
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload21 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x323264616F6C7961UL: //Payload22
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload22 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x333264616F6C7961UL: //Payload23
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload23 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x343264616F6C7961UL: //Payload24
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload24 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x353264616F6C7961UL: //Payload25
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload25 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x363264616F6C7961UL: //Payload26
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload26 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x373264616F6C7961UL: //Payload27
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload27 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x383264616F6C7961UL: //Payload28
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload28 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

                            case 0x393264616F6C7961UL: //Payload29
                                if (name[0] == (byte)0x50)
                                {
                                    result.Payload29 = ReadInt32(injector, json, ref position, ref context);
                                    goto next;
                                }

                                break;

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
        /// Окно: длина плюс байты 2..8. Для имени в девять байт это хвостовые
        /// семь, и они у PREFIX различны все тридцать - в отличие от первых
        /// семи, одинаковых у всех.
        /// </summary>
        private static ulong Window(scoped ReadOnlySpan<byte> name)
        {
            return ((ulong)(byte)name.Length << 56)
                | BinaryPrimitives.ReadUInt32LittleEndian(name.Slice(2, 4))
                | ((ulong)BinaryPrimitives.ReadUInt16LittleEndian(name.Slice(6, 2)) << 32)
                | ((ulong)name[8] << 48);
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
