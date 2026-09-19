using System;
using System.Linq;
using BenchmarkDotNet.Running;

namespace JsonGoddess.WebPerformanceTests
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            //--verify: поднять оба приложения, сверить документы на всех
            //ступенях и сказать, обслуживает ли мост Order на тех опциях,
            //которые построил ASP.NET. Нужен, чтобы узнать это за секунды, а
            //не в конце получасового прогона
            if (args.Any(a => string.Equals(a, "--verify", StringComparison.Ordinal)))
            {
                var fixture = new StaircaseFixture();
                fixture.Setup();
                fixture.Cleanup();

                Console.WriteLine("verify: every step of the staircase produces the same document.");
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
