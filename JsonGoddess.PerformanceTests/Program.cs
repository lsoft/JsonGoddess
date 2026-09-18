using System;
using System.Linq;
using BenchmarkDotNet.Running;

namespace JsonGoddess.PerformanceTests
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            //--verify: прогнать проверки GlobalSetup без замера. Нужен, чтобы
            //убедиться, что все участники дают один документ, не дожидаясь
            //получаса BenchmarkDotNet
            if (args.Any(a => string.Equals(a, "--verify", StringComparison.Ordinal)))
            {
                var fixture = new RegularFixture();
                fixture.Setup();
                new WideFixture().Setup();

                //формы под §8.2/O5/O7 и лестница флагов: у них проверка - это
                //не формальность, а единственное, что отличает замер от
                //бессмыслицы. Участник, который читает меньше остальных,
                //окажется быстрее всех, и таблица покажет ровно это
                new PrefixFixture().Setup();
                new BucketFixture().Setup();
                new FlagCostFixture().Setup();
                new LayoutFixture().Setup();

                Console.WriteLine("verify: all serializers agree on every document, and they read back.");
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
