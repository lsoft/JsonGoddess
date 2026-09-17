using System;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// Цена <b>включённого</b> флага - по строке на каждый, как требует план
    /// от фазы 6.
    ///
    /// Не путать с утверждением «выключённый флаг стоит ноль»: то держится
    /// тестом на текст (порождаемый код хоста без флагов не меняется ни на
    /// байт, §9.11/§9.12) и в замере не нуждается. Здесь меряется другое и
    /// ниоткуда не выводимое: сколько платит тот, кто флаг <b>включил</b>.
    ///
    /// Документ один на всех - REGULAR, тот же, что в §12.1. Отличается между
    /// участниками только код, который для них напечатан, поэтому
    /// <c>Ratio</c> к <see cref="Base"/> и есть цена флага.
    ///
    /// Оговорка, без которой числа соврут: документ здесь <b>чистый</b> - без
    /// комментариев, без висячих запятых, без дубликатов, без неизвестных
    /// свойств. То есть меряется цена, которую флаг берёт с документа, на
    /// котором он ни разу не срабатывает, - цена самой проверки, а не работы.
    /// Это и есть интересное число: за срабатывание платит тот, кто прислал
    /// такой документ, а за проверку - все.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class FlagCostFixture
    {
        private Order _order = null!;
        private byte[] _utf8 = null!;

        [GlobalSetup]
        public void Setup()
        {
            _order = Order.CreateSample();
            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_order);

            Verify();
        }

        /// <summary>
        /// Тринадцать хостов обязаны прочитать один документ одинаково. Флаг,
        /// меняющий результат на чистом документе, - не флаг, а ошибка.
        /// </summary>
        private void Verify()
        {
            LadderBase.Deserialize(DefaultInjector.Instance, _utf8, out var expected);
            if (expected is null)
            {
                throw new InvalidOperationException("лестница: базовый хост прочитал null");
            }

            Expect("DuplicateProperties", Read(LadderDuplicateProperties.Deserialize), expected);
            Expect("TrailingContent", Read(LadderTrailingContent.Deserialize), expected);
            Expect("ControlCharsInStrings", Read(LadderControlChars.Deserialize), expected);
            Expect("StrictNumbers", Read(LadderStrictNumbers.Deserialize), expected);
            Expect("InvalidUtf8", Read(LadderInvalidUtf8.Deserialize), expected);
            Expect("MaxDepth", Read(LadderMaxDepth.Deserialize), expected);
            Expect("UnknownProperties", Read(LadderUnknownProperties.Deserialize), expected);
            Expect("Comments", Read(LadderComments.Deserialize), expected);
            Expect("TrailingCommas", Read(LadderTrailingCommas.Deserialize), expected);
            Expect("NamedFloatingPointLiterals", Read(LadderNamedFloats.Deserialize), expected);
            Expect("NumbersFromStrings", Read(LadderNumbersFromStrings.Deserialize), expected);
            Expect("CaseInsensitiveNames", Read(LadderCaseInsensitive.Deserialize), expected);

            //и отдельно: документ - действительно эталонный
            var exhauster = new PooledUtf8Exhauster(4096);
            LadderBase.Serialize(exhauster, _order);
            var ours = Encoding.UTF8.GetString(exhauster.ToArray());
            var theirs = Encoding.UTF8.GetString(_utf8);

            if (ours != theirs)
            {
                throw new InvalidOperationException(
                    "лестница: наш документ разошёлся с эталонным:"
                    + Environment.NewLine + "ours: " + ours
                    + Environment.NewLine + "stj : " + theirs
                    );
            }
        }

        private delegate void Reader(DefaultInjector injector, ReadOnlySpan<byte> json, out Order? result);

        private Order? Read(Reader reader)
        {
            reader(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        private static void Expect(string flag, Order? actual, Order expected)
        {
            if (actual is null)
            {
                throw new InvalidOperationException("лестница, флаг " + flag + ": прочитан null");
            }

            if (actual.Id != expected.Id
                || actual.Customer != expected.Customer
                || actual.Total != expected.Total
                || actual.Paid != expected.Paid
                || actual.Reference != expected.Reference
                || actual.Created != expected.Created
                || (actual.Lines?.Count ?? -1) != (expected.Lines?.Count ?? -1))
            {
                throw new InvalidOperationException(
                    "лестница, флаг " + flag + ": документ прочитан иначе, чем базовым хостом"
                    );
            }
        }

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Baseline = true, Description = "без флагов")]
        public Order? Base() => Read(LadderBase.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: DuplicateProperties")]
        public Order? GuardDuplicates() => Read(LadderDuplicateProperties.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: TrailingContent")]
        public Order? GuardTrailingContent() => Read(LadderTrailingContent.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: ControlCharsInStrings")]
        public Order? GuardControlChars() => Read(LadderControlChars.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: StrictNumbers")]
        public Order? GuardStrictNumbers() => Read(LadderStrictNumbers.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: InvalidUtf8")]
        public Order? GuardInvalidUtf8() => Read(LadderInvalidUtf8.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: MaxDepth")]
        public Order? GuardMaxDepth() => Read(LadderMaxDepth.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "guard: UnknownProperties")]
        public Order? GuardUnknownProperties() => Read(LadderUnknownProperties.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "feature: Comments")]
        public Order? FeatureComments() => Read(LadderComments.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "feature: TrailingCommas")]
        public Order? FeatureTrailingCommas() => Read(LadderTrailingCommas.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "feature: NamedFloatingPointLiterals")]
        public Order? FeatureNamedFloats() => Read(LadderNamedFloats.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "feature: NumbersFromStrings")]
        public Order? FeatureNumbersFromStrings() => Read(LadderNumbersFromStrings.Deserialize);

        [BenchmarkCategory("flag-cost")]
        [Benchmark(Description = "feature: CaseInsensitiveNames")]
        public Order? FeatureCaseInsensitive() => Read(LadderCaseInsensitive.Deserialize);
    }
}
