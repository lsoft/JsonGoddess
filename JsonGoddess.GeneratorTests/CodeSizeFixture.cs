using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Риск из §16 плана: <b>взрыв объёма порождаемого кода</b> на графе из
    /// сотен типов.
    ///
    /// План велит мерить это «с фазы 4», и мерить приходится именно здесь:
    /// кодогенерация - единственная техника, у которой цена платится не в
    /// рантайме, а в сборке потребителя, и не увидеть её в бенчмарках нельзя
    /// по построению.
    ///
    /// Замер оформлен <b>тестом</b>, а не разовым прогоном, и это решение:
    /// однажды снятое число устаревает молча, а проверка - нет. Утверждение
    /// при этом делается только о <b>размере</b>, который детерминирован;
    /// время печатается в отчёт, но ничего не держит - на занятой машине оно
    /// красило бы сборку по чужой вине.
    /// </summary>
    public class CodeSizeFixture
    {
        /// <summary>
        /// Размеры графа. Двести - цифра из плана; меньшие нужны затем, что
        /// проверяется не размер, а <b>рост</b>: одна точка не отличает
        /// линейный от квадратичного.
        /// </summary>
        private static readonly int[] _sizes = { 25, 50, 100, 200, };

        public sealed class Measurement
        {
            public required int Types { get; init; }

            public required int GeneratedBytes { get; init; }

            public required int GeneratedLines { get; init; }

            public required long GeneratorMilliseconds { get; init; }

            public required long CompileMilliseconds { get; init; }

            public double BytesPerType => (double)GeneratedBytes / Types;
        }

        /// <summary>
        /// Синтетический граф: у каждого типа скаляры, ссылка на соседа,
        /// коллекция соседей и словарь. Не «двести плоских POCO»: доля
        /// порождаемого кода, приходящаяся на коллекции и на диспетчер имён,
        /// у плоских типов вышла бы заниженной, а риск - недооценённым.
        /// </summary>
        private static string Graph(int count)
        {
            var source = new StringBuilder();

            source.AppendLine("using System;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using JsonGoddess;");
            source.AppendLine("namespace Demo");
            source.AppendLine("{");

            for (var i = 0; i < count; i++)
            {
                var next = "Node" + ((i + 1) % count);

                source.AppendLine("    public class Node" + i);
                source.AppendLine("    {");
                source.AppendLine("        public int Id { get; set; }");
                source.AppendLine("        public string? Title { get; set; }");
                source.AppendLine("        public decimal Amount { get; set; }");
                source.AppendLine("        public DateTime Created { get; set; }");
                source.AppendLine("        public Guid Reference { get; set; }");
                source.AppendLine("        public bool Active { get; set; }");
                source.AppendLine("        public " + next + "? Next { get; set; }");
                source.AppendLine("        public List<" + next + ">? Children { get; set; }");
                source.AppendLine("        public Dictionary<string, int>? Counters { get; set; }");
                source.AppendLine("        public int[]? Numbers { get; set; }");
                source.AppendLine("    }");
                source.AppendLine();
            }

            source.AppendLine("    [JsonSubject(typeof(Node0), true)]");
            for (var i = 1; i < count; i++)
            {
                source.AppendLine("    [JsonSubject(typeof(Node" + i + "), false)]");
            }

            source.AppendLine("    public partial class GraphSerializer { }");
            source.AppendLine("}");

            return source.ToString();
        }

        private static Measurement Measure(int count)
        {
            var files = new[] { new SourceFile("Graph.cs", Graph(count)), };

            var compilation = GeneratorHarness.CreateCompilation(files);
            var driver = GeneratorHarness.CreateDriver();

            var stopwatch = Stopwatch.StartNew();
            var run = GeneratorHarness.Continue(driver, compilation);
            var generated = stopwatch.ElapsedMilliseconds;

            Assert.Empty(run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

            var text = run.SingleGeneratedFile;

            stopwatch.Restart();
            var errors = run.CompilationErrors;
            var compiled = stopwatch.ElapsedMilliseconds;

            Assert.Empty(errors.Select(e => e.ToString()));

            return new Measurement
            {
                Types = count,
                GeneratedBytes = Encoding.UTF8.GetByteCount(text),
                GeneratedLines = text.Count(c => c == '\n') + 1,
                GeneratorMilliseconds = generated,
                CompileMilliseconds = compiled,
            };
        }

        /// <summary>
        /// Порождаемый код растёт <b>линейно</b> по числу типов.
        ///
        /// Проверяется именно это, а не абсолютный размер: абсолютный зависит
        /// от того, сколько членов у типа в образце, и запрещать его значило бы
        /// запрещать себе печатать код. А вот сверхлинейный рост - это уже
        /// ошибка конструкции, и она обязана краснеть.
        ///
        /// Граница взята с большим запасом (полтора против единицы у идеально
        /// линейного): у маленького графа на тип приходится доля преамбулы и
        /// общих методов, поэтому байт на тип у него заведомо больше, а не
        /// меньше.
        /// </summary>
        [Fact]
        public void Generated_code_grows_linearly_with_the_number_of_types()
        {
            var measurements = _sizes.Select(Measure).ToList();

            var smallest = measurements.First();
            var largest = measurements.Last();

            Assert.True(
                largest.BytesPerType <= smallest.BytesPerType * 1.5,
                "байт на тип: " + Format(smallest.BytesPerType) + " при " + smallest.Types
                + " типах и " + Format(largest.BytesPerType) + " при " + largest.Types
                + " - это не линейный рост"
                );

            WriteReport(measurements);
        }

        private static void WriteReport(IReadOnlyList<Measurement> measurements)
        {
            var report = new StringBuilder();

            report.Append("# Объём порождаемого кода\n\n");
            report.Append("Отчёт порождается тестом `CodeSizeFixture`; править руками нечего.\n\n");
            report.Append("Риск §16: кодогенерация платит не в рантайме, а в сборке потребителя, и\n");
            report.Append("бенчмарками этого не видно по построению. Граф синтетический: у каждого типа\n");
            report.Append("шесть скаляров, ссылка на соседа, список соседей, словарь и массив.\n\n");

            report.Append("| Типов | Байт | Строк | Байт на тип | Генератор, мс | Компиляция, мс |\n");
            report.Append("|---:|---:|---:|---:|---:|---:|\n");

            foreach (var measurement in measurements)
            {
                report.Append("| ").Append(N(measurement.Types))
                    .Append(" | ").Append(N(measurement.GeneratedBytes))
                    .Append(" | ").Append(N(measurement.GeneratedLines))
                    .Append(" | ").Append(Format(measurement.BytesPerType))
                    .Append(" | ").Append(N((int)measurement.GeneratorMilliseconds))
                    .Append(" | ").Append(N((int)measurement.CompileMilliseconds))
                    .Append(" |\n");
            }

            report.Append('\n');
            report.Append("Держит тест только **размер**: он детерминирован. Время напечатано, но ничего\n");
            report.Append("не держит - на занятой машине оно красило бы сборку по чужой вине.\n");

            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "code-size-report.md"),
                report.ToString(),
                new UTF8Encoding(false)
                );
        }

        private static string Format(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
