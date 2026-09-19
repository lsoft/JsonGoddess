using System;
using System.Collections.Generic;
using JsonGoddess;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Ручная копия того, что печатает генератор для <see cref="Order"/> в
    /// веб-профиле, - с единственной правкой: вместо отказа на нехватке данных
    /// читатель возвращает <c>false</c>.
    ///
    /// <para>
    /// Форма держится близко к порождённой намеренно: диспетчер по длине имени,
    /// скаляры отдельными методами, разбор через инжектор. Если схема окажется
    /// рабочей, эмиттеру предстоит напечатать ровно это, и расхождение формы
    /// обесценило бы замер.
    /// </para>
    ///
    /// <para>
    /// <b>Откат безвреден по построению.</b> Недобранный объект просто
    /// выбрасывается: ничего, кроме заполнения свойств нового экземпляра, здесь
    /// не происходит. Единственное состояние, переживающее откат, - арендованные
    /// буферы в <see cref="JsonParseContext"/>, и они черновые.
    /// </para>
    /// </summary>
    internal static class OrderReader
    {
        private static readonly DefaultInjector Injector = DefaultInjector.Instance;

        internal static bool Order(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out Order? value
            )
        {
            value = null;

            if (!TryScan.Null(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!TryScan.Expect(json, ref position, TryScan.OpenBrace, final))
            {
                return false;
            }

            var result = new Order();

            if (!TryScan.TryConsume(json, ref position, TryScan.CloseBrace, final, out var closed))
            {
                return false;
            }

            if (closed)
            {
                value = result;
                return true;
            }

            while (true)
            {
                if (!TryScan.String(json, ref position, final, out var name, out var nameEscaped))
                {
                    return false;
                }

                if (nameEscaped)
                {
                    name = context.UnescapeName(name, true);
                }

                if (!TryScan.Expect(json, ref position, TryScan.Colon, final))
                {
                    return false;
                }

                switch (name.Length)
                {
                    case 2:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "id"u8))
                        {
                            if (!Int32(json, ref position, ref context, final, out var id))
                            {
                                return false;
                            }

                            result.Id = id;
                            goto next;
                        }

                        break;

                    case 4:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "paid"u8))
                        {
                            if (!TryScan.Boolean(json, ref position, final, out var paid))
                            {
                                return false;
                            }

                            result.Paid = paid;
                            goto next;
                        }

                        break;

                    case 5:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "total"u8))
                        {
                            if (!Decimal(json, ref position, ref context, final, out var total))
                            {
                                return false;
                            }

                            result.Total = total;
                            goto next;
                        }

                        if (JsonAsciiName.EqualsIgnoreCase(name, "lines"u8))
                        {
                            if (!Lines(json, ref position, ref context, final, out var lines))
                            {
                                return false;
                            }

                            result.Lines = lines;
                            goto next;
                        }

                        break;

                    case 7:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "created"u8))
                        {
                            if (!DateTime(json, ref position, ref context, final, out var created))
                            {
                                return false;
                            }

                            result.Created = created;
                            goto next;
                        }

                        break;

                    case 8:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "customer"u8))
                        {
                            if (!StringOrNull(json, ref position, ref context, final, out var customer))
                            {
                                return false;
                            }

                            result.Customer = customer;
                            goto next;
                        }

                        break;

                    case 9:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "reference"u8))
                        {
                            if (!Guid(json, ref position, ref context, final, out var reference))
                            {
                                return false;
                            }

                            result.Reference = reference;
                            goto next;
                        }

                        break;
                }

                if (!Skip(json, ref position, ref context, final))
                {
                    return false;
                }

            next:
                if (!TryScan.TryConsume(json, ref position, TryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!TryScan.Expect(json, ref position, TryScan.CloseBrace, final))
            {
                return false;
            }

            value = result;
            return true;
        }

        private static bool Lines(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out List<OrderLine>? value
            )
        {
            value = null;

            if (!TryScan.Null(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!TryScan.Expect(json, ref position, TryScan.OpenBracket, final))
            {
                return false;
            }

            var items = new List<OrderLine>();

            if (!TryScan.TryConsume(json, ref position, TryScan.CloseBracket, final, out var closed))
            {
                return false;
            }

            if (closed)
            {
                value = items;
                return true;
            }

            while (true)
            {
                if (!Line(json, ref position, ref context, final, out var line))
                {
                    return false;
                }

                items.Add(line!);

                if (!TryScan.TryConsume(json, ref position, TryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!TryScan.Expect(json, ref position, TryScan.CloseBracket, final))
            {
                return false;
            }

            value = items;
            return true;
        }

        private static bool Line(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out OrderLine? value
            )
        {
            value = null;

            if (!TryScan.Null(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!TryScan.Expect(json, ref position, TryScan.OpenBrace, final))
            {
                return false;
            }

            var result = new OrderLine();

            if (!TryScan.TryConsume(json, ref position, TryScan.CloseBrace, final, out var closed))
            {
                return false;
            }

            if (closed)
            {
                value = result;
                return true;
            }

            while (true)
            {
                if (!TryScan.String(json, ref position, final, out var name, out var nameEscaped))
                {
                    return false;
                }

                if (nameEscaped)
                {
                    name = context.UnescapeName(name, true);
                }

                if (!TryScan.Expect(json, ref position, TryScan.Colon, final))
                {
                    return false;
                }

                switch (name.Length)
                {
                    case 3:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "sku"u8))
                        {
                            if (!StringOrNull(json, ref position, ref context, final, out var sku))
                            {
                                return false;
                            }

                            result.Sku = sku;
                            goto next;
                        }

                        break;

                    case 4:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "note"u8))
                        {
                            if (!StringOrNull(json, ref position, ref context, final, out var note))
                            {
                                return false;
                            }

                            result.Note = note;
                            goto next;
                        }

                        break;

                    case 5:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "price"u8))
                        {
                            if (!Decimal(json, ref position, ref context, final, out var price))
                            {
                                return false;
                            }

                            result.Price = price;
                            goto next;
                        }

                        break;

                    case 8:
                        if (JsonAsciiName.EqualsIgnoreCase(name, "quantity"u8))
                        {
                            if (!Int32(json, ref position, ref context, final, out var quantity))
                            {
                                return false;
                            }

                            result.Quantity = quantity;
                            goto next;
                        }

                        break;
                }

                if (!Skip(json, ref position, ref context, final))
                {
                    return false;
                }

            next:
                if (!TryScan.TryConsume(json, ref position, TryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!TryScan.Expect(json, ref position, TryScan.CloseBrace, final))
            {
                return false;
            }

            value = result;
            return true;
        }

        private static bool Int32(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out int value
            )
        {
            value = 0;

            if (!TryScan.Number(json, ref position, final, out var raw))
            {
                return false;
            }

            Injector.Parse(ref context, raw, out value);
            return true;
        }

        private static bool Decimal(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out decimal value
            )
        {
            value = 0m;

            if (!TryScan.Number(json, ref position, final, out var raw))
            {
                return false;
            }

            Injector.Parse(ref context, raw, out value);
            return true;
        }

        private static bool StringOrNull(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out string? value
            )
        {
            value = null;

            if (!TryScan.Null(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!TryScan.String(json, ref position, final, out var raw, out var hasEscape))
            {
                return false;
            }

            value = JsonStringDecoder.DecodeStrict(raw, hasEscape);
            return true;
        }

        private static bool DateTime(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out System.DateTime value
            )
        {
            value = default;

            if (!TryScan.String(json, ref position, final, out var raw, out var hasEscape))
            {
                return false;
            }

            Injector.ParseText(ref context, raw, hasEscape, out value);
            return true;
        }

        private static bool Guid(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out System.Guid value
            )
        {
            value = default;

            if (!TryScan.String(json, ref position, final, out var raw, out var hasEscape))
            {
                return false;
            }

            Injector.ParseText(ref context, raw, hasEscape, out value);
            return true;
        }

        /// <summary>
        /// Незнакомое свойство. Рекурсия здесь не страшна: глубину ограничивает
        /// тот же счётчик, что и у порождённого кода, а прототипу довольно
        /// простой формы.
        /// </summary>
        private static bool Skip(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final
            )
        {
            if (!TryScan.Whitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                return final;
            }

            switch (json[position])
            {
                case TryScan.OpenBrace:
                    return SkipObject(json, ref position, ref context, final);

                case TryScan.OpenBracket:
                    return SkipArray(json, ref position, ref context, final);

                case TryScan.Quote:
                    return TryScan.String(json, ref position, final, out _, out _);

                case (byte)'t':
                case (byte)'f':
                    return TryScan.Boolean(json, ref position, final, out _);

                case (byte)'n':
                    return TryScan.Null(json, ref position, final, out _);

                default:
                    return TryScan.Number(json, ref position, final, out _);
            }
        }

        private static bool SkipObject(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final
            )
        {
            if (!TryScan.Expect(json, ref position, TryScan.OpenBrace, final))
            {
                return false;
            }

            if (!TryScan.TryConsume(json, ref position, TryScan.CloseBrace, final, out var closed))
            {
                return false;
            }

            if (closed)
            {
                return true;
            }

            while (true)
            {
                if (!TryScan.String(json, ref position, final, out _, out _))
                {
                    return false;
                }

                if (!TryScan.Expect(json, ref position, TryScan.Colon, final))
                {
                    return false;
                }

                if (!Skip(json, ref position, ref context, final))
                {
                    return false;
                }

                if (!TryScan.TryConsume(json, ref position, TryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            return TryScan.Expect(json, ref position, TryScan.CloseBrace, final);
        }

        private static bool SkipArray(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final
            )
        {
            if (!TryScan.Expect(json, ref position, TryScan.OpenBracket, final))
            {
                return false;
            }

            if (!TryScan.TryConsume(json, ref position, TryScan.CloseBracket, final, out var closed))
            {
                return false;
            }

            if (closed)
            {
                return true;
            }

            while (true)
            {
                if (!Skip(json, ref position, ref context, final))
                {
                    return false;
                }

                if (!TryScan.TryConsume(json, ref position, TryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            return TryScan.Expect(json, ref position, TryScan.CloseBracket, final);
        }
    }
}
