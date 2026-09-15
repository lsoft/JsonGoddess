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
    /// REGULAR, обе стороны. Baseline - рефлексивный
    /// <c>System.Text.Json</c>; рядом обязательно стоят его source-gen режим и
    /// Newtonsoft, иначе число ни о чём не говорит.
    ///
    /// Каждый участник получает свой естественный вход и отдаёт свой
    /// естественный выход: JsonGoddess и STJ работают по UTF-8, Newtonsoft -
    /// по строке. Приводить Newtonsoft к байтам значило бы мерить не его.
    ///
    /// <see cref="Verify"/> сверяет документы байт в байт до всякого замера:
    /// сравнение разных документов - это не бенчмарк, а совпадение.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class RegularFixture
    {
        private Order _order = null!;
        private byte[] _utf8 = null!;
        private string _text = null!;
        private PooledUtf8Exhauster _exhauster = null!;

        [GlobalSetup]
        public void Setup()
        {
            _order = Order.CreateSample();
            _exhauster = new PooledUtf8Exhauster(4096);

            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_order);
            _text = Encoding.UTF8.GetString(_utf8);

            Verify();
        }

        /// <summary>
        /// Все три сериализатора обязаны выдать один и тот же документ, иначе
        /// сравнивать нечего. Проверяется на настоящем прогоне, а не на глаз.
        /// </summary>
        private void Verify()
        {
            _exhauster.Reset();
            OrderSerializer.Serialize(_exhauster, _order);
            var ours = Encoding.UTF8.GetString(_exhauster.ToArray());

            if (ours != _text)
            {
                throw new InvalidOperationException(
                    "JsonGoddess and System.Text.Json produced different documents:"
                    + Environment.NewLine + "ours: " + ours
                    + Environment.NewLine + "stj : " + _text
                    );
            }

            var stjContext = JsonSerializer.Serialize(_order, OrderJsonContext.Default.Order);
            if (stjContext != _text)
            {
                throw new InvalidOperationException("STJ source-gen produced a different document: " + stjContext);
            }

            _exhauster.Reset();
            OrderGenerated.Serialize(_exhauster, _order);
            var generated = Encoding.UTF8.GetString(_exhauster.ToArray());

            if (generated != _text)
            {
                throw new InvalidOperationException(
                    "The generated serializer produced a different document:"
                    + Environment.NewLine + "generated: " + generated
                    + Environment.NewLine + "stj      : " + _text
                    );
            }

            //все три читателя обязаны прочесть одинаково: иначе замер сравнивал
            //бы не формы, а три разные программы
            OrderSerializer.DeserializeByLength(DefaultInjector.Instance, _utf8, out var byLength);
            OrderSerializer.DeserializeByKey(DefaultInjector.Instance, _utf8, out var byKey);
            OrderGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var byGenerator);

            foreach (var back in new[] { byLength, byKey, byGenerator, })
            {
                if (back is null || back.Id != _order.Id || back.Lines is null || back.Lines.Count != 3
                    || back.Lines[2].Note != "bulk pack" || back.Reference != _order.Reference
                    || back.Created != _order.Created || back.Total != _order.Total || !back.Paid
                    || back.Lines[1].Note is not null || back.Customer != _order.Customer
                    || back.Lines[0].Sku != "BRK-0001" || back.Lines[0].Quantity != 4
                    || back.Lines[0].Price != 129.99m)
                {
                    throw new InvalidOperationException("JsonGoddess did not read the document back correctly.");
                }
            }
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public byte[] SerializeStj()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_order);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public byte[] SerializeStjSourceGenerated()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_order, OrderJsonContext.Default.Order);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "Newtonsoft.Json")]
        public string SerializeNewtonsoft()
        {
            return JsonConvert.SerializeObject(_order);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "JsonGoddess (generated)")]
        public int SerializeJsonGoddessGenerated()
        {
            _exhauster.Reset();
            OrderGenerated.Serialize(_exhauster, _order);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "JsonGoddess (hand-written)")]
        public int SerializeJsonGoddess()
        {
            _exhauster.Reset();
            OrderSerializer.Serialize(_exhauster, _order);
            return _exhauster.WrittenCount;
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "JsonGoddess (generated) -> byte[]")]
        public byte[] SerializeJsonGoddessToArray()
        {
            _exhauster.Reset();
            OrderGenerated.Serialize(_exhauster, _order);
            return _exhauster.ToArray();
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Order? DeserializeStj()
        {
            return JsonSerializer.Deserialize<Order>(_utf8);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public Order? DeserializeStjSourceGenerated()
        {
            return JsonSerializer.Deserialize(_utf8, OrderJsonContext.Default.Order);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "Newtonsoft.Json")]
        public Order? DeserializeNewtonsoft()
        {
            return JsonConvert.DeserializeObject<Order>(_text);
        }

        /// <summary>
        /// Порождённый код. Он и есть предмет измерения: рукописные формы
        /// рядом стоят не как альтернатива, а как верхняя и нижняя границы,
        /// между которыми он обязан оказаться - и не ниже лучшей из них.
        /// </summary>
        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (generated)")]
        public Order? DeserializeJsonGoddessGenerated()
        {
            OrderGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        /// <summary>
        /// Две рукописные формы диспетчера имён стоят рядом намеренно:
        /// сравнивать их между процессами нельзя - абсолютное время уезжает на
        /// проценты само по себе, и разница форм в эту дрейфующую величину
        /// укладывается. Значимо только отношение, снятое внутри одного
        /// прогона.
        /// </summary>
        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by length)")]
        public Order? DeserializeJsonGoddessByLength()
        {
            OrderSerializer.DeserializeByLength(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (by key)")]
        public Order? DeserializeJsonGoddessByKey()
        {
            OrderSerializer.DeserializeByKey(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }
    }
}
