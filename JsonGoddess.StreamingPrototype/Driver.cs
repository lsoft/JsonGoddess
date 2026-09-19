using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Сколько работы стоила нарезка. Нужна не для отчёта, а для проверки
    /// главного обещания схемы: переигрываний должно быть порядка числа границ,
    /// а не числа элементов.
    /// </summary>
    internal sealed class DriverStats
    {
        internal int Reads;

        internal int Retries;

        /// <summary>
        /// Сколько раз разбор элемента даже не начинали - заведомо не влезал.
        /// Это те переигрывания, которых удалось не делать.
        /// </summary>
        internal int Skipped;

        internal int Gathers;

        internal long GatheredBytes;

        internal int LargestWindow;

        public override string ToString()
        {
            return "обращений к трубе " + Reads
                + ", переигранных элементов " + Retries
                + ", не начатых " + Skipped
                + ", сборок окна " + Gathers + " (" + GatheredBytes + " Б)"
                + ", наибольшее окно " + LargestWindow + " Б";
        }
    }

    /// <summary>
    /// Драйвер гибридной схемы (PLAN.md §12.9, фаза 10).
    ///
    /// <para>
    /// Автомат здесь неглубокий - ровно на верхний уровень: «перед массивом»,
    /// «в элементах», «готово». Внутри элемента работает обычный быстрый
    /// читатель, и всё, что от него требуется, - вернуть <c>false</c>, если
    /// байты кончились. Недобранный элемент выбрасывается и переигрывается
    /// целиком, когда данные доедут.
    /// </para>
    ///
    /// <para>
    /// Ожидание стои́т здесь и только здесь - между двумя <c>await</c> идёт
    /// разбор всего, что влезло. Именно поэтому разбор совмещается с приёмом
    /// по сети, чего форматтер с копированием не умеет по построению.
    /// </para>
    /// </summary>
    internal static class Driver
    {
        /// <summary>
        /// Состояния верхнего автомата. Их пять, а не три, и это первая находка
        /// прототипа: окно кончается где угодно, в том числе <b>между</b>
        /// элементом и запятой, и «в элементах» такого положения не выражает.
        ///
        /// <para>
        /// Первый элемент отделён от прочих не из педантизма: закрывающая
        /// скобка законна только до первого элемента (<c>[]</c>), а после
        /// запятой - уже нет (<c>[1,]</c> эталон отвергает).
        /// </para>
        /// </summary>
        private enum Phase
        {
            BeforeArray = 0,
            BeforeFirstElement = 1,
            BeforeElement = 2,
            AfterElement = 3,
            Done = 4,
        }

        /// <summary>
        /// Потолок на недочитанную конструкцию. У эталона такой защиты нет
        /// вовсе: проба <c>BigTokenProbe</c> показала, что он спокойно растит
        /// буфер до размера токена, каким бы тот ни был.
        /// </summary>
        internal const int DefaultCap = 8 * 1024 * 1024;

        internal static async Task<Order[]> ReadOrders(
            PipeReader pipe,
            DriverStats stats,
            int cap = DefaultCap,
            CancellationToken token = default
            )
        {
            var items = default(OrderBuilder);
            var phase = Phase.BeforeArray;
            byte[]? scratch = null;

            //Наибольший виденный элемент. Нужен, чтобы не начинать разбор там,
            //где он заведомо не влезет: недобранный объект выбрасывается, и
            //мусор от него - единственное, чем потоковый разбор был дороже
            //буферного по аллокациям.
            var largestElement = 0;

            try
            {
                while (true)
                {
                    var read = await pipe.ReadAsync(token).ConfigureAwait(false);

                    if (read.IsCanceled)
                    {
                        throw new OperationCanceledException();
                    }

                    var buffer = read.Buffer;
                    stats.Reads++;

                    if (buffer.Length > cap)
                    {
                        throw new JsonDocumentException(
                            "The pending JSON value exceeds the configured limit of " + cap + " bytes.",
                            0
                            );
                    }

                    var was = phase;
                    var consumed = ParseWhatFits(
                        buffer,
                        ref phase,
                        ref items,
                        read.IsCompleted,
                        ref scratch,
                        ref largestElement,
                        stats
                        );

                    if (Trace)
                    {
                        Console.WriteLine(
                            "    трасса: окно " + buffer.Length + " Б, фаза " + was + " -> " + phase
                            + ", съедено " + consumed + ", элементов " + items.Count
                            );
                    }

                    pipe.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    if (phase == Phase.Done)
                    {
                        return items.Finish();
                    }

                    if (read.IsCompleted)
                    {
                        //сюда попасть нельзя: при final читатель обязан либо
                        //дочитать, либо отказать. Проверка на случай, если
                        //где-то в Try-примитивах упущен случай.
                        throw new JsonDocumentException("Unexpected end of data.", 0);
                    }
                }
            }
            finally
            {
                //и на отказе тоже: невозвращённая аренда - это не утечка
                //памяти, а тихая деградация пула
                items.Release();

                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }
            }
        }

        /// <summary>
        /// Синхронная часть: ни одного <c>await</c>, поэтому спаны и
        /// <see cref="JsonParseContext"/> живут здесь совершенно законно.
        /// </summary>
        private static int ParseWhatFits(
            in ReadOnlySequence<byte> buffer,
            ref Phase phase,
            ref OrderBuilder items,
            bool final,
            ref byte[]? scratch,
            ref int largestElement,
            DriverStats stats
            )
        {
            var span = Contiguous(buffer, ref scratch, stats);
            var context = new JsonParseContext(span);

            if (span.Length > stats.LargestWindow)
            {
                stats.LargestWindow = span.Length;
            }

            try
            {
                var position = 0;
                var consumed = 0;

                if (phase == Phase.BeforeArray)
                {
                    if (!TryScan.Expect(span, ref position, TryScan.OpenBracket, final))
                    {
                        return consumed;
                    }

                    phase = Phase.BeforeFirstElement;
                    consumed = position;
                }

                while (true)
                {
                    if (phase == Phase.BeforeFirstElement)
                    {
                        //пустой массив законен только здесь
                        if (!TryScan.TryConsume(span, ref position, TryScan.CloseBracket, final, out var empty))
                        {
                            return consumed;
                        }

                        if (empty)
                        {
                            phase = Phase.Done;
                            return position;
                        }

                        phase = Phase.BeforeElement;
                    }

                    if (phase == Phase.BeforeElement)
                    {
                        //Заведомо не влезет - даже не начинаем. Пробовать
                        //стои́т не времени, а мусора: до места обрыва мы уже
                        //создали объект, список строк и сами строки, и всё это
                        //выбрасывается. Оценка грубая - по наибольшему из
                        //виденных элементов, - и ошибиться она может только в
                        //сторону лишнего круга по трубе.
                        var start = position;

                        if (!final && span.Length - position < largestElement)
                        {
                            stats.Skipped++;
                            return consumed;
                        }

                        bool read;
                        Order? order;

                        try
                        {
                            read = OrderReader.Order(span, ref position, ref context, final, out order);
                        }
                        catch (JsonDocumentException failure)
                        {
                            //Путь эталона - это $[3].id, то есть индекс
                            //элемента плюс место внутри него. Индекс знает
                            //драйвер, место внутри - холодный проход по
                            //элементу (§6.4); окна целиком для прохода нет и
                            //быть не может, оно кончается где попало.
                            throw Located(failure, span, start, items.Count);
                        }

                        if (!read)
                        {
                            //элемент не влез: откатываемся к его началу и ждём
                            stats.Retries++;
                            return consumed;
                        }

                        items.Add(order!);
                        consumed = position;
                        phase = Phase.AfterElement;

                        var size = position - start;
                        if (size > largestElement)
                        {
                            largestElement = size;
                        }
                    }

                    if (!TryScan.TryConsume(span, ref position, TryScan.Comma, final, out var more))
                    {
                        return consumed;
                    }

                    if (more)
                    {
                        consumed = position;
                        phase = Phase.BeforeElement;
                        continue;
                    }

                    if (!TryScan.Expect(span, ref position, TryScan.CloseBracket, final))
                    {
                        return consumed;
                    }

                    phase = Phase.Done;
                    return position;
                }
            }
            catch (JsonDocumentException failure) when (failure.Path is null)
            {
                //Отказ ВНЕ элемента: до массива, между элементами, на его
                //закрытии. Путь тут ставит драйвер, потому что внутри элемента
                //мы не были. Правила сняты пробой у эталона (Verify.Paths):
                //до массива - корень, дальше - индекс СЛЕДУЮЩЕГО элемента.
                JsonPath.Locate(span, Math.Max(0, failure.BytePosition), out var line, out var column);

                throw new JsonDocumentException(
                    failure.Reason,
                    failure.BytePosition,
                    phase == Phase.BeforeArray
                        ? "$"
                        : "$[" + items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]",
                    line,
                    column,
                    failure
                    );
            }
            finally
            {
                context.Release();
            }
        }

        /// <summary>
        /// Печать хода разбора. Оставлена намеренно: обе находки прототипа
        /// нашлись именно ею, а не рассуждением, - видно было, что драйвер
        /// сообщает «съедено 0», а окно всё равно уезжает вперёд.
        /// </summary>
        internal static bool Trace;

        /// <summary>
        /// Отказ с путём в нотации эталона: <c>$[3].id</c>.
        ///
        /// <para>
        /// Собирается из двух половин, и иначе нельзя. Индекс элемента знает
        /// только драйвер; место внутри элемента - только холодный проход по
        /// документу (§6.4), а документа у нас нет: есть окно, которое
        /// кончается где попало и начинается не с начала тела. Поэтому проход
        /// идёт по <b>элементу</b>, а его результат приставляется к индексу.
        /// </para>
        /// </summary>
        private static JsonDocumentException Located(
            JsonDocumentException failure,
            ReadOnlySpan<byte> span,
            int elementStart,
            int index
            )
        {
            var inside = JsonPath.Decorate(
                new JsonDocumentException(
                    failure.Reason,
                    Math.Max(0, failure.BytePosition - elementStart),
                    failure.Anchor
                    ),
                span.Slice(elementStart)
                );

            var tail = inside.Path is null || inside.Path.Length <= 1
                ? string.Empty
                : inside.Path.Substring(1);

            return new JsonDocumentException(
                failure.Reason,
                failure.BytePosition,
                "$[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" + tail,
                inside.LineNumber,
                inside.BytePositionInLine,
                failure
                );
        }

        /// <summary>
        /// Непрерывный кусок. Обычный случай - один сегмент, и тогда не
        /// копируется ничего. Иначе собирается <b>остаток</b>, а он ограничен
        /// недочитанным элементом, а не телом.
        /// </summary>
        internal static ReadOnlySpan<byte> Contiguous(
            in ReadOnlySequence<byte> buffer,
            ref byte[]? scratch,
            DriverStats stats
            )
        {
            if (buffer.IsSingleSegment)
            {
                return buffer.FirstSpan;
            }

            var length = checked((int)buffer.Length);

            if (scratch is null || scratch.Length < length)
            {
                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }

                scratch = ArrayPool<byte>.Shared.Rent(length);
            }

            buffer.CopyTo(scratch);

            stats.Gathers++;
            stats.GatheredBytes += length;

            return scratch.AsSpan(0, length);
        }
    }
}
