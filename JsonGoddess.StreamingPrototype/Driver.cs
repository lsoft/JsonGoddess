using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
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
    /// Драйвер корневого массива.
    ///
    /// <para>
    /// <b>Здесь больше нет ни одной строки разбора.</b> С пунктом 6в автомат
    /// верхнего уровня печатает эмиттер (<c>StreamDriverProducer</c>), а цикл по
    /// трубе, сборка окна и потолок живут в рантайме
    /// (<c>JsonStreamReader&lt;T&gt;</c>). Осталась оболочка, которая переносит
    /// счётчики порождённого драйвера в <see cref="DriverStats"/>, - ровно
    /// затем, чтобы все проверки прототипа продолжали читаться как раньше и
    /// проверяли теперь порождённый код.
    /// </para>
    /// </summary>
    internal static class Driver
    {
        internal const int DefaultCap = JsonStreamReader<System.Collections.Generic.IList<Order>>.DefaultCap;

        /// <summary>Печать хода разбора - осталась от ручного драйвера, ныне ничего не печатает.</summary>
        internal static bool Trace;

        internal static async Task<Order[]> ReadOrders(
            PipeReader pipe,
            DriverStats stats,
            int cap = DefaultCap,
            CancellationToken token = default
            )
        {
            var driver = new Generated.OrderHost.Stream_JsonGoddess_PerformanceTests_Model_Order(
                DefaultInjector.Instance,
                asList: false
                );

            try
            {
                //длина тела здесь неизвестна - проверки кормят трубу по кускам
                //нарочно, и быстрому пути тут делать нечего
                return (Order[])await driver.ReadAsync(pipe, cap, -1, token).ConfigureAwait(false);
            }
            finally
            {
                stats.Reads += driver.Reads;
                stats.Retries += driver.Retries;
                stats.Skipped += driver.Skipped;
                stats.Gathers += driver.Gathers;
                stats.LargestWindow = Math.Max(stats.LargestWindow, driver.LargestWindow);
            }
        }

        /// <summary>
        /// Непрерывный кусок - осталось ради драйвера одиночного объекта,
        /// который до пункта 6в-2 ещё рукописный. У порождённого драйвера это
        /// делает рантайм.
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

            return new ReadOnlySpan<byte>(scratch, 0, length);
        }
    }
}
