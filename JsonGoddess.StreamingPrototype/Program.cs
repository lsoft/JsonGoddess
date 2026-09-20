using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Прототип гибридного читателя (PLAN.md §12.9, фаза 10).
    ///
    /// <para>
    /// Без аргументов - проверки: есть ли подводные камни. С <c>--bench</c> -
    /// замер, и запускать его надо через <c>run-benchmarks.bat --stream</c>,
    /// иначе прогон не закреплён на P-ядрах.
    /// </para>
    /// </summary>
    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--bench", StringComparison.Ordinal)))
            {
                BenchmarkSwitcher
                    .FromAssembly(typeof(Program).Assembly)
                    .Run(args.Where(a => !string.Equals(a, "--bench", StringComparison.Ordinal)).ToArray());

                return 0;
            }

            if (args.Any(a => string.Equals(a, "--network", StringComparison.Ordinal)))
            {
                await Web.Network.All();
                return 0;
            }

            if (args.Any(a => string.Equals(a, "--alloc", StringComparison.Ordinal)))
            {
                await Allocations.All();
                return 0;
            }

            await Verify.All();
            await PolymorphicVerify.All();
            await Web.WebVerify.All();

            Console.WriteLine();

            if (Verify.Failures == 0)
            {
                Console.WriteLine("проверки прошли");
                return 0;
            }

            Console.WriteLine("ПРОВАЛОВ: " + Verify.Failures);
            return 1;
        }
    }
}
