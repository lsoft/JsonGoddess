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
    /// PREFIX: худший случай диспетчера имён - тридцать членов, у которых
    /// совпадают и длина, и первые семь байт. Ключ (§8.2 плана) у всех
    /// тридцати один и тот же, поэтому <c>switch</c> схлопывается в одну метку,
    /// а внутри остаётся цепочка из тридцати сравнений.
    ///
    /// Форма заведена под вопрос O7 (§15) и отвечает на два вопроса сразу:
    /// дешевле ли <b>звено</b> цепочки, если сравнивать двумя <c>ulong</c>
    /// вместо <c>SequenceEqual</c>, и дешевле ли <b>убрать цепочку</b>,
    /// переключаясь по окну со смещением. Оба ответа осмысленны только вместе:
    /// первое удешевляет шаг, второе снимает линейный рост.
    ///
    /// Baseline здесь - не эталон, а <b>наша сегодняшняя форма</b>: вопрос
    /// стоит «стоит ли менять то, что есть», и отвечает на него отношение к
    /// ней, а не к <c>System.Text.Json</c>. Эталон в таблице всё равно
    /// присутствует - как напоминание о масштабе, в котором всё это
    /// происходит.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class PrefixFixture
    {
        private Prefix _prefix = null!;
        private byte[] _utf8 = null!;

        [GlobalSetup]
        public void Setup()
        {
            _prefix = Prefix.CreateSample();
            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_prefix);

            Verify();
        }

        /// <summary>
        /// Все пять форм обязаны прочитать один документ в один объект. Без
        /// этого сравнивать их время бессмысленно: быстрее всех окажется та,
        /// что читает меньше всего.
        /// </summary>
        private void Verify()
        {
            PrefixSerializer.DeserializeChainEqual(DefaultInjector.Instance, _utf8, out var chainEqual);
            PrefixSerializer.DeserializeChainWords(DefaultInjector.Instance, _utf8, out var chainWords);
            PrefixSerializer.DeserializeWindowEqual(DefaultInjector.Instance, _utf8, out var windowEqual);
            PrefixSerializer.DeserializeWindowWord(DefaultInjector.Instance, _utf8, out var windowWord);
            PrefixGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var generated);

            var expected = Checksum(_prefix);

            Expect("chain/SequenceEqual", chainEqual, expected);
            Expect("chain/two ulong", chainWords, expected);
            Expect("window/SequenceEqual", windowEqual, expected);
            Expect("window/one ulong", windowWord, expected);
            Expect("generated", generated, expected);

            //и отдельно - что документ вообще наш: PREFIX пишется и нами тоже
            var exhauster = new PooledUtf8Exhauster(4096);
            PrefixGenerated.Serialize(exhauster, _prefix);
            var ours = Encoding.UTF8.GetString(exhauster.ToArray());
            var theirs = Encoding.UTF8.GetString(_utf8);

            if (ours != theirs)
            {
                throw new InvalidOperationException(
                    "PREFIX: наш документ разошёлся с эталонным:"
                    + Environment.NewLine + "ours: " + ours
                    + Environment.NewLine + "stj : " + theirs
                    );
            }
        }

        private static void Expect(string form, Prefix? actual, long expected)
        {
            if (actual is null)
            {
                throw new InvalidOperationException("PREFIX, форма " + form + ": прочитан null");
            }

            var got = Checksum(actual);
            if (got != expected)
            {
                throw new InvalidOperationException(
                    "PREFIX, форма " + form + ": контрольная сумма " + got + " вместо " + expected
                    );
            }
        }

        /// <summary>
        /// Сумма <b>взвешенная</b>, и это не педантизм: значения членов
        /// различны, поэтому простая сумма пережила бы перестановку - а
        /// перестановка здесь ровно тот сорт ошибки, который и надо ловить.
        /// Спутанные местами <c>case</c> дали бы ту же сумму и другой объект.
        /// </summary>
        private static long Checksum(Prefix value)
        {
            long sum = 0;
            sum += 1L * value.Payload00; sum += 2L * value.Payload01; sum += 3L * value.Payload02;
            sum += 4L * value.Payload03; sum += 5L * value.Payload04; sum += 6L * value.Payload05;
            sum += 7L * value.Payload06; sum += 8L * value.Payload07; sum += 9L * value.Payload08;
            sum += 10L * value.Payload09; sum += 11L * value.Payload10; sum += 12L * value.Payload11;
            sum += 13L * value.Payload12; sum += 14L * value.Payload13; sum += 15L * value.Payload14;
            sum += 16L * value.Payload15; sum += 17L * value.Payload16; sum += 18L * value.Payload17;
            sum += 19L * value.Payload18; sum += 20L * value.Payload19; sum += 21L * value.Payload20;
            sum += 22L * value.Payload21; sum += 23L * value.Payload22; sum += 24L * value.Payload23;
            sum += 25L * value.Payload24; sum += 26L * value.Payload25; sum += 27L * value.Payload26;
            sum += 28L * value.Payload27; sum += 29L * value.Payload28; sum += 30L * value.Payload29;
            return sum;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Baseline = true, Description = "chain: SequenceEqual (как сейчас)")]
        public Prefix? ChainEqual()
        {
            PrefixSerializer.DeserializeChainEqual(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "chain: два ulong вместо вызова")]
        public Prefix? ChainWords()
        {
            PrefixSerializer.DeserializeChainWords(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "window: switch по окну + SequenceEqual")]
        public Prefix? WindowEqual()
        {
            PrefixSerializer.DeserializeWindowEqual(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "window: switch по окну + один ulong")]
        public Prefix? WindowWord()
        {
            PrefixSerializer.DeserializeWindowWord(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (порождённый)")]
        public Prefix? Generated()
        {
            PrefixGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "System.Text.Json (для масштаба)")]
        public Prefix? Stj()
        {
            return JsonSerializer.Deserialize<Prefix>(_utf8);
        }
    }
}
