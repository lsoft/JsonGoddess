using System;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// Порог <c>KeySwitchThreshold</c> (§8.2 плана) и вопрос O5 (§15) - на
    /// корзинах из двух, четырёх и восьми членов с именами по семь байт.
    ///
    /// Семь байт выбраны затем, что при такой длине ключ <b>полон</b>: форма
    /// «по ключу» обходится вовсе без сравнения байтов, и порог меряется в
    /// самом чистом виде - таблица переходов против цепочки, без хвостов.
    /// Замер (§12.3 и §12.4 плана) показал, что цепочка выигрывает на всех
    /// трёх корзинах - 1.28, 1.53 и 2.09, - и что прежний порог в четыре члена
    /// заставлял эмиттер печатать ключ там, где он вдвое хуже. Порог поднят до
    /// 24; сам перелом (где-то между 9 и 29) по-прежнему не измерен, и эта
    /// фикстура остаётся местом, где его будут искать.
    ///
    /// Восьмичленная корзина несёт вторую пару - под O5: цепочка
    /// <c>EqualsIgnoreCase</c> (то, что эмиттер печатает сегодня при
    /// включённом <c>CaseInsensitiveNames</c>) против <c>switch</c> по
    /// <b>свёрнутому</b> ключу, который вернул бы таблицу переходов.
    ///
    /// Категории разделены по размеру корзины: сравнивать двойку с восьмёркой
    /// бессмысленно - это разные документы.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class BucketFixture
    {
        private byte[] _two = null!;
        private byte[] _four = null!;
        private byte[] _eight = null!;

        [GlobalSetup]
        public void Setup()
        {
            _two = JsonSerializer.SerializeToUtf8Bytes(Bucket2.CreateSample());
            _four = JsonSerializer.SerializeToUtf8Bytes(Bucket4.CreateSample());
            _eight = JsonSerializer.SerializeToUtf8Bytes(Bucket8.CreateSample());

            Verify();
        }

        private void Verify()
        {
            BucketSerializer.DeserializeChain2(DefaultInjector.Instance, _two, out var chain2);
            BucketSerializer.DeserializeKey2(DefaultInjector.Instance, _two, out var key2);
            Bucket2Generated.Deserialize(DefaultInjector.Instance, _two, out var generated2);

            foreach (var read in new[] { chain2, key2, generated2, })
            {
                if (read is null || read.AlphaA1 != 1 || read.AlphaB2 != 2)
                {
                    throw new InvalidOperationException("BUCKET-2 прочитан неверно");
                }
            }

            BucketSerializer.DeserializeChain4(DefaultInjector.Instance, _four, out var chain4);
            BucketSerializer.DeserializeKey4(DefaultInjector.Instance, _four, out var key4);
            Bucket4Generated.Deserialize(DefaultInjector.Instance, _four, out var generated4);

            foreach (var read in new[] { chain4, key4, generated4, })
            {
                if (read is null || read.AlphaA1 != 1 || read.AlphaB2 != 2 || read.AlphaC3 != 3 || read.AlphaD4 != 4)
                {
                    throw new InvalidOperationException("BUCKET-4 прочитан неверно");
                }
            }

            BucketSerializer.DeserializeChain8(DefaultInjector.Instance, _eight, out var chain8);
            BucketSerializer.DeserializeKey8(DefaultInjector.Instance, _eight, out var key8);
            BucketSerializer.DeserializeFoldChain8(DefaultInjector.Instance, _eight, out var foldChain8);
            BucketSerializer.DeserializeFoldKey8(DefaultInjector.Instance, _eight, out var foldKey8);
            Bucket8Generated.Deserialize(DefaultInjector.Instance, _eight, out var generated8);
            Bucket8FoldGenerated.Deserialize(DefaultInjector.Instance, _eight, out var generatedFold8);

            foreach (var read in new[] { chain8, key8, foldChain8, foldKey8, generated8, generatedFold8, })
            {
                if (read is null
                    || read.AlphaA1 != 1 || read.AlphaB2 != 2 || read.AlphaC3 != 3 || read.AlphaD4 != 4
                    || read.AlphaE5 != 5 || read.AlphaF6 != 6 || read.AlphaG7 != 7 || read.AlphaH8 != 8)
                {
                    throw new InvalidOperationException("BUCKET-8 прочитан неверно");
                }
            }

            //регистронезависимые формы обязаны читать и документ в другом
            //регистре - иначе мерилось бы не то, что заявлено
            var shouted = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(_eight).Replace("alpha", "ALPHA").Replace("Alpha", "ALPHA")
                );

            BucketSerializer.DeserializeFoldChain8(DefaultInjector.Instance, shouted, out var shoutedChain);
            BucketSerializer.DeserializeFoldKey8(DefaultInjector.Instance, shouted, out var shoutedKey);
            Bucket8FoldGenerated.Deserialize(DefaultInjector.Instance, shouted, out var shoutedGenerated);

            foreach (var read in new[] { shoutedChain, shoutedKey, shoutedGenerated, })
            {
                if (read is null || read.AlphaA1 != 1 || read.AlphaH8 != 8)
                {
                    throw new InvalidOperationException(
                        "BUCKET-8: регистронезависимая форма не прочитала имя в другом регистре"
                        );
                }
            }

            //а не-регистронезависимая - обязана НЕ прочитать: если прочитает,
            //значит формы отличаются не тем, чем заявлено
            BucketSerializer.DeserializeKey8(DefaultInjector.Instance, shouted, out var shoutedStrict);
            if (shoutedStrict is null || shoutedStrict.AlphaA1 != 0)
            {
                throw new InvalidOperationException(
                    "BUCKET-8: точная форма приняла имя в другом регистре - формы неразличимы"
                    );
            }
        }

        [BenchmarkCategory("bucket-2")]
        [Benchmark(Baseline = true, Description = "2 члена: цепочка")]
        public Bucket2? Chain2()
        {
            BucketSerializer.DeserializeChain2(DefaultInjector.Instance, _two, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-2")]
        [Benchmark(Description = "2 члена: switch по ключу")]
        public Bucket2? Key2()
        {
            BucketSerializer.DeserializeKey2(DefaultInjector.Instance, _two, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-2")]
        [Benchmark(Description = "2 члена: порождённый (порог печатает цепочку)")]
        public Bucket2? Generated2()
        {
            Bucket2Generated.Deserialize(DefaultInjector.Instance, _two, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-4")]
        [Benchmark(Baseline = true, Description = "4 члена: цепочка")]
        public Bucket4? Chain4()
        {
            BucketSerializer.DeserializeChain4(DefaultInjector.Instance, _four, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-4")]
        [Benchmark(Description = "4 члена: switch по ключу")]
        public Bucket4? Key4()
        {
            BucketSerializer.DeserializeKey4(DefaultInjector.Instance, _four, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-4")]
        [Benchmark(Description = "4 члена: порождённый (порог печатает цепочку)")]
        public Bucket4? Generated4()
        {
            Bucket4Generated.Deserialize(DefaultInjector.Instance, _four, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Baseline = true, Description = "8 членов: цепочка")]
        public Bucket8? Chain8()
        {
            BucketSerializer.DeserializeChain8(DefaultInjector.Instance, _eight, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Description = "8 членов: switch по ключу")]
        public Bucket8? Key8()
        {
            BucketSerializer.DeserializeKey8(DefaultInjector.Instance, _eight, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Description = "8 членов, без учёта регистра: цепочка EqualsIgnoreCase")]
        public Bucket8? FoldChain8()
        {
            BucketSerializer.DeserializeFoldChain8(DefaultInjector.Instance, _eight, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Description = "8 членов, без учёта регистра: switch по свёрнутому ключу")]
        public Bucket8? FoldKey8()
        {
            BucketSerializer.DeserializeFoldKey8(DefaultInjector.Instance, _eight, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Description = "8 членов: порождённый (порог печатает цепочку)")]
        public Bucket8? Generated8()
        {
            Bucket8Generated.Deserialize(DefaultInjector.Instance, _eight, out var result);
            return result;
        }

        [BenchmarkCategory("bucket-8")]
        [Benchmark(Description = "8 членов: порождённый, CaseInsensitiveNames")]
        public Bucket8? GeneratedFold8()
        {
            Bucket8FoldGenerated.Deserialize(DefaultInjector.Instance, _eight, out var result);
            return result;
        }
    }
}
