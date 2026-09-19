using System;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Model;
using JsonGoddess.StreamingPrototype.Generated;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Честный замер гибридной схемы. Три вопроса, и на каждый - своя пара
    /// строк в таблице.
    ///
    /// <list type="number">
    /// <item><b>Не съела ли Try-форма счастливый путь.</b> Порождённый читатель
    /// против ручного Try-читателя на одном объекте, одни и те же правила имён.
    /// Это главный вопрос: без него «мы быстрее эталона» не отличить от
    /// «прототип случайно быстрее нашего же кода».</item>
    /// <item><b>Сколько стои́т нарезка.</b> Тот же Try-читатель по целому буферу
    /// и через трубу кусками по 4 КБ.</item>
    /// <item><b>Что получает ASP.NET.</b> Эталон через трубу - ровно то, что
    /// делает штатный форматтер с .NET 10, - против нас через ту же трубу.</item>
    /// </list>
    ///
    /// <para>
    /// Подача обоим одна и та же: <see cref="Dribble"/> отдаёт по 4 КБ за
    /// обращение синхронно, без потоков и без <c>Pipe</c>. Настоящий
    /// <c>Pipe</c> мерил бы заодно своего писателя и планировщик, а вопрос
    /// здесь про разбор.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    [Orderer(SummaryOrderPolicy.Declared)]
    public class StreamingFixture
    {
        private const int Chunk = 4096;

        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private byte[] _many = Array.Empty<byte>();

        private byte[] _one = Array.Empty<byte>();

        [GlobalSetup]
        public void Setup()
        {
            var many = new Order[1000];

            for (var i = 0; i < many.Length; i++)
            {
                var order = Order.CreateSample();
                order.Id += i;
                order.Customer = "Acme Industrial Supplies, branch " + i;
                order.Total += i;
                many[i] = order;
            }

            _many = Reference.SerializeToUtf8Bytes(many, Web);
            _one = Reference.SerializeToUtf8Bytes(Order.CreateSample(), Web);

            //участники обязаны читать один и тот же документ одинаково -
            //узнать об этом надо до, а не после получаса BenchmarkDotNet
            var mine = Speed.OneShot(_many);
            var theirs = Reference.Deserialize<Order[]>(_many, Web)!;

            if (Reference.Serialize(mine, Web) != Reference.Serialize(theirs, Web))
            {
                throw new InvalidOperationException("участники замера читают по-разному");
            }
        }

        [BenchmarkCategory("one object")]
        [Benchmark(Baseline = true, Description = "read: System.Text.Json")]
        public int StjOne()
        {
            return Reference.Deserialize<Order>(_one, Web)!.Id;
        }

        [BenchmarkCategory("one object")]
        [Benchmark(Description = "read: JsonGoddess, generated")]
        public int GeneratedOne()
        {
            OrderHost.Deserialize(DefaultInjector.Instance, _one, out Order? result);
            return result!.Id;
        }

        [BenchmarkCategory("one object")]
        [Benchmark(Description = "read: JsonGoddess, Try form")]
        public int TryOne()
        {
            var span = _one.AsSpan();
            var context = new JsonParseContext(span);

            try
            {
                var position = 0;
                OrderReader.Order(span, ref position, ref context, true, out var order);
                return order!.Id;
            }
            finally
            {
                context.Release();
            }
        }

        [BenchmarkCategory("1000 objects")]
        [Benchmark(Baseline = true, Description = "read: System.Text.Json, whole buffer")]
        public int StjBuffer()
        {
            return Reference.Deserialize<Order[]>(_many, Web)!.Length;
        }

        [BenchmarkCategory("1000 objects")]
        [Benchmark(Description = "read: System.Text.Json, from a pipe")]
        public async Task<int> StjPipe()
        {
            var read = await Reference.DeserializeAsync<Order[]>(new Dribble(_many, false, Chunk), Web);
            return read!.Length;
        }

        [BenchmarkCategory("1000 objects")]
        [Benchmark(Description = "read: JsonGoddess, whole buffer")]
        public int TryBuffer()
        {
            return Speed.OneShot(_many).Length;
        }

        [BenchmarkCategory("1000 objects")]
        [Benchmark(Description = "read: JsonGoddess, from a pipe")]
        public async Task<int> TryPipe()
        {
            var items = await Driver.ReadOrders(new Dribble(_many, false, Chunk), new DriverStats());
            return items.Length;
        }
    }
}
