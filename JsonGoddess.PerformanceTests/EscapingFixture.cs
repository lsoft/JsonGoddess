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
    /// Чего стоит эталонное экранирование - то есть цена обещания фасада
    /// «байты не изменятся» (PLAN.md §15 O9).
    ///
    /// <para>
    /// Меряются два sink'а на одних и тех же входах:
    /// <c>PooledUtf8Exhauster</c> (минимум RFC 8259 §7 - то, что получает тот,
    /// кто позвал JsonGoddess по имени) и <c>CompatUtf8Exhauster</c> (набор
    /// энкодера эталона по умолчанию - то, что получает тот, кто подменил
    /// <c>JsonSerializer</c> фасадом). Рядом обязательно стоит сам эталон:
    /// без него число «мы замедлились в N раз» ничего не говорит о том,
    /// обогнали мы его или нет.
    /// </para>
    ///
    /// <para>
    /// Входов три, и они выбраны не по красоте, а по тому, где проходит
    /// граница поведения. Чистый ASCII - весь идёт одним транскодированием у
    /// обоих. Кириллица - худший случай: у нас по-прежнему одно
    /// транскодирование, у compat'а каждый символ становится шестью байтами.
    /// Смешанный - как выглядит настоящий текст.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class EscapingFixture
    {
        private const string Ascii =
            "Acme Industrial Supplies, order 1042, warehouse 7, back order, bulk pack, no comments";

        private const string Cyrillic =
            "Общество с ограниченной ответственностью «Ромашка», заказ 1042, склад 7, дозаказ";

        private const string Mixed =
            "Acme <b>Industrial</b> & Co, заказ 1042 + доставка, note: 'urgent', warehouse 7";

        private PooledUtf8Exhauster _ours = null!;
        private CompatUtf8Exhauster _compat = null!;

        private Order _order = null!;
        private Order _orderCyrillic = null!;

        private readonly JsonSerializerOptions _referenceDefault = new JsonSerializerOptions();

        [GlobalSetup]
        public void Setup()
        {
            _ours = new PooledUtf8Exhauster(4096);
            _compat = new CompatUtf8Exhauster(4096);

            _order = Order.CreateSample();

            _orderCyrillic = Order.CreateSample();
            _orderCyrillic.Customer = "Общество с ограниченной ответственностью «Ромашка»";
            foreach (var line in _orderCyrillic.Lines!)
            {
                line.Note = "дозаказ, склад 7";
            }

            Verify();
        }

        /// <summary>
        /// Сравнивать разные документы - это не бенчмарк, а совпадение.
        /// Compat-sink обязан совпасть с эталоном <b>по умолчанию</b>, наш
        /// собственный - разойтись с ним на не-ASCII; проверяется и то, и
        /// другое, потому что зелёный замер при разъехавшихся наборах означал
        /// бы, что мерили не то.
        /// </summary>
        private void Verify()
        {
            foreach (var value in new[] { Ascii, Cyrillic, Mixed, })
            {
                _compat.Reset();
                _compat.Append(value);
                var compat = _compat.ToString();
                var theirs = JsonSerializer.Serialize(value, _referenceDefault);

                if (compat != theirs)
                {
                    throw new InvalidOperationException(
                        "CompatUtf8Exhauster diverged from the reference:"
                        + Environment.NewLine + "ours: " + compat
                        + Environment.NewLine + "stj : " + theirs
                        );
                }
            }

            _ours.Reset();
            _ours.Append(Cyrillic);
            if (_ours.ToString() == JsonSerializer.Serialize(Cyrillic, _referenceDefault))
            {
                throw new InvalidOperationException(
                    "PooledUtf8Exhauster no longer differs from the default encoder - "
                    + "then this fixture measures one thing twice."
                    );
            }

            _compat.Reset();
            OrderSerializer.Serialize(_compat, _orderCyrillic);
            var document = Encoding.UTF8.GetString(_compat.ToArray());
            var reference = JsonSerializer.Serialize(_orderCyrillic, _referenceDefault);

            if (document != reference)
            {
                throw new InvalidOperationException(
                    "The compat sink wrote a different document:"
                    + Environment.NewLine + "ours: " + document
                    + Environment.NewLine + "stj : " + reference
                    );
            }
        }

        [BenchmarkCategory("string: ascii")]
        [Benchmark(Baseline = true, Description = "ours (RFC minimum)")]
        public int StringAsciiOurs()
        {
            _ours.Reset();
            _ours.Append(Ascii);
            return _ours.WrittenCount;
        }

        [BenchmarkCategory("string: ascii")]
        [Benchmark(Description = "compat (reference set)")]
        public int StringAsciiCompat()
        {
            _compat.Reset();
            _compat.Append(Ascii);
            return _compat.WrittenCount;
        }

        [BenchmarkCategory("string: ascii")]
        [Benchmark(Description = "System.Text.Json")]
        public string StringAsciiReference() => JsonSerializer.Serialize(Ascii, _referenceDefault);

        [BenchmarkCategory("string: cyrillic")]
        [Benchmark(Baseline = true, Description = "ours (RFC minimum)")]
        public int StringCyrillicOurs()
        {
            _ours.Reset();
            _ours.Append(Cyrillic);
            return _ours.WrittenCount;
        }

        [BenchmarkCategory("string: cyrillic")]
        [Benchmark(Description = "compat (reference set)")]
        public int StringCyrillicCompat()
        {
            _compat.Reset();
            _compat.Append(Cyrillic);
            return _compat.WrittenCount;
        }

        [BenchmarkCategory("string: cyrillic")]
        [Benchmark(Description = "System.Text.Json")]
        public string StringCyrillicReference() => JsonSerializer.Serialize(Cyrillic, _referenceDefault);

        [BenchmarkCategory("string: mixed")]
        [Benchmark(Baseline = true, Description = "ours (RFC minimum)")]
        public int StringMixedOurs()
        {
            _ours.Reset();
            _ours.Append(Mixed);
            return _ours.WrittenCount;
        }

        [BenchmarkCategory("string: mixed")]
        [Benchmark(Description = "compat (reference set)")]
        public int StringMixedCompat()
        {
            _compat.Reset();
            _compat.Append(Mixed);
            return _compat.WrittenCount;
        }

        [BenchmarkCategory("string: mixed")]
        [Benchmark(Description = "System.Text.Json")]
        public string StringMixedReference() => JsonSerializer.Serialize(Mixed, _referenceDefault);

        [BenchmarkCategory("REGULAR: ascii")]
        [Benchmark(Baseline = true, Description = "ours (RFC minimum)")]
        public int DocumentAsciiOurs()
        {
            _ours.Reset();
            OrderSerializer.Serialize(_ours, _order);
            return _ours.WrittenCount;
        }

        [BenchmarkCategory("REGULAR: ascii")]
        [Benchmark(Description = "compat (reference set)")]
        public int DocumentAsciiCompat()
        {
            _compat.Reset();
            OrderSerializer.Serialize(_compat, _order);
            return _compat.WrittenCount;
        }

        [BenchmarkCategory("REGULAR: ascii")]
        [Benchmark(Description = "System.Text.Json")]
        public string DocumentAsciiReference() => JsonSerializer.Serialize(_order, _referenceDefault);

        [BenchmarkCategory("REGULAR: cyrillic")]
        [Benchmark(Baseline = true, Description = "ours (RFC minimum)")]
        public int DocumentCyrillicOurs()
        {
            _ours.Reset();
            OrderSerializer.Serialize(_ours, _orderCyrillic);
            return _ours.WrittenCount;
        }

        [BenchmarkCategory("REGULAR: cyrillic")]
        [Benchmark(Description = "compat (reference set)")]
        public int DocumentCyrillicCompat()
        {
            _compat.Reset();
            OrderSerializer.Serialize(_compat, _orderCyrillic);
            return _compat.WrittenCount;
        }

        [BenchmarkCategory("REGULAR: cyrillic")]
        [Benchmark(Description = "System.Text.Json")]
        public string DocumentCyrillicReference() => JsonSerializer.Serialize(_orderCyrillic, _referenceDefault);
    }
}
