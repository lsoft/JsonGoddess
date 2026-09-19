using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess;
using JsonGoddess.Internal;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Драйвер для тела, у которого в корне <b>один объект</b>, а не массив.
    ///
    /// <para>
    /// Заведён потому, что на таком теле драйвер массива вырождается: элемент
    /// там один, и «переиграть элемент» означает «перечитать весь документ», то
    /// есть держать его в памяти целиком. Здесь единицей переигрывания
    /// становится <b>свойство</b>: сам объект создаётся один раз и живёт в
    /// состоянии драйвера, а откатывается и перечитывается только то свойство,
    /// которое не влезло.
    /// </para>
    ///
    /// <para>
    /// Ниже свойства спуститься тем же приёмом можно (значение свойства -
    /// такой же массив или объект), и генератору это ничего не стоит: форма
    /// известна на компиляции. Но каждый уровень - свой драйвер, поэтому
    /// прототип останавливается здесь и честно говорит, где предел: окно
    /// ограничено <b>наибольшим значением свойства</b>, а не объектом.
    /// </para>
    /// </summary>
    internal static class SingleDriver
    {
        /// <summary>
        /// Состояния. Три последних - спуск в значение свойства
        /// <c>lines</c>: без него окно упиралось бы в это свойство целиком, а
        /// у толстого заказа оно и есть почти весь документ.
        ///
        /// <para>
        /// Ровно так же строится любой следующий уровень, и генератору это
        /// ничего не стоит: форма известна на компиляции. Прототип спускается
        /// на два уровня, чтобы показать, что построение рекурсивно, а не
        /// чтобы закрыть все формы разом.
        /// </para>
        /// </summary>
        private enum Phase
        {
            BeforeObject = 0,
            BeforeFirstProperty = 1,
            BeforeProperty = 2,
            AfterProperty = 3,
            Done = 4,

            LinesFirst = 5,
            LinesElement = 6,
            LinesAfterElement = 7,
        }

        internal static async Task<Order?> ReadOne(
            PipeReader pipe,
            DriverStats stats,
            int cap = Driver.DefaultCap,
            CancellationToken token = default
            )
        {
            var phase = Phase.BeforeObject;
            Order? result = null;
            byte[]? scratch = null;
            var largestProperty = 0;
            var largestLine = 0;

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

                    var consumed = ParseWhatFits(
                        buffer,
                        ref phase,
                        ref result,
                        read.IsCompleted,
                        ref scratch,
                        ref largestProperty,
                        ref largestLine,
                        stats
                        );

                    pipe.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    if (phase == Phase.Done)
                    {
                        return result;
                    }

                    if (read.IsCompleted)
                    {
                        throw new JsonDocumentException("Unexpected end of data.", 0);
                    }
                }
            }
            finally
            {
                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }
            }
        }

        private static int ParseWhatFits(
            in ReadOnlySequence<byte> buffer,
            ref Phase phase,
            ref Order? result,
            bool final,
            ref byte[]? scratch,
            ref int largestProperty,
            ref int largestLine,
            DriverStats stats
            )
        {
            var span = Driver.Contiguous(buffer, ref scratch, stats);
            var context = new JsonParseContext(span);

            if (span.Length > stats.LargestWindow)
            {
                stats.LargestWindow = span.Length;
            }

            try
            {
                var position = 0;
                var consumed = 0;

                if (phase == Phase.BeforeObject)
                {
                    if (!TryScan.Null(span, ref position, final, out var isNull))
                    {
                        return consumed;
                    }

                    if (isNull)
                    {
                        phase = Phase.Done;
                        return position;
                    }

                    if (!TryScan.Expect(span, ref position, TryScan.OpenBrace, final))
                    {
                        return consumed;
                    }

                    result = new Order();
                    phase = Phase.BeforeFirstProperty;
                    consumed = position;
                }

                while (true)
                {
                    if (phase == Phase.BeforeFirstProperty)
                    {
                        //пустой объект законен только здесь
                        if (!TryScan.TryConsume(span, ref position, TryScan.CloseBrace, final, out var empty))
                        {
                            return consumed;
                        }

                        if (empty)
                        {
                            phase = Phase.Done;
                            return position;
                        }

                        phase = Phase.BeforeProperty;
                    }

                    if (phase == Phase.BeforeProperty)
                    {
                        var start = position;

                        if (!final && span.Length - position < largestProperty)
                        {
                            stats.Skipped++;
                            return consumed;
                        }

                        bool read;
                        OrderReader.Member which;

                        try
                        {
                            read = OrderReader.Name(span, ref position, ref context, final, out which);

                            if (read && which == OrderReader.Member.Lines)
                            {
                                //Спуск: массив строк разбирается ЗДЕСЬ, по
                                //элементу, а не целиком внутри читателя.
                                if (!TryScan.Null(span, ref position, final, out var noLines))
                                {
                                    stats.Retries++;
                                    return consumed;
                                }

                                if (noLines)
                                {
                                    result!.Lines = null;
                                    consumed = position;
                                    phase = Phase.AfterProperty;
                                    goto separator;
                                }

                                if (!TryScan.Expect(span, ref position, TryScan.OpenBracket, final))
                                {
                                    stats.Retries++;
                                    return consumed;
                                }

                                result!.Lines = new System.Collections.Generic.List<OrderLine>();
                                consumed = position;
                                phase = Phase.LinesFirst;
                                continue;
                            }

                            if (read)
                            {
                                read = OrderReader.Value(span, ref position, result!, ref context, final, which);
                            }
                        }
                        catch (JsonDocumentException failure) when (failure.Path is null)
                        {
                            throw Located(failure, span, start);
                        }

                        if (!read)
                        {
                            //свойство не влезло: откатываемся к его имени.
                            //Объект НЕ выбрасывается - в этом вся разница с
                            //драйвером массива.
                            stats.Retries++;
                            return consumed;
                        }

                        consumed = position;
                        phase = Phase.AfterProperty;

                        var size = position - start;
                        if (size > largestProperty)
                        {
                            largestProperty = size;
                        }
                    }

                    if (phase == Phase.LinesFirst)
                    {
                        if (!TryScan.TryConsume(span, ref position, TryScan.CloseBracket, final, out var empty))
                        {
                            return consumed;
                        }

                        if (empty)
                        {
                            consumed = position;
                            phase = Phase.AfterProperty;
                            goto separator;
                        }

                        phase = Phase.LinesElement;
                    }

                    while (phase == Phase.LinesElement || phase == Phase.LinesAfterElement)
                    {
                        if (phase == Phase.LinesElement)
                        {
                            var start = position;

                            if (!final && span.Length - position < largestLine)
                            {
                                stats.Skipped++;
                                return consumed;
                            }

                            bool read;

                            try
                            {
                                read = OrderReader.Line(span, ref position, ref context, final, out var line);

                                if (read)
                                {
                                    result!.Lines!.Add(line!);
                                }
                            }
                            catch (JsonDocumentException failure) when (failure.Path is null)
                            {
                                throw Located(failure, span, start);
                            }

                            if (!read)
                            {
                                stats.Retries++;
                                return consumed;
                            }

                            consumed = position;
                            phase = Phase.LinesAfterElement;

                            var length = position - start;
                            if (length > largestLine)
                            {
                                largestLine = length;
                            }
                        }

                        if (!TryScan.TryConsume(span, ref position, TryScan.Comma, final, out var another))
                        {
                            return consumed;
                        }

                        if (another)
                        {
                            consumed = position;
                            phase = Phase.LinesElement;
                            continue;
                        }

                        if (!TryScan.Expect(span, ref position, TryScan.CloseBracket, final))
                        {
                            return consumed;
                        }

                        consumed = position;
                        phase = Phase.AfterProperty;
                    }

                separator:

                    if (!TryScan.TryConsume(span, ref position, TryScan.Comma, final, out var more))
                    {
                        return consumed;
                    }

                    if (more)
                    {
                        consumed = position;
                        phase = Phase.BeforeProperty;
                        continue;
                    }

                    if (!TryScan.Expect(span, ref position, TryScan.CloseBrace, final))
                    {
                        return consumed;
                    }

                    phase = Phase.Done;
                    return position;
                }
            }
            catch (JsonDocumentException failure) when (failure.Path is null)
            {
                JsonPath.Locate(span, Math.Max(0, failure.BytePosition), out var line, out var column);

                throw new JsonDocumentException(failure.Reason, failure.BytePosition, "$", line, column, failure);
            }
            finally
            {
                context.Release();
            }
        }

        /// <summary>
        /// Путь внутри свойства. Корень здесь - сам объект, поэтому проход
        /// идёт по документу от начала свойства, а результат приставляется к
        /// <c>$</c> без индекса: индексировать нечего.
        /// </summary>
        private static JsonDocumentException Located(
            JsonDocumentException failure,
            ReadOnlySpan<byte> span,
            int propertyStart
            )
        {
            //пройти надо по объекту, а не по свойству: иначе имя свойства в
            //путь не попадёт. Начало объекта в окне нам неизвестно, поэтому
            //честный ответ - корень плюс то, что знает сам отказ
            JsonPath.Locate(span, Math.Max(0, failure.BytePosition), out var line, out var column);

            var name = NameAt(span, propertyStart);

            return new JsonDocumentException(
                failure.Reason,
                failure.BytePosition,
                name is null ? "$" : "$." + name,
                line,
                column,
                failure
                );
        }

        /// <summary>Имя свойства, начинающегося с этой позиции.</summary>
        private static string? NameAt(ReadOnlySpan<byte> span, int start)
        {
            var position = start;

            try
            {
                return TryScan.String(span, ref position, true, out var raw, out var escaped)
                    ? JsonStringDecoder.Decode(raw, escaped)
                    : null;
            }
            catch (JsonDocumentException)
            {
                return null;
            }
        }
    }
}
