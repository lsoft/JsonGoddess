using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Встраивание в настоящий ASP.NET Core: два приложения, различающиеся
    /// одной строкой, и сравнение ответов байт в байт.
    /// </summary>
    internal static class WebVerify
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        internal static async Task All()
        {
            Console.WriteLine("=== 6. Внутри ASP.NET Core: тот же ответ, тот же отказ ===");

            using var stock = await Start(false);
            using var ours = await Start(true);

            await SameAnswer(stock, ours, 1);
            await SameAnswer(stock, ours, 200);
            await SameAnswer(stock, ours, 1000);

            //тело на 3.7 МБ - ради одного числа: окна. Оно обязано остаться
            //размером с элемент, а не с тело
            await SameAnswer(stock, ours, 10000);

            await SameRefusal(stock, ours, "[{\"id\":1,}]", "висячая запятая");
            await SameRefusal(stock, ours, "[{\"id\":01}]", "ведущий ноль");
            await SameRefusal(stock, ours, "[{\"id\":1}", "обрыв");
            await SameRefusal(stock, ours, string.Empty, "пустое тело");
            await SameRefusal(stock, ours, "не json вовсе", "мусор");

            await NotServed(ours);
        }

        private static Task<IHost> Start(bool goddess) => Pipeline.Start(goddess);

        private static HttpClient Client(IHost host) => Pipeline.Client(host);

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

        private static async Task<(HttpStatusCode Status, string Body)> Post(IHost host, byte[] body, string path)
        {
            using var client = Client(host);
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var response = await client.PostAsync(path, content);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task SameAnswer(IHost stock, IHost ours, int count)
        {
            var body = Payload(count);

            var before = StreamingInputFormatter.Invocations;

            var theirs = await Post(stock, body, "/orders");
            var mine = await Post(ours, body, "/orders");

            var called = StreamingInputFormatter.Invocations > before;

            Verify.Check(
                theirs.Status == mine.Status && theirs.Body == mine.Body && called,
                "тело из " + count + " объектов (" + body.Length + " Б): ответы совпали="
                + (theirs.Body == mine.Body) + ", форматтер позвали=" + called
                + ", " + (StreamingInputFormatter.Last is null
                    ? "без статистики"
                    : "обращений к трубе " + StreamingInputFormatter.Last.Reads
                        + ", переигрываний " + StreamingInputFormatter.Last.Retries
                        + ", окно до " + StreamingInputFormatter.Last.LargestWindow + " Б")
                );
        }

        private static async Task SameRefusal(IHost stock, IHost ours, string text, string what)
        {
            var body = Encoding.UTF8.GetBytes(text);

            var theirs = await Post(stock, body, "/orders");
            var mine = await Post(ours, body, "/orders");

            Verify.Check(
                theirs.Status == mine.Status,
                what + ": эталон " + (int)theirs.Status + ", мы " + (int)mine.Status
                );
        }

        /// <summary>
        /// Тип, который мы не обслуживаем, обязан молча уйти штатному
        /// форматтеру. Проверяется тем, что запрос всё равно работает.
        /// </summary>
        private static async Task NotServed(IHost ours)
        {
            var body = Reference.SerializeToUtf8Bytes(new[] { 1, 2, 3, }, Web);

            using var client = Client(ours);
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var response = await client.PostAsync("/count", content);

            Verify.Check(
                response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.OK,
                "чужой тип не ломает конвейер: " + (int)response.StatusCode
                );
        }
    }
}
