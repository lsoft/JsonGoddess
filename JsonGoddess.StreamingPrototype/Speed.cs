using System;
using System.Collections.Generic;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Разбор целого буфера тем же читателем: <c>final</c> истинен с первого
    /// байта, переигрываний нет ни одного.
    ///
    /// <para>
    /// Нужен двоим - проверке (сверить прочитанное с эталоном до замера) и
    /// самому замеру (строка «целый буфер»). Замер живёт в
    /// <see cref="StreamingFixture"/> и запускается
    /// <c>run-benchmarks.bat --stream</c>; грубого <c>Stopwatch</c> здесь не
    /// осталось намеренно - он показывал разброс больше измеряемой величины.
    /// </para>
    /// </summary>
    internal static class Speed
    {
        internal static Order[] OneShot(byte[] body)
        {
            var span = body.AsSpan();
            var context = new JsonParseContext(span);
            var items = default(OrderBuilder);

            try
            {
                var position = 0;

                TryScan.Expect(span, ref position, TryScan.OpenBracket, true);

                if (!TryScan.TryConsume(span, ref position, TryScan.CloseBracket, true, out var empty) || empty)
                {
                    return items.Finish();
                }

                while (true)
                {
                    OrderReader.Order(span, ref position, ref context, true, out var order);
                    items.Add(order!);

                    TryScan.TryConsume(span, ref position, TryScan.Comma, true, out var more);
                    if (!more)
                    {
                        break;
                    }
                }

                TryScan.Expect(span, ref position, TryScan.CloseBracket, true);
                return items.Finish();
            }
            finally
            {
                items.Release();
                context.Release();
            }
        }
    }
}
