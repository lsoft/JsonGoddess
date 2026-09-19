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

            if (!JsonTryScan.TryReadNull(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.OpenBrace, final))
            {
                return false;
            }

            var result = new Order();

            if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.CloseBrace, final, out var closed))
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
                if (!Property(json, ref position, result, ref context, final))
                {
                    return false;
                }

                if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.CloseBrace, final))
            {
                return false;
            }

            value = result;
            return true;
        }

        /// <summary>
        /// Одно свойство: имя, двоеточие, значение - и всё это либо влезло,
        /// либо нет.
        ///
        /// <para>
        /// Вынесено из тела объекта не ради красоты. Это <b>единица
        /// переигрывания</b> на уровень ниже элемента: корневой объект нельзя
        /// выбросить и перечитать целиком - он и есть весь документ, - а
        /// свойство можно. Драйвер одиночного объекта (<c>Driver.ReadOne</c>)
        /// зовёт именно этот метод, а тело объекта внутри массива - тот же
        /// метод в цикле.
        /// </para>
        /// </summary>
        internal static bool Property(
            ReadOnlySpan<byte> json,
            ref int position,
            Order result,
            ref JsonParseContext context,
            bool final
            )
        {
            return Name(json, ref position, ref context, final, out var which)
                && Value(json, ref position, result, ref context, final, which);
        }

        /// <summary>
        /// Какое свойство прочитано. Отдельно от значения - ради драйвера
        /// одиночного объекта: узнав <see cref="Member.Lines"/>, он берёт
        /// разбор массива на себя, чтобы единицей переигрывания стал элемент,
        /// а не всё свойство целиком.
        /// </summary>
        internal enum Member
        {
            Unknown = 0,
            Id = 1,
            Customer = 2,
            Created = 3,
            Total = 4,
            Paid = 5,
            Reference = 6,
            Lines = 7,
        }

        internal static bool Name(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out Member which
            )
        {
            which = Member.Unknown;

            if (!JsonTryScan.ReadStringContent(json, ref position, final, out var name, out var nameEscaped))
            {
                return false;
            }

            if (nameEscaped)
            {
                name = context.UnescapeName(name, true);
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.Colon, final))
            {
                return false;
            }

            which = Which(name);
            return true;
        }

        private static Member Which(ReadOnlySpan<byte> name)
        {
            switch (name.Length)
            {
                case 2:
                    return JsonAsciiName.EqualsIgnoreCase(name, "id"u8) ? Member.Id : Member.Unknown;

                case 4:
                    return JsonAsciiName.EqualsIgnoreCase(name, "paid"u8) ? Member.Paid : Member.Unknown;

                case 5:
                    if (JsonAsciiName.EqualsIgnoreCase(name, "total"u8))
                    {
                        return Member.Total;
                    }

                    return JsonAsciiName.EqualsIgnoreCase(name, "lines"u8) ? Member.Lines : Member.Unknown;

                case 7:
                    return JsonAsciiName.EqualsIgnoreCase(name, "created"u8) ? Member.Created : Member.Unknown;

                case 8:
                    return JsonAsciiName.EqualsIgnoreCase(name, "customer"u8) ? Member.Customer : Member.Unknown;

                case 9:
                    return JsonAsciiName.EqualsIgnoreCase(name, "reference"u8) ? Member.Reference : Member.Unknown;

                default:
                    return Member.Unknown;
            }
        }

        internal static bool Value(
            ReadOnlySpan<byte> json,
            ref int position,
            Order result,
            ref JsonParseContext context,
            bool final,
            Member which
            )
        {
            switch (which)
            {
                case Member.Id:
                    if (!Int32(json, ref position, ref context, final, out var id))
                    {
                        return false;
                    }

                    result.Id = id;
                    return true;

                case Member.Paid:
                    if (!JsonTryScan.ReadBoolean(json, ref position, final, out var paid))
                    {
                        return false;
                    }

                    result.Paid = paid;
                    return true;

                case Member.Total:
                    if (!Decimal(json, ref position, ref context, final, out var total))
                    {
                        return false;
                    }

                    result.Total = total;
                    return true;

                case Member.Lines:
                    if (!Lines(json, ref position, ref context, final, out var lines))
                    {
                        return false;
                    }

                    result.Lines = lines;
                    return true;

                case Member.Created:
                    if (!DateTime(json, ref position, ref context, final, out var created))
                    {
                        return false;
                    }

                    result.Created = created;
                    return true;

                case Member.Customer:
                    if (!StringOrNull(json, ref position, ref context, final, out var customer))
                    {
                        return false;
                    }

                    result.Customer = customer;
                    return true;

                case Member.Reference:
                    if (!Guid(json, ref position, ref context, final, out var reference))
                    {
                        return false;
                    }

                    result.Reference = reference;
                    return true;

                default:
                    return JsonTryScan.SkipValue(json, ref position, final);
            }
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

            if (!JsonTryScan.TryReadNull(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.OpenBracket, final))
            {
                return false;
            }

            var items = new List<OrderLine>();

            if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.CloseBracket, final, out var closed))
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

                if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.CloseBracket, final))
            {
                return false;
            }

            value = items;
            return true;
        }

        internal static bool Line(
            ReadOnlySpan<byte> json,
            ref int position,
            ref JsonParseContext context,
            bool final,
            out OrderLine? value
            )
        {
            value = null;

            if (!JsonTryScan.TryReadNull(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.OpenBrace, final))
            {
                return false;
            }

            var result = new OrderLine();

            if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.CloseBrace, final, out var closed))
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
                if (!JsonTryScan.ReadStringContent(json, ref position, final, out var name, out var nameEscaped))
                {
                    return false;
                }

                if (nameEscaped)
                {
                    name = context.UnescapeName(name, true);
                }

                if (!JsonTryScan.Expect(json, ref position, JsonTryScan.Colon, final))
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

                if (!JsonTryScan.SkipValue(json, ref position, final))
                {
                    return false;
                }

            next:
                if (!JsonTryScan.TryConsume(json, ref position, JsonTryScan.Comma, final, out var more))
                {
                    return false;
                }

                if (!more)
                {
                    break;
                }
            }

            if (!JsonTryScan.Expect(json, ref position, JsonTryScan.CloseBrace, final))
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

            if (!JsonTryScan.ReadNumberRawStrict(json, ref position, final, out var raw))
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

            if (!JsonTryScan.ReadNumberRawStrict(json, ref position, final, out var raw))
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

            if (!JsonTryScan.TryReadNull(json, ref position, final, out var isNull))
            {
                return false;
            }

            if (isNull)
            {
                return true;
            }

            if (!JsonTryScan.ReadStringContent(json, ref position, final, out var raw, out var hasEscape))
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

            if (!JsonTryScan.ReadStringContent(json, ref position, final, out var raw, out var hasEscape))
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

            if (!JsonTryScan.ReadStringContent(json, ref position, final, out var raw, out var hasEscape))
            {
                return false;
            }

            Injector.ParseText(ref context, raw, hasEscape, out value);
            return true;
        }
    }
}
