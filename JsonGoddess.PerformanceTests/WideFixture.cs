using System;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// WIDE: 30 членов, все имена одной длины. Форма, на которой проверяется
    /// утверждение "сопоставление имён - главная точка выигрыша на чтении":
    /// здесь switch по длине не отсекает ни одного кандидата, и цепочка
    /// сравнений работает в полную силу. Худший случай для нас и обычный для
    /// STJ, который ищет свойство по хэшу.
    ///
    /// Если на этой форме мы проигрываем, значит диспетчер придётся делать
    /// двухуровневым (switch по первым восьми байтам как ulong) - и вот тогда
    /// это будет решение, принятое по числу, а не по убеждению.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class WideFixture
    {
        private Wide _wide = null!;
        private byte[] _utf8 = null!;
        private string _text = null!;
        private PooledUtf8Exhauster _exhauster = null!;

        [GlobalSetup]
        public void Setup()
        {
            _wide = Wide.CreateSample();
            _exhauster = new PooledUtf8Exhauster(4096);

            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_wide);
            _text = Encoding.UTF8.GetString(_utf8);

            Verify();
        }

        private void Verify()
        {
            _exhauster.Reset();
            WideSerializer.Serialize(_exhauster, _wide);
            var ours = Encoding.UTF8.GetString(_exhauster.ToArray());

            if (ours != _text)
            {
                throw new InvalidOperationException(
                    "JsonGoddess and System.Text.Json produced different WIDE documents:"
                    + Environment.NewLine + "ours: " + ours
                    + Environment.NewLine + "stj : " + _text
                    );
            }

            _exhauster.Reset();
            WideGenerated.Serialize(_exhauster, _wide);
            var generated = Encoding.UTF8.GetString(_exhauster.ToArray());

            if (generated != _text)
            {
                throw new InvalidOperationException(
                    "The generated serializer produced a different WIDE document:"
                    + Environment.NewLine + "generated: " + generated
                    + Environment.NewLine + "stj      : " + _text
                    );
            }

            WideSerializer.DeserializeByLength(DefaultInjector.Instance, _utf8, out var byLength);
            WideSerializer.DeserializeByKey(DefaultInjector.Instance, _utf8, out var byKey);
            WideSerializer.DeserializeByKeyCalls(DefaultInjector.Instance, _utf8, out var byKeyCalls);
            WideSerializer.DeserializeByKeyInlinedCalls(DefaultInjector.Instance, _utf8, out var byKeyInlined);
            WideGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var byGenerator);

            foreach (var back in new[] { byLength, byKey, byKeyCalls, byKeyInlined, byGenerator, })
            {
                if (back is null || back.Field00 != _wide.Field00 || back.Field29 != _wide.Field29
                    || back.Field15 != _wide.Field15 || back.Field28 != _wide.Field28
                    || back.Field01 != _wide.Field01 || back.Field14 != _wide.Field14)
                {
                    throw new InvalidOperationException("JsonGoddess did not read the WIDE document back correctly.");
                }
            }
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public byte[] SerializeStj()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_wide);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public byte[] SerializeStjSourceGenerated()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_wide, OrderJsonContext.Default.Wide);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "Newtonsoft.Json")]
        public string SerializeNewtonsoft()
        {
            return JsonConvert.SerializeObject(_wide);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "JsonGoddess (generated)")]
        public int SerializeJsonGoddessGenerated()
        {
            _exhauster.Reset();
            WideGenerated.Serialize(_exhauster, _wide);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "JsonGoddess (hand-written)")]
        public int SerializeJsonGoddess()
        {
            _exhauster.Reset();
            WideSerializer.Serialize(_exhauster, _wide);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Wide? DeserializeStj()
        {
            return JsonSerializer.Deserialize<Wide>(_utf8);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public Wide? DeserializeStjSourceGenerated()
        {
            return JsonSerializer.Deserialize(_utf8, OrderJsonContext.Default.Wide);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "Newtonsoft.Json")]
        public Wide? DeserializeNewtonsoft()
        {
            return JsonConvert.DeserializeObject<Wide>(_text);
        }

        /// <summary>
        /// Порождённый код. Он и есть предмет измерения: рукописные формы
        /// рядом стоят не как альтернатива, а как верхняя и нижняя границы,
        /// между которыми он обязан оказаться - и не ниже лучшей из них.
        /// </summary>
        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (generated)")]
        public Wide? DeserializeJsonGoddessGenerated()
        {
            WideGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by length)")]
        public Wide? DeserializeJsonGoddessByLength()
        {
            WideSerializer.DeserializeByLength(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by key)")]
        public Wide? DeserializeJsonGoddessByKey()
        {
            WideSerializer.DeserializeByKey(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        /// <summary>
        /// Пара к следующему методу: тот же <c>by key</c>, у которого скаляры
        /// читаются вызовом. Разность этих двух строк - цена невстроенного
        /// вызова на тридцати членах, и получается она внутри одного
        /// round-robin, а не сравнением двух прогонов.
        /// </summary>
        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by key, scalar calls)")]
        public Wide? DeserializeJsonGoddessByKeyCalls()
        {
            WideSerializer.DeserializeByKeyCalls(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by key, scalar calls inlined)")]
        public Wide? DeserializeJsonGoddessByKeyInlinedCalls()
        {
            WideSerializer.DeserializeByKeyInlinedCalls(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }
    }
}
