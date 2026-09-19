using System;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.Compat.Interop;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// Куда уходят 52 ns, на которые порождённый читатель моста отстаёт от
    /// рукописного предела (§12.8).
    ///
    /// <para>
    /// Разница между ними ровно одна: рукописный зовёт
    /// <c>reader.GetInt32()</c> напрямую, порождённый - <c>BridgeRead.Int32</c>,
    /// который сперва сам смотрит на тип токена, а потом вызывает
    /// <c>TryGetInt32</c>, смотрящий на него же. Гипотеза - лишняя ветка на
    /// каждое значение; на REGULAR значений девятнадцать, и 52 ns дают около
    /// 2.7 ns на значение, то есть примерно одну проверку. Похоже на правду -
    /// и потому подлежит проверке, а не принятию.
    /// </para>
    ///
    /// <para>
    /// Замер поставлен так, чтобы кроме чтения значений в нём не было ничего:
    /// один и тот же массив из <see cref="Count"/> элементов, один и тот же
    /// обход токенов, отличается только вызов. Сравнивать надо не «сколько
    /// стои́т чтение», а разницу между двумя столбцами - абсолютные числа тут
    /// включают токенизацию, которая одна на обоих.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    //Дизассемблер здесь не украшение: разница между столбцами - единицы
    //наносекунд, то есть единицы инструкций, и секундомер на таком масштабе
    //отвечает «примерно». Прецедент в проекте есть - границу формы диспетчера
    //имён (§12.6.1) тоже провели по машинному коду, а не по замеру.
    [DisassemblyDiagnoser(maxDepth: 3, printSource: false, exportHtml: false)]
    public class ValueReadFixture
    {
        private const int Count = 32;

        private byte[] _numbers = null!;
        private byte[] _strings = null!;
        private byte[] _decimals = null!;
        private byte[] _dates = null!;
        private byte[] _guids = null!;
        private byte[] _booleans = null!;

        [GlobalSetup]
        public void Setup()
        {
            _numbers = Array("1042");
            _strings = Array("\"Acme Industrial Supplies\"");
            _decimals = Array("1499.95");
            _dates = Array("\"2026-09-14T18:57:54Z\"");
            _guids = Array("\"6f9619ff-8b86-d011-b42d-00cf4fc964ff\"");
            _booleans = Array("true");

            Verify();
        }

        private static byte[] Array(string element)
        {
            var builder = new StringBuilder("[");
            for (var i = 0; i < Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(element);
            }

            return Encoding.UTF8.GetBytes(builder.Append(']').ToString());
        }

        /// <summary>
        /// Оба способа обязаны прочесть одно и то же. Иначе замер сравнивал бы
        /// не две формы одного чтения, а две разные программы.
        /// </summary>
        private void Verify()
        {
            if (Int32ViaHelper() != Int32Direct()
                || DecimalViaHelper() != DecimalDirect()
                || DateTimeViaHelper() != DateTimeDirect()
                || GuidViaHelper() != GuidDirect()
                || BooleanViaHelper() != BooleanDirect()
                || StringViaHelper() != StringDirect())
            {
                throw new InvalidOperationException("the helper and the direct call disagree");
            }
        }

        [BenchmarkCategory("int32")]
        [Benchmark(Baseline = true, Description = "reader.GetInt32")]
        public int Int32Direct()
        {
            var reader = new Utf8JsonReader(_numbers);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetInt32();
            }

            return total;
        }

        [BenchmarkCategory("int32")]
        [Benchmark(Description = "BridgeRead.Int32")]
        public int Int32ViaHelper()
        {
            var reader = new Utf8JsonReader(_numbers);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.Int32(ref reader);
            }

            return total;
        }

        [BenchmarkCategory("string")]
        [Benchmark(Baseline = true, Description = "reader.GetString")]
        public int StringDirect()
        {
            var reader = new Utf8JsonReader(_strings);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetString()!.Length;
            }

            return total;
        }

        [BenchmarkCategory("string")]
        [Benchmark(Description = "BridgeRead.String")]
        public int StringViaHelper()
        {
            var reader = new Utf8JsonReader(_strings);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.String(ref reader)!.Length;
            }

            return total;
        }

        [BenchmarkCategory("decimal")]
        [Benchmark(Baseline = true, Description = "reader.GetDecimal")]
        public decimal DecimalDirect()
        {
            var reader = new Utf8JsonReader(_decimals);
            reader.Read();

            var total = 0m;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetDecimal();
            }

            return total;
        }

        [BenchmarkCategory("decimal")]
        [Benchmark(Description = "BridgeRead.Decimal")]
        public decimal DecimalViaHelper()
        {
            var reader = new Utf8JsonReader(_decimals);
            reader.Read();

            var total = 0m;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.Decimal(ref reader);
            }

            return total;
        }

        [BenchmarkCategory("datetime")]
        [Benchmark(Baseline = true, Description = "reader.GetDateTime")]
        public long DateTimeDirect()
        {
            var reader = new Utf8JsonReader(_dates);
            reader.Read();

            var total = 0L;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetDateTime().Ticks;
            }

            return total;
        }

        [BenchmarkCategory("datetime")]
        [Benchmark(Description = "BridgeRead.DateTime")]
        public long DateTimeViaHelper()
        {
            var reader = new Utf8JsonReader(_dates);
            reader.Read();

            var total = 0L;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.DateTime(ref reader).Ticks;
            }

            return total;
        }

        [BenchmarkCategory("guid")]
        [Benchmark(Baseline = true, Description = "reader.GetGuid")]
        public int GuidDirect()
        {
            var reader = new Utf8JsonReader(_guids);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetGuid().GetHashCode();
            }

            return total;
        }

        [BenchmarkCategory("guid")]
        [Benchmark(Description = "BridgeRead.Guid")]
        public int GuidViaHelper()
        {
            var reader = new Utf8JsonReader(_guids);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.Guid(ref reader).GetHashCode();
            }

            return total;
        }

        [BenchmarkCategory("bool")]
        [Benchmark(Baseline = true, Description = "reader.GetBoolean")]
        public int BooleanDirect()
        {
            var reader = new Utf8JsonReader(_booleans);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += reader.GetBoolean() ? 1 : 0;
            }

            return total;
        }

        [BenchmarkCategory("bool")]
        [Benchmark(Description = "BridgeRead.Boolean")]
        public int BooleanViaHelper()
        {
            var reader = new Utf8JsonReader(_booleans);
            reader.Read();

            var total = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                total += BridgeRead.Boolean(ref reader) ? 1 : 0;
            }

            return total;
        }
    }
}
