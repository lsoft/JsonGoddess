using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.Compat.Interop;
using JsonGoddess.PerformanceTests.Bridge;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// Есть ли выигрыш у маршрута B (PLAN.md §10) - моста через
    /// <c>JsonTypeInfo</c>. Вопрос стоит до всякой реализации: маршрут A
    /// работает только там, где вызов виден генератору, а мост открывает
    /// ASP.NET Core, <c>HttpClient.GetFromJsonAsync</c> и любой чужой код,
    /// принимающий <c>JsonSerializerOptions</c>. Платой за это остаётся слой
    /// <c>Utf8JsonWriter</c>/<c>Utf8JsonReader</c>, который никуда не девается.
    ///
    /// <para>
    /// Участников четыре, и три из них - границы. Эталон рефлексивный и он же
    /// в режиме source-gen задают, что мост обязан обогнать. Маршрут A стоит
    /// сверху: это потолок, который мост не перепрыгнет, и расстояние до него
    /// и есть цена моста.
    /// </para>
    ///
    /// <para>
    /// Формы две. Один <c>Order</c> в корне - худший случай для моста: накладные
    /// расходы на вход в <c>JsonSerializer</c> платятся один раз на один
    /// объект. Массив из десяти - то, как выглядит настоящий ответ веб-метода:
    /// эталон владеет массивом, каждый элемент пишем мы.
    /// </para>
    ///
    /// <para>
    /// Асинхронная форма стоит отдельно не ради скорости, а ради вопроса, на
    /// который иначе нет ответа: доезжает ли значение до конвертера одним
    /// куском. Счётчики <c>Contiguous</c>/<c>FellBack</c> печатаются в
    /// <see cref="Verify"/>, и если откат случается всегда, то замеренное
    /// число - это скорость отката, а не моста.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class BridgeFixture
    {
        private const int BatchSize = 10;

        /// <summary>
        /// Кусок потока. Семь байт - не «поменьше для красоты», а размер, при
        /// котором имя свойства заведомо разрезается: самое короткое имя в
        /// <c>Order</c> длиннее.
        /// </summary>
        private const int ChunkSize = 7;

        private static readonly JsonSerializerOptions BridgeOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new OrderBridgeResolver(),
        };

        /// <summary>
        /// Тот же мост, но рукописный: читает прямо из <c>Utf8JsonReader</c>,
        /// имена сверяет целиком.
        ///
        /// <para>
        /// Назывался «верхней границей», пока не выяснилось, что границей он
        /// был по недоразумению: первая редакция разводила члены по длине
        /// имени и байты не сравнивала вовсе, то есть принимала за <c>Id</c>
        /// всякое двухбайтовое имя. Отсюда и бралось её преимущество в 8%,
        /// которое я едва не принял за накладные расходы генератора.
        /// Неправильная программа границей быть не может; теперь он сверяет
        /// имена и стои́т здесь как вторая реализация того же, а не как
        /// недостижимый потолок.
        /// </para>
        ///
        /// <para>
        /// Чего в нём по-прежнему нет - отказов с эталонной формулировкой,
        /// обязательных членов и медленного пути для экранированных и
        /// разрезанных имён. Разница в этом, а не в диспетчере.
        /// </para>
        /// </summary>
        private static readonly JsonSerializerOptions StreamingOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new OrderStreamingBridgeResolver(),
        };

        /// <summary>
        /// Настоящий мост - тот, что напечатает генератор потребителю пакета.
        /// Ради него весь замер и затеян: рукописные формы рядом стоят
        /// границами, а не участниками.
        /// </summary>
        private static readonly JsonSerializerOptions GeneratedOptions =
            new JsonSerializerOptions().UseJsonGoddess();

        private Order _order = null!;
        private Order[] _batch = null!;

        private byte[] _utf8 = null!;
        private byte[] _batchUtf8 = null!;

        private CompatUtf8Exhauster _exhauster = null!;

        /// <summary>
        /// Приёмник для записи в поток. Один на все итерации и обнуляется
        /// длиной, а не пересоздаётся: иначе замер записи мерил бы заодно
        /// выделение буфера <c>MemoryStream</c>.
        /// </summary>
        private MemoryStream _sink = null!;

        [GlobalSetup]
        public void Setup()
        {
            _order = Order.CreateSample();
            _batch = new Order[BatchSize];
            for (var index = 0; index < BatchSize; index++)
            {
                var copy = Order.CreateSample();
                copy.Id += index;
                _batch[index] = copy;
            }

            _exhauster = new CompatUtf8Exhauster(16384);
            _sink = new MemoryStream(16384);

            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_order);
            _batchUtf8 = JsonSerializer.SerializeToUtf8Bytes(_batch);

            Verify();
        }

        /// <summary>
        /// Мост обязан отдавать те же байты, что эталон, и читать те же
        /// значения. Иначе таблица сравнивает не скорости, а две разные
        /// программы.
        /// </summary>
        private void Verify()
        {
            SameBytes("bridge, one", JsonSerializer.SerializeToUtf8Bytes(_order, BridgeOptions), _utf8);
            SameBytes("bridge, batch", JsonSerializer.SerializeToUtf8Bytes(_batch, BridgeOptions), _batchUtf8);

            _exhauster.Reset();
            OrderBridgeHost.Serialize(_exhauster, _order);
            SameBytes("route A, one", _exhauster.ToArray(), _utf8);

            _exhauster.Reset();
            SerializeBatchDirect();
            SameBytes("route A, batch", _exhauster.ToArray(), _batchUtf8);

            OrderBridgeConverter.Contiguous = 0;
            OrderBridgeConverter.FellBack = 0;

            SameOrder("bridge, one", JsonSerializer.Deserialize<Order>(_utf8, BridgeOptions));
            var batch = JsonSerializer.Deserialize<Order[]>(_batchUtf8, BridgeOptions);
            if (batch is null || batch.Length != BatchSize)
            {
                throw new InvalidOperationException("The bridge lost elements of the batch.");
            }

            foreach (var element in batch)
            {
                SameOrderShape("bridge, batch", element);
            }

            SameOrder("bridge, async", DeserializeAsyncBridge().GetAwaiter().GetResult());

            SameBytes("generated bridge, one", JsonSerializer.SerializeToUtf8Bytes(_order, GeneratedOptions), _utf8);
            SameBytes(
                "generated bridge, batch",
                JsonSerializer.SerializeToUtf8Bytes(_batch, GeneratedOptions),
                _batchUtf8
                );

            SameOrder("generated bridge, one", JsonSerializer.Deserialize<Order>(_utf8, GeneratedOptions));

            //Разрезанный поток проверяется отдельно и обязательно: там работает
            //медленный путь имён, и ошибка в нём выглядела бы не как падение, а
            //как молча непрочитанный член
            SameOrder("generated bridge, chunked", DeserializeChunkedBridgeGenerated().GetAwaiter().GetResult());
            SameOrder("reference, chunked", DeserializeChunkedStj().GetAwaiter().GetResult());

            var generatedBatch = JsonSerializer.Deserialize<Order[]>(_batchUtf8, GeneratedOptions);
            if (generatedBatch is null || generatedBatch.Length != BatchSize)
            {
                throw new InvalidOperationException("The generated bridge lost elements of the batch.");
            }

            foreach (var element in generatedBatch)
            {
                SameOrderShape("generated bridge, batch", element);
            }

            SameOrder("streaming bridge, one", JsonSerializer.Deserialize<Order>(_utf8, StreamingOptions));
            var streamingBatch = JsonSerializer.Deserialize<Order[]>(_batchUtf8, StreamingOptions);
            if (streamingBatch is null || streamingBatch.Length != BatchSize)
            {
                throw new InvalidOperationException("The streaming bridge lost elements of the batch.");
            }

            foreach (var element in streamingBatch)
            {
                SameOrderShape("streaming bridge, batch", element);
            }

            OrderBridgeHost.Deserialize(DefaultInjector.Instance, _utf8, out var direct);
            SameOrder("route A, one", direct);

            Console.WriteLine(
                "bridge read: contiguous=" + OrderBridgeConverter.Contiguous
                + " fell back=" + OrderBridgeConverter.FellBack
                );

            if (OrderBridgeConverter.Contiguous == 0)
            {
                throw new InvalidOperationException(
                    "The raw span was never reconstructed: the benchmark would be measuring the fallback."
                    );
            }
        }

        private static void SameBytes(string who, byte[] actual, byte[] expected)
        {
            if (actual.Length != expected.Length)
            {
                throw new InvalidOperationException(
                    who + " produced a document of a different length: "
                    + Encoding.UTF8.GetString(actual)
                    );
            }

            for (var index = 0; index < actual.Length; index++)
            {
                if (actual[index] != expected[index])
                {
                    throw new InvalidOperationException(
                        who + " diverged at byte " + index + ":"
                        + Environment.NewLine + "actual  : " + Encoding.UTF8.GetString(actual)
                        + Environment.NewLine + "expected: " + Encoding.UTF8.GetString(expected)
                        );
                }
            }
        }

        private void SameOrder(string who, Order? actual)
        {
            if (actual is null || actual.Id != _order.Id)
            {
                throw new InvalidOperationException(who + " did not read the document back.");
            }

            SameOrderShape(who, actual);
        }

        private void SameOrderShape(string who, Order? actual)
        {
            if (actual is null
                || actual.Customer != _order.Customer
                || actual.Created != _order.Created
                || actual.Total != _order.Total
                || !actual.Paid
                || actual.Reference != _order.Reference
                || actual.Lines is null
                || actual.Lines.Count != 3
                || actual.Lines[0].Sku != "BRK-0001"
                || actual.Lines[1].Note is not null
                || actual.Lines[2].Note != "bulk pack")
            {
                throw new InvalidOperationException(who + " read the document back incorrectly.");
            }
        }

        private void SerializeBatchDirect()
        {
            //AppendRaw, а не Append(char): Append пишет значение JSON, то есть
            //строку в кавычках, а здесь нужна структура
            _exhauster.AppendRaw("["u8);
            for (var index = 0; index < _batch.Length; index++)
            {
                if (index > 0)
                {
                    _exhauster.AppendRaw(","u8);
                }

                OrderBridgeHost.Serialize(_exhauster, _batch[index]);
            }

            _exhauster.AppendRaw("]"u8);
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
        [Benchmark(Description = "bridge (generated)")]
        public byte[] SerializeBridgeGenerated()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_order, GeneratedOptions);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "bridge (hand-written)")]
        public byte[] SerializeBridge()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_order, BridgeOptions);
        }

        [BenchmarkCategory("serialize")]
        [Benchmark(Description = "facade (route A)")]
        public byte[] SerializeRouteA()
        {
            _exhauster.Reset();
            OrderBridgeHost.Serialize(_exhauster, _order);
            return _exhauster.ToArray();
        }

        [BenchmarkCategory("serialize-batch")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public byte[] SerializeBatchStj()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_batch);
        }

        [BenchmarkCategory("serialize-batch")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public byte[] SerializeBatchStjSourceGenerated()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_batch, OrderJsonContext.Default.OrderArray);
        }

        [BenchmarkCategory("serialize-batch")]
        [Benchmark(Description = "bridge (generated)")]
        public byte[] SerializeBatchBridgeGenerated()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_batch, GeneratedOptions);
        }

        [BenchmarkCategory("serialize-batch")]
        [Benchmark(Description = "bridge (hand-written)")]
        public byte[] SerializeBatchBridge()
        {
            return JsonSerializer.SerializeToUtf8Bytes(_batch, BridgeOptions);
        }

        [BenchmarkCategory("serialize-batch")]
        [Benchmark(Description = "facade (route A)")]
        public byte[] SerializeBatchRouteA()
        {
            _exhauster.Reset();
            SerializeBatchDirect();
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
        [Benchmark(Description = "bridge (route B)")]
        public Order? DeserializeBridge()
        {
            return JsonSerializer.Deserialize<Order>(_utf8, BridgeOptions);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "bridge (generated)")]
        public Order? DeserializeBridgeGenerated()
        {
            return JsonSerializer.Deserialize<Order>(_utf8, GeneratedOptions);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "hand-written, names verified")]
        public Order? DeserializeStreamingBridge()
        {
            return JsonSerializer.Deserialize<Order>(_utf8, StreamingOptions);
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "facade (route A)")]
        public Order? DeserializeRouteA()
        {
            OrderBridgeHost.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        /// <summary>
        /// Из чего складывается чтение через мост. Конвертер обязан сначала
        /// узнать, где кончается значение, - а узнать это можно только пройдя
        /// его токенизатором эталона целиком. Эта строка показывает цену
        /// одного лишь такого прохода, до всякого разбора: если она
        /// сопоставима с полным чтением маршрута A, то мост платит за документ
        /// дважды, и никакая оптимизация разбора этого не исправит.
        /// </summary>
        [BenchmarkCategory("read-cost")]
        [Benchmark(Baseline = true, Description = "Utf8JsonReader.Skip only")]
        public long SkipOnly()
        {
            var reader = new Utf8JsonReader(_utf8);
            reader.Read();
            reader.Skip();
            return reader.BytesConsumed;
        }

        [BenchmarkCategory("read-cost")]
        [Benchmark(Description = "Utf8JsonReader.Read loop only")]
        public long TokenizeOnly()
        {
            var reader = new Utf8JsonReader(_utf8);
            var tokens = 0L;
            while (reader.Read())
            {
                tokens++;
            }

            return tokens;
        }

        [BenchmarkCategory("read-cost")]
        [Benchmark(Description = "facade (route A), whole read")]
        public Order? ReadCostRouteA()
        {
            OrderBridgeHost.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize-batch")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public Order[]? DeserializeBatchStj()
        {
            return JsonSerializer.Deserialize<Order[]>(_batchUtf8);
        }

        [BenchmarkCategory("deserialize-batch")]
        [Benchmark(Description = "System.Text.Json srcgen")]
        public Order[]? DeserializeBatchStjSourceGenerated()
        {
            return JsonSerializer.Deserialize(_batchUtf8, OrderJsonContext.Default.OrderArray);
        }

        [BenchmarkCategory("deserialize-batch")]
        [Benchmark(Description = "bridge (route B)")]
        public Order[]? DeserializeBatchBridge()
        {
            return JsonSerializer.Deserialize<Order[]>(_batchUtf8, BridgeOptions);
        }

        [BenchmarkCategory("deserialize-batch")]
        [Benchmark(Description = "bridge (generated)")]
        public Order[]? DeserializeBatchBridgeGenerated()
        {
            return JsonSerializer.Deserialize<Order[]>(_batchUtf8, GeneratedOptions);
        }

        [BenchmarkCategory("deserialize-batch")]
        [Benchmark(Description = "hand-written, names verified")]
        public Order[]? DeserializeBatchStreamingBridge()
        {
            return JsonSerializer.Deserialize<Order[]>(_batchUtf8, StreamingOptions);
        }

        [BenchmarkCategory("deserialize-async")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<Order?> DeserializeAsyncStj()
        {
            using (var stream = new MemoryStream(_utf8, writable: false))
            {
                return await JsonSerializer.DeserializeAsync<Order>(stream).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Главный сценарий моста: чтение из потока. Маршрут A сюда не
        /// достаёт вовсе - ему нужен весь документ в памяти одним куском, - и
        /// строка эта в таблице важнее всех остальных.
        /// </summary>
        [BenchmarkCategory("deserialize-async")]
        [Benchmark(Description = "bridge (generated)")]
        public async Task<Order?> DeserializeAsyncBridgeGenerated()
        {
            using (var stream = new MemoryStream(_utf8, writable: false))
            {
                return await JsonSerializer.DeserializeAsync<Order>(stream, GeneratedOptions).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// То же, но поток отдаёт по куску за раз. Форма настоящая, а не
        /// выдуманная: так приезжает тело HTTP-запроса, и читатель эталона
        /// оказывается в многосегментном режиме, где имя свойства может
        /// приехать разрезанным. Здесь и видно, чего стои́т медленный путь
        /// имён.
        /// </summary>
        [BenchmarkCategory("deserialize-chunked")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task<Order?> DeserializeChunkedStj()
        {
            using (var stream = new ChunkedStream(_utf8, ChunkSize))
            {
                return await JsonSerializer.DeserializeAsync<Order>(stream).ConfigureAwait(false);
            }
        }

        [BenchmarkCategory("deserialize-chunked")]
        [Benchmark(Description = "bridge (generated)")]
        public async Task<Order?> DeserializeChunkedBridgeGenerated()
        {
            using (var stream = new ChunkedStream(_utf8, ChunkSize))
            {
                return await JsonSerializer.DeserializeAsync<Order>(stream, GeneratedOptions).ConfigureAwait(false);
            }
        }

        [BenchmarkCategory("serialize-async")]
        [Benchmark(Baseline = true, Description = "System.Text.Json")]
        public async Task SerializeAsyncStj()
        {
            _sink.SetLength(0);
            await JsonSerializer.SerializeAsync(_sink, _order).ConfigureAwait(false);
        }

        [BenchmarkCategory("serialize-async")]
        [Benchmark(Description = "bridge (generated)")]
        public async Task SerializeAsyncBridgeGenerated()
        {
            _sink.SetLength(0);
            await JsonSerializer.SerializeAsync(_sink, _order, GeneratedOptions).ConfigureAwait(false);
        }

        [BenchmarkCategory("deserialize-async")]
        [Benchmark(Description = "bridge (route B)")]
        public Task<Order?> DeserializeAsyncBridgeBenchmark()
        {
            return DeserializeAsyncBridge();
        }

        private async Task<Order?> DeserializeAsyncBridge()
        {
            using (var stream = new MemoryStream(_utf8, writable: false))
            {
                return await JsonSerializer.DeserializeAsync<Order>(stream, BridgeOptions).ConfigureAwait(false);
            }
        }
    }
}
