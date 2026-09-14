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
                Console.WriteLine("verify: all serializers agree on both documents, and they read back.");
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
