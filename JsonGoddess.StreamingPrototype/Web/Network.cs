using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Единственный стенд проекта, где есть настоящий сокет.
    ///
    /// <para>
    /// Все прочие числа - процессорные, и на них не виден эффект, ради которого
    /// гибрид вообще затевался: эталон разбирает тело <b>по мере прибытия</b> и
    /// потому амортизирует разбор о сетевое ожидание. Схема «дождаться всего и
    /// разобрать» этого не умеет и по настенным часам может оказаться позже,
    /// хотя разбирает вдвое быстрее. Проверяется это только так - Kestrel на
    /// петле и клиент, который шлёт тело порциями с паузами.
    /// </para>
    ///
    /// <para>
    /// Петля - не настоящая сеть, и полосу она не ограничивает; ограничивает её
    /// клиент, и это честнее, чем эмулировать канал внутри сервера: пауза между
    /// порциями - ровно то, что делает медленный отправитель.
    /// </para>
    /// </summary>
    internal static class Network
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        internal static async Task All()
        {
            Console.WriteLine("=== 9. Настоящий сокет: совмещается ли разбор с приёмом ===");

            using var stock = await Start(false);
            using var ours = await Start(true);

            var body = Payload(1000);

            Console.WriteLine("  тело " + body.Length + " Б");
            Console.WriteLine();
            Console.WriteLine("    как шлёт клиент          ASP.NET как есть   + гибрид   отношение");

            //Порции и паузы, а не «полоса»: Task.Delay(1) на Windows спит
            //около 11 мс, и всякое вычисленное МБ/с было бы враньём. Здесь
            //важен не абсолют, а то, как меняется ОТНОШЕНИЕ по мере того, как
            //приём растягивается во времени.
            var modes = new (string What, int Chunk, int Pause)[]
            {
                ("одной порцией", int.MaxValue, 0),
                ("по 4 КБ, без пауз", 4096, 0),
                ("по 4 КБ, пауза 1 мс", 4096, 1),
                ("по 4 КБ, пауза 5 мс", 4096, 5),
            };

            foreach (var mode in modes)
            {
                var theirs = await Measure(stock, body, mode.Chunk, mode.Pause);
                var mine = await Measure(ours, body, mode.Chunk, mode.Pause);

                Console.WriteLine(
                    "    " + mode.What.PadRight(24)
                    + (theirs.ToString("F1") + " мс").PadLeft(15)
                    + (mine.ToString("F1") + " мс").PadLeft(12)
                    + ("   " + (mine / theirs).ToString("F2")).PadLeft(10)
                    );
            }

            Console.WriteLine();
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

        private static async Task<IHost> Start(bool goddess)
        {
            var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseKestrel();
                    web.UseUrls("http://127.0.0.1:0");

                    web.ConfigureServices(services =>
                    {
                        var mvc = services.AddControllers(options =>
                        {
                            if (goddess)
                            {
                                options.InputFormatters.Insert(0, new StreamingInputFormatter());
                            }
                        });

                        mvc.AddApplicationPart(typeof(OrdersController).Assembly);
                    });

                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
                })
                .StartAsync();

            return host;
        }

        private static string Address(IHost host)
        {
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();

            return addresses!.Addresses.First();
        }

        /// <summary>
        /// Медиана нескольких прогонов. Среднее здесь врёт: первый запрос
        /// платит за прогрев конвейера, а один выброс на петле стои́т
        /// миллисекунд.
        /// </summary>
        private static async Task<double> Measure(IHost host, byte[] body, int chunk, int pause)
        {
            using var client = new HttpClient { BaseAddress = new Uri(Address(host)), };

            //прогрев
            for (var i = 0; i < 3; i++)
            {
                await Post(client, body, chunk, pause);
            }

            const int Rounds = 9;
            var times = new List<double>(Rounds);
            var watch = new Stopwatch();

            for (var i = 0; i < Rounds; i++)
            {
                watch.Restart();
                await Post(client, body, chunk, pause);
                watch.Stop();

                times.Add(watch.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            return times[times.Count / 2];
        }

        private static async Task Post(HttpClient client, byte[] body, int chunk, int pause)
        {
            using var content = new Throttled(body, chunk, pause);
            using var response = await client.PostAsync("/count", content);

            response.EnsureSuccessStatusCode();
        }

        /// <summary>Тело порциями с паузой между ними.</summary>
        private sealed class Throttled : HttpContent
        {
            private readonly byte[] _body;

            private readonly int _chunk;

            private readonly int _pause;

            internal Throttled(byte[] body, int chunk, int pause)
            {
                _body = body;
                _chunk = chunk;
                _pause = pause;

                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            {
                for (var offset = 0; offset < _body.Length; offset += _chunk)
                {
                    var length = Math.Min(_chunk, _body.Length - offset);

                    await stream.WriteAsync(_body.AsMemory(offset, length));
                    await stream.FlushAsync();

                    if (_pause > 0)
                    {
                        await Task.Delay(_pause);
                    }
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _body.Length;
                return true;
            }
        }
    }
}
