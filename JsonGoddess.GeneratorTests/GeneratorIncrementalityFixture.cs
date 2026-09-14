using System.Collections.Generic;
using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Инкрементальность - <b>обе</b> её половины, и вторая важнее первой.
    ///
    /// Попадания кэша на нерелевантных правках мерит всякий; здесь вместе с
    /// ними проверяются <b>промахи</b> на межфайловых. Генератор, заведённый
    /// строго от узла хоста, прошёл бы первый тест идеально и провалил второй,
    /// выдавая устаревший код после переименования члена в соседнем файле - и
    /// ничем бы себя при этом не выдал.
    /// </summary>
    public class GeneratorIncrementalityFixture
    {
        private const string HostText = @"
using JsonGoddess;

namespace Demo
{
    [JsonSubject(typeof(Order), true)]
    public partial class OrderSerializer
    {
    }
}
";

        private const string ModelText = @"
namespace Demo
{
    public class Order
    {
        public int Id { get; set; }
        public string? Customer { get; set; }
    }
}
";

        [Fact]
        public void Edit_in_an_unrelated_file_is_a_cache_hit()
        {
            var first = Files("namespace Demo { public class Unrelated { public int X { get; set; } } }");
            var second = Files("namespace Demo { public class Unrelated { public int X { get; set; } public int Y { get; set; } } }");

            var driver = GeneratorHarness.CreateDriver();
            var run1 = GeneratorHarness.Continue(driver, GeneratorHarness.CreateCompilation(first));
            var run2 = GeneratorHarness.Continue(run1.Driver, GeneratorHarness.CreateCompilation(second));

            Assert.Equal(run1.SingleGeneratedFile, run2.SingleGeneratedFile);
            Assert.All(OutputReasons(run2), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        }

        [Fact]
        public void Renaming_a_member_in_another_file_is_a_cache_miss()
        {
            var first = Files("namespace Demo { public class Unrelated { } }");

            var driver = GeneratorHarness.CreateDriver();
            var run1 = GeneratorHarness.Continue(driver, GeneratorHarness.CreateCompilation(first));

            //правка не в файле хоста, а в файле модели: именно этот случай
            //отличает конвейер, проходящий через Compilation, от конвейера,
            //заведённого от размеченного объявления
            var renamed = new List<SourceFile>
            {
                new SourceFile("Host.cs", HostText),
                new SourceFile("Model.cs", ModelText.Replace("Customer", "Client")),
                new SourceFile("Unrelated.cs", "namespace Demo { public class Unrelated { } }"),
            };

            var run2 = GeneratorHarness.Continue(run1.Driver, GeneratorHarness.CreateCompilation(renamed));

            Assert.Contains("Customer", run1.SingleGeneratedFile);
            Assert.Contains("Client", run2.SingleGeneratedFile);
            Assert.DoesNotContain("Customer", run2.SingleGeneratedFile);
            Assert.Contains(OutputReasons(run2), reason => reason != IncrementalStepRunReason.Cached);
        }

        /// <summary>
        /// Правка в том же файле, не меняющая результат, тоже обязана быть
        /// попаданием: связывание отработает заново, но выход совпадёт, и
        /// именно на этом равенстве держится вся схема.
        /// </summary>
        [Fact]
        public void Edit_that_does_not_change_the_output_is_a_cache_hit()
        {
            var first = Files("namespace Demo { public class Unrelated { } }");

            var driver = GeneratorHarness.CreateDriver();
            var run1 = GeneratorHarness.Continue(driver, GeneratorHarness.CreateCompilation(first));

            var commented = new List<SourceFile>
            {
                new SourceFile("Host.cs", HostText),
                new SourceFile("Model.cs", "//просто комментарий" + ModelText),
                new SourceFile("Unrelated.cs", "namespace Demo { public class Unrelated { } }"),
            };

            var run2 = GeneratorHarness.Continue(run1.Driver, GeneratorHarness.CreateCompilation(commented));

            Assert.All(OutputReasons(run2), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        }

        private static List<SourceFile> Files(string unrelated)
        {
            return new List<SourceFile>
            {
                new SourceFile("Host.cs", HostText),
                new SourceFile("Model.cs", ModelText),
                new SourceFile("Unrelated.cs", unrelated),
            };
        }

        private static IReadOnlyList<IncrementalStepRunReason> OutputReasons(GeneratorRun run)
        {
            var reasons = run.RunResult.Results
                .SelectMany(r => r.TrackedOutputSteps)
                .SelectMany(pair => pair.Value)
                .SelectMany(step => step.Outputs)
                .Select(output => output.Reason)
                .ToList();

            Assert.NotEmpty(reasons);
            return reasons;
        }
    }
}
