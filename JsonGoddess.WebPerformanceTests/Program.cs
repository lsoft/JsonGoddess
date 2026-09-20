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

                //Звенья пути записи - здесь же и по той же причине: узнать,
                //что экзостер пишет не тот документ, надо за секунды, а не в
                //конце получасового прогона
                var write = new WritePathFixture();
                write.Setup();
                write.Cleanup();

                //Порождённый входной форматтер - здесь же, а не отдельным
                //прогоном: это то же самое приложение и тот же вопрос
                //«совпадаем ли мы с эталоном», только на чтении тела
                Web.StreamingVerify.All().GetAwaiter().GetResult();

                if (Web.StreamingVerify.Failures > 0)
                {
                    Console.WriteLine("ПРОВАЛОВ: " + Web.StreamingVerify.Failures);
                    Environment.ExitCode = 1;
                }

                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
