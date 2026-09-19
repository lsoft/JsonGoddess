using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Драйвер для тела, у которого в корне <b>один объект</b>, а не массив.
    ///
    /// <para>
    /// <b>Здесь больше нет ни одной строки разбора.</b> С пунктом 6в автомат -
    /// включая спуск в свойство-коллекцию - печатает эмиттер
    /// (<c>StreamDriverProducer</c>). Осталась оболочка, переносящая счётчики в
    /// <see cref="DriverStats"/>: проверки прототипа читаются как раньше и
    /// проверяют теперь порождённый код.
    /// </para>
    /// </summary>
    internal static class SingleDriver
    {
        internal static async Task<Order?> ReadOne(
            PipeReader pipe,
            DriverStats stats,
            int cap = Driver.DefaultCap,
            CancellationToken token = default
            )
        {
            var driver = new Generated.OrderHost.StreamOne_JsonGoddess_PerformanceTests_Model_Order(
                DefaultInjector.Instance
                );

            try
            {
                return await driver.ReadAsync(pipe, cap, token).ConfigureAwait(false);
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
    }
}
