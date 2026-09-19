using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Model;
using Microsoft.Extensions.Hosting;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Гибридная схема внутри настоящего ASP.NET Core: тот же запрос, тот же
    /// конвейер, разница в одной строке настройки.
    ///
    /// <para>
    /// Ответ намеренно крошечный - контроллер отдаёт <c>int</c>. Мерить надо
    /// разбор тела, а вернув прочитанное, мы намерили бы заодно и запись,
    /// которая у нас и так вдвое дешевле, - и число получилось бы красивее
    /// правды.
    /// </para>
    ///
    /// <para>
    /// Три размера тела не для полноты: на одном объекте доля JSON в запросе
    /// исчезающая, на тысяче - основная, на десяти тысячах (3.7 МБ) виден
    /// вопрос о памяти, ради которого схема и затевалась.
    /// </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    [Orderer(SummaryOrderPolicy.Declared)]
    public class WebFixture
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private IHost? _stock;

        private IHost? _ours;

        private HttpClient? _stockClient;

        private HttpClient? _oursClient;

        private byte[] _one = Array.Empty<byte>();

        private byte[] _many = Array.Empty<byte>();

        private byte[] _huge = Array.Empty<byte>();

        [GlobalSetup]
        public void Setup()
        {
            _stock = Pipeline.Start(false).GetAwaiter().GetResult();
            _ours = Pipeline.Start(true).GetAwaiter().GetResult();

            _stockClient = Pipeline.Client(_stock);
            _oursClient = Pipeline.Client(_ours);

            _one = Payload(1);
            _many = Payload(1000);
            _huge = Payload(10000);

            //участники обязаны отвечать одинаково - узнать об этом надо до
            //получаса BenchmarkDotNet, а не после
            foreach (var body in new[] { _one, _many, _huge, })
            {
                var theirs = Post(_stockClient!, body).GetAwaiter().GetResult();
                var mine = Post(_oursClient!, body).GetAwaiter().GetResult();

                if (theirs != mine)
                {
                    throw new InvalidOperationException("участники замера отвечают по-разному: " + theirs + " / " + mine);
                }
            }

            if (StreamingInputFormatter.Invocations == 0)
            {
                throw new InvalidOperationException("наш форматтер ни разу не позвали");
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _stockClient?.Dispose();
            _oursClient?.Dispose();
            _stock?.Dispose();
            _ours?.Dispose();
        }

        private static byte[] Payload(int count)
        {
            var many = new Order[count];

            for (var i = 0; i < count; i++)
            {
                var order = Order.CreateSample();
                order.Id += i;
                order.Customer = "Acme Industrial Supplies, branch " + i;
                order.Total += i;
                many[i] = order;
            }

            return Reference.SerializeToUtf8Bytes(many, Web);
        }

        private static async Task<string> Post(HttpClient client, byte[] body)
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await client.PostAsync("/count", content);

            return (int)response.StatusCode + ":" + await response.Content.ReadAsStringAsync();
        }

        [BenchmarkCategory("post: one object")]
        [Benchmark(Baseline = true, Description = "ASP.NET as it ships")]
        public Task<string> StockOne() => Post(_stockClient!, _one);

        [BenchmarkCategory("post: one object")]
        [Benchmark(Description = "+ JsonGoddess streaming formatter")]
        public Task<string> OursOne() => Post(_oursClient!, _one);

        [BenchmarkCategory("post: 1000 objects")]
        [Benchmark(Baseline = true, Description = "ASP.NET as it ships")]
        public Task<string> StockMany() => Post(_stockClient!, _many);

        [BenchmarkCategory("post: 1000 objects")]
        [Benchmark(Description = "+ JsonGoddess streaming formatter")]
        public Task<string> OursMany() => Post(_oursClient!, _many);

        [BenchmarkCategory("post: 10000 objects")]
        [Benchmark(Baseline = true, Description = "ASP.NET as it ships")]
        public Task<string> StockHuge() => Post(_stockClient!, _huge);

        [BenchmarkCategory("post: 10000 objects")]
        [Benchmark(Description = "+ JsonGoddess streaming formatter")]
        public Task<string> OursHuge() => Post(_oursClient!, _huge);
    }
}
