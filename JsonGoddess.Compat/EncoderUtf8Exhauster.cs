using System;
using System.Buffers;
using System.Text.Encodings.Web;
using JsonGoddess.Internal;

namespace JsonGoddess.Compat
{
    /// <summary>
    /// Раковина, которая экранирует строки <b>тем самым энкодером</b>, что
    /// лежит в <c>JsonSerializerOptions</c>, - а не своей копией его правил.
    ///
    /// <para>
    /// Нужна веб-профилю моста (PLAN.md §10). ASP.NET Core пишет ответ
    /// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c>, и пока раковина
    /// умела только умолчательный набор, мост на записи отступал - то есть
    /// молчал ровно там, ради чего строился.
    /// </para>
    ///
    /// <para>
    /// <b>Почему делегируем, а не повторяем.</b> Набор релаксированного
    /// энкодера снят пробой, и он не описывается правилом: экранируются
    /// управляющие, <c>"</c>, <c>\</c>, U+007F-U+00A0 <b>и все незанятые
    /// кодовые точки Unicode</b> - семь тысяч девятьсот восемнадцать символов
    /// в одном только BMP. Это таблица рантайма, а не логика; она разъедется с
    /// нашей копией на первом же обновлении Unicode, и разъедется молча -
    /// валидным документом, отличающимся от эталонного. Единственный способ не
    /// разойтись - спрашивать тот же объект, которым пишет эталон.
    /// <see cref="CompatUtf8Exhauster"/> при этом остаётся как есть: его набор
    /// зафиксирован, измерен и обслуживает профиль по умолчанию.
    /// </para>
    ///
    /// <para>
    /// <b>Непарный суррогат разбирается до транскодирования, и это не
    /// придирка.</b> Эталон печатает его экранированным (<c>�</c>), а
    /// настоящий U+FFFD - сырым; байты у них после транскодирования одни и те
    /// же, и различить их потом уже нечем. Проверено пробой: <c>"\uD800"</c>
    /// даёт <c>"�"</c>, а <c>"�"</c> даёт <c>"�"</c>.
    /// </para>
    ///
    /// <para>
    /// Совпадение с эталоном проверено перебором: весь BMP поодиночке и в
    /// тексте, астральные плоскости с шагом, все комбинации суррогатов - на
    /// обоих энкодерах, расхождений ноль (<c>CompatRelaxedEscapingFixture</c>).
    /// </para>
    ///
    /// <para>
    /// Не потокобезопасна - как и все раковины.
    /// </para>
    /// </summary>
    public sealed class EncoderUtf8Exhauster : PooledUtf8ExhausterBase
    {
        /// <summary>
        /// Потолок раздувания на один байт источника: ASCII-байт может стать
        /// <c>\u00XX</c>, то есть шестью. Последовательность из четырёх байт
        /// даёт суррогатную пару - двенадцать байт, то есть три на байт;
        /// худший случай всё равно здесь.
        /// </summary>
        private const int MaxBytesPerSourceByte = 6;

        private JavaScriptEncoder _encoder;

        public EncoderUtf8Exhauster()
            : this(JavaScriptEncoder.Default, DefaultCapacity)
        {
        }

        public EncoderUtf8Exhauster(JavaScriptEncoder encoder)
            : this(encoder, DefaultCapacity)
        {
        }

        public EncoderUtf8Exhauster(JavaScriptEncoder encoder, int capacity)
            : base(capacity)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        }

        /// <summary>
        /// Сбросить записанное и взять энкодер на следующий документ.
        ///
        /// <para>
        /// Энкодер приходит с каждым вызовом, а не задаётся однажды, потому
        /// что раковина живёт на потоке, а опции - у вызывающего: два
        /// приложения в одном процессе вправе писать разными энкодерами, и
        /// запомненный был бы враньём про одно из них.
        /// </para>
        /// </summary>
        public void Reset(JavaScriptEncoder encoder)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            Reset();
        }

        public override void Append(string? value)
        {
            if (value is null)
            {
                AppendNull();
                return;
            }

            AppendText(value.AsSpan());
        }

        public override void Append(char value)
        {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            AppendText(one);
        }

        private void AppendText(ReadOnlySpan<char> text)
        {
            if (!HasLoneSurrogate(text))
            {
                //Общий случай, и он же быстрый: одна кавычка, одно
                //транскодирование, один векторный поиск внутри энкодера.
                //Проходов ровно столько же, сколько у CompatUtf8Exhauster, -
                //только порядок обратный: тот ищет по char'ам и потом
                //транскодирует, этот транскодирует и потом ищет по байтам
                var span = GetSpan((text.Length * 3) + 2);
                span[0] = (byte)'"';

                var written = JsonStringEncoder.Transcode(text, span.Slice(1));

                var first = _encoder.FindFirstCharacterToEncodeUtf8(span.Slice(1, written));
                if (first < 0)
                {
                    span[written + 1] = (byte)'"';
                    Advance(written + 2);
                    return;
                }

                EscapeTail(span, written, first);
                AppendQuote();
                return;
            }

            AppendQuote();

            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    //корректная пара - её разберёт энкодер
                    i++;
                    continue;
                }

                if (!char.IsSurrogate(c))
                {
                    continue;
                }

                Run(text.Slice(start, i - start));

                //именно литералом, а не символом замены: транскодирование дало
                //бы те же байты, что у настоящего U+FFFD, а тот пишется сырым
                AppendRaw("\\uFFFD"u8);
                start = i + 1;
            }

            Run(text.Slice(start));
            AppendQuote();
        }

        /// <summary>
        /// Кусок строки без обрамляющих кавычек и заведомо без непарных
        /// суррогатов.
        /// </summary>
        private void Run(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return;
            }

            var span = GetSpan(text.Length * 3);
            var written = JsonStringEncoder.Transcode(text, span);

            var first = _encoder.FindFirstCharacterToEncodeUtf8(span.Slice(0, written));
            if (first < 0)
            {
                Advance(written);
                return;
            }

            EscapeTail(span, written, first, 0);
        }

        /// <summary>
        /// Хвост, начиная с <paramref name="first"/>, переписывается
        /// энкодером. Префикс уже лежит в буфере и верен: до этого места
        /// энкодеру нечего было делать.
        ///
        /// <para>
        /// Хвост уезжает в арендованный буфер, потому что источник и приёмник
        /// иначе перекрывались бы: экранирование удлиняет, и запись затирала
        /// бы ещё не прочитанное.
        /// </para>
        /// </summary>
        private void EscapeTail(Span<byte> span, int written, int first, int offset = 1)
        {
            var tailLength = written - first;
            var scratch = ArrayPool<byte>.Shared.Rent(tailLength);

            try
            {
                span.Slice(offset + first, tailLength).CopyTo(scratch);

                //префикс фиксируется до роста буфера: Grow переносит только
                //зафиксированное, и незафиксированное потерялось бы
                Advance(offset + first);

                EncodeAll(new ReadOnlySpan<byte>(scratch, 0, tailLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        private void EncodeAll(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                var destination = GetSpan((source.Length * MaxBytesPerSourceByte) + 1);

                var status = _encoder.EncodeUtf8(source, destination, out var consumed, out var written);

                Advance(written);
                source = source.Slice(consumed);

                if (status == OperationStatus.Done)
                {
                    return;
                }

                if (consumed == 0 && written == 0)
                {
                    //Места запрошено по худшему случаю, а источник - UTF-8,
                    //который мы же и построили. Если энкодер всё равно не
                    //двигается, то молчать нельзя: следующий оборот цикла
                    //повторил бы то же самое вечно
                    throw new InvalidOperationException(
                        "the JavaScriptEncoder made no progress (" + status + "); the bridge cannot write this value."
                        );
                }
            }
        }

        private void AppendQuote()
        {
            var span = GetSpan(1);
            span[0] = (byte)'"';
            Advance(1);
        }

        /// <summary>
        /// Есть ли в тексте суррогат <b>без пары</b>. Отдельным проходом, и
        /// проход этот дешёвый: у строки без суррогатов вовсе он кончается
        /// одним векторным поиском, а таких строк подавляющее большинство.
        /// </summary>
        private static bool HasLoneSurrogate(ReadOnlySpan<char> text)
        {
            var index = IndexOfSurrogate(text, 0);

            while (index >= 0)
            {
                if (char.IsHighSurrogate(text[index])
                    && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1]))
                {
                    index = IndexOfSurrogate(text, index + 2);
                    continue;
                }

                return true;
            }

            return false;
        }

        private static int IndexOfSurrogate(ReadOnlySpan<char> text, int from)
        {
            if (from >= text.Length)
            {
                return -1;
            }

#if NET8_0_OR_GREATER
            var found = System.MemoryExtensions.IndexOfAnyInRange(text.Slice(from), '\uD800', '\uDFFF');
            return found < 0 ? -1 : from + found;
#else
            for (var i = from; i < text.Length; i++)
            {
                if (char.IsSurrogate(text[i]))
                {
                    return i;
                }
            }

            return -1;
#endif
        }
    }
}
