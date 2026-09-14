using System;
using JsonGoddess.Internal;

namespace JsonGoddess
{
    /// <summary>
    /// Общая часть всех UTF-8 sink'ов: форматирование значений. Конкретный sink
    /// отвечает только на два вопроса - "дай мне место под N байт" и "я записал
    /// M" - и больше ни на что.
    ///
    /// Разделение именно такое, потому что лексика значения (сколько знаков у
    /// double, где кавычки у Guid, как обрезается дробная часть у DateTime) -
    /// свойство формата, а не буфера. Дублировать её в каждом sink'е значило бы
    /// разъехаться в первый же месяц.
    /// </summary>
    public abstract class Utf8ExhausterBase : ExhausterBase
    {
        /// <summary>
        /// Непрерывный кусок буфера длиной не менее
        /// <paramref name="sizeHint"/> байт.
        /// </summary>
        protected abstract Span<byte> GetSpan(int sizeHint);

        /// <summary>
        /// Столько байт из последнего <see cref="GetSpan"/> действительно
        /// записано.
        /// </summary>
        protected abstract void Advance(int count);

        public sealed override void AppendRaw(ReadOnlySpan<byte> utf8)
        {
            if (utf8.Length == 0)
            {
                return;
            }

            utf8.CopyTo(GetSpan(utf8.Length));
            Advance(utf8.Length);
        }

        public sealed override void AppendNull()
        {
            Advance(JsonValueFormatter.WriteNull(GetSpan(4)));
        }

        public sealed override void Append(bool value)
        {
            Advance(JsonValueFormatter.WriteBoolean(value, GetSpan(5)));
        }

        public sealed override void Append(sbyte value)
        {
            Advance(JsonValueFormatter.Write((long)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(byte value)
        {
            Advance(JsonValueFormatter.Write((ulong)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(short value)
        {
            Advance(JsonValueFormatter.Write((long)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(ushort value)
        {
            Advance(JsonValueFormatter.Write((ulong)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(int value)
        {
            Advance(JsonValueFormatter.Write((long)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(uint value)
        {
            Advance(JsonValueFormatter.Write((ulong)value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(long value)
        {
            Advance(JsonValueFormatter.Write(value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(ulong value)
        {
            Advance(JsonValueFormatter.Write(value, GetSpan(JsonValueFormatter.MaxIntegerBytes)));
        }

        public sealed override void Append(float value)
        {
            Advance(JsonValueFormatter.Write(value, GetSpan(JsonValueFormatter.MaxRealBytes)));
        }

        public sealed override void Append(double value)
        {
            Advance(JsonValueFormatter.Write(value, GetSpan(JsonValueFormatter.MaxRealBytes)));
        }

        public sealed override void Append(decimal value)
        {
            Advance(JsonValueFormatter.Write(value, GetSpan(JsonValueFormatter.MaxRealBytes)));
        }

        public sealed override void Append(DateTime value)
        {
            Advance(JsonValueFormatter.WriteQuoted(value, GetSpan(JsonValueFormatter.MaxDateTimeBytes)));
        }

        public sealed override void Append(DateTimeOffset value)
        {
            Advance(JsonValueFormatter.WriteQuoted(value, GetSpan(JsonValueFormatter.MaxDateTimeBytes)));
        }

        public sealed override void Append(TimeSpan value)
        {
            Advance(JsonValueFormatter.WriteQuoted(value, GetSpan(JsonValueFormatter.MaxTimeSpanBytes)));
        }

        public sealed override void Append(Guid value)
        {
            Advance(JsonValueFormatter.WriteQuoted(value, GetSpan(JsonValueFormatter.MaxGuidBytes)));
        }

        public sealed override void Append(char value)
        {
            var span = GetSpan(JsonStringEncoder.MaxBytesPerEscape + 6);
            span[0] = (byte)'"';
            int written;
            if (JsonStringEncoder.NeedsEscape(value))
            {
                written = JsonStringEncoder.WriteEscape(value, span.Slice(1));
            }
            else
            {
                //одиночный char может быть половиной суррогатной пары; тогда
                //транскодирование даст U+FFFD, и это ровно то, что делает
                //System.Text.Json - непарный суррогат не UTF-8-представим
                Span<char> one = stackalloc char[1];
                one[0] = value;
                written = JsonStringEncoder.Transcode(one, span.Slice(1));
            }

            span[written + 1] = (byte)'"';
            Advance(written + 2);
        }

        public sealed override void Append(string? value)
        {
            if (value is null)
            {
                AppendNull();
                return;
            }

            AppendText(value.AsSpan());
        }

        public sealed override void AppendBase64(byte[]? value)
        {
            if (value is null)
            {
                AppendNull();
                return;
            }

            var span = GetSpan(JsonBase64.GetMaxEncodedLength(value.Length));
            Advance(JsonBase64.WriteQuoted(value, span));
        }

        /// <summary>
        /// Строка в кавычках. Быстрый путь - ни одного экранируемого символа,
        /// одно транскодирование на всю строку. Медленный идёт прогонами между
        /// экранируемыми символами, и всё равно без промежуточной строки.
        /// </summary>
        private void AppendText(ReadOnlySpan<char> text)
        {
            //3 байта на символ - потолок UTF-8 для любого char (суррогатная пара
            //это два char и четыре байта, то есть два на символ), плюс кавычки
            var span = GetSpan(text.Length * 3 + 2);
            span[0] = (byte)'"';
            var written = 1;

            var rest = text;
            while (true)
            {
                var escapable = JsonStringEncoder.IndexOfEscapable(rest);
                if (escapable < 0)
                {
                    written += JsonStringEncoder.Transcode(rest, span.Slice(written));
                    break;
                }

                if (escapable > 0)
                {
                    written += JsonStringEncoder.Transcode(rest.Slice(0, escapable), span.Slice(written));
                }

                //Экранирование раздувает: символ превращается максимум в шесть
                //байт, а начальный бюджет считался по трём, и на каждом
                //экранированном символе запас проседает. Поэтому проверяется не
                //место под один escape, а место под худший случай всего
                //остатка - тогда рост случается максимум один раз за строку, а
                //на строках без escape этот код не исполняется вовсе.
                var worstCaseRest = (rest.Length - escapable) * JsonStringEncoder.MaxBytesPerEscape + 1;
                if (span.Length - written < worstCaseRest)
                {
                    Advance(written);
                    span = GetSpan(worstCaseRest);
                    written = 0;
                }

                written += JsonStringEncoder.WriteEscape(rest[escapable], span.Slice(written));
                rest = rest.Slice(escapable + 1);
            }

            span[written] = (byte)'"';
            Advance(written + 1);
        }
    }
}
