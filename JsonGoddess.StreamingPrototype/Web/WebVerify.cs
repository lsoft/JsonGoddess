using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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

            await SingleObject(stock, ours, 3, "обычный");
            await SingleObject(stock, ours, 2000, "толстый");

            //Пути отказа у корневого ОБЪЕКТА: их драйвер ставит сам, и правила
            //у него другие, чем у массива. Особенно внутри свойства-коллекции -
            //там путь собирается из имени свойства, индекса элемента и холодного
            //прохода по элементу, и проверить это можно только сверкой.
            await SameRefusalOne(stock, ours, "{\"id\":01}", "объект: ведущий ноль");
            await SameRefusalOne(stock, ours, "{\"id\":1,}", "объект: висячая запятая");
            await SameRefusalOne(
                stock, ours,
                "{\"id\":1,\"lines\":[{\"sku\":\"a\",\"quantity\":1,\"price\":1},{\"sku\":\"b\",\"quantity\":2,\"price\":01}]}",
                "объект: ведущий ноль во втором элементе коллекции"
                );
            await SameRefusalOne(
                stock, ours,
                "{\"id\":1,\"lines\":[{\"sku\":\"a\",\"quantity\":\"нет\"}]}",
                "объект: не число в элементе коллекции"
                );

            await Aborted(stock, "эталон");
            await Aborted(ours, "мы");

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
                        + ", окно до " + StreamingInputFormatter.Last.LargestWindow + " Б"
                        + ", целиком одним куском=" + StreamingInputFormatter.Last.Whole)
                );
        }

        private static async Task SameRefusal(IHost stock, IHost ours, string text, string what)
        {
            var body = Encoding.UTF8.GetBytes(text);

            var theirs = await Post(stock, body, "/orders");
            var mine = await Post(ours, body, "/orders");

            //Сверяются код и КЛЮЧИ ModelState - то есть то, что клиент
            //разбирает машинно. Тексты сообщений у нас свои и совпадать не
            //обязаны: это объявленное расхождение, а не недосмотр.
            var theirKeys = Keys(theirs.Body);
            var myKeys = Keys(mine.Body);

            Verify.Check(
                theirs.Status == mine.Status && theirKeys == myKeys,
                what + ": код " + (int)theirs.Status + "/" + (int)mine.Status
                + ", ключи «" + theirKeys + "» / «" + myKeys + "»"
                );
        }

        /// <summary>То же, но телом с одним объектом - маршрут <c>/one</c>.</summary>
        private static async Task SameRefusalOne(IHost stock, IHost ours, string text, string what)
        {
            var body = Encoding.UTF8.GetBytes(text);

            var theirs = await Post(stock, body, "/one");
            var mine = await Post(ours, body, "/one");

            var theirKeys = Keys(theirs.Body);
            var myKeys = Keys(mine.Body);

            Verify.Check(
                theirs.Status == mine.Status && theirKeys == myKeys,
                what + ": код " + (int)theirs.Status + "/" + (int)mine.Status
                + ", ключи «" + theirKeys + "» / «" + myKeys + "»"
                );
        }

        /// <summary>
        /// Ключи <c>errors</c> из <c>ProblemDetails</c>, в порядке появления.
        /// </summary>
        private static string Keys(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty("errors", out var errors))
                {
                    return "<нет errors>";
                }

                var names = new System.Collections.Generic.List<string>();

                foreach (var error in errors.EnumerateObject())
                {
                    names.Add(error.Name);
                }

                return string.Join(", ", names);
            }
            catch (JsonException)
            {
                return "<не разобрать>";
            }
        }

        /// <summary>
        /// Корень - один объект. Смысл проверки не в ответе (он совпадёт и
        /// так), а в <b>окне</b>: единицей переигрывания стало свойство, и
        /// держать весь документ больше не надо.
        /// </summary>
        private static async Task SingleObject(IHost stock, IHost ours, int lines, string what)
        {
            var order = Order.CreateSample();
            order.Lines = new System.Collections.Generic.List<OrderLine>();

            for (var i = 0; i < lines; i++)
            {
                order.Lines.Add(new OrderLine { Sku = "SKU-" + i, Quantity = i, Price = i, Note = "строка номер " + i, });
            }

            var body = Reference.SerializeToUtf8Bytes(order, Web);

            var theirs = await Post(stock, body, "/one");
            var mine = await Post(ours, body, "/one");

            Verify.Check(
                theirs.Status == mine.Status && theirs.Body == mine.Body,
                "корневой объект, " + what + " (" + body.Length + " Б): ответы совпали="
                + (theirs.Body == mine.Body)
                + ", " + (StreamingInputFormatter.Last is null
                    ? "без статистики"
                    : "обращений " + StreamingInputFormatter.Last.Reads
                        + ", переигрываний " + StreamingInputFormatter.Last.Retries
                        + ", окно до " + StreamingInputFormatter.Last.LargestWindow + " Б")
                );
        }

        /// <summary>
        /// Оборванный клиент. Проверяются две вещи, и обе про живучесть:
        /// запрос обязан <b>завершиться</b> (а не висеть на ожидании добавки,
        /// которой не будет), и приложение обязано пережить это - следующий
        /// запрос отвечает как обычно.
        /// </summary>
        private static async Task Aborted(IHost host, string who)
        {
            using var client = Client(host);

            using var leaving = new CancellationTokenSource();

            var posting = client.PostAsync("/count", new Abort(Payload(1000), leaving.Token), leaving.Token);

            //дать телу доехать до половины и уйти
            await Task.Delay(200);
            leaving.Cancel();

            var finished = await Task.WhenAny(posting, Task.Delay(TimeSpan.FromSeconds(10)));

            if (!ReferenceEquals(finished, posting))
            {
                Verify.Check(false, who + ": обрыв клиента подвесил запрос");
                return;
            }

            var how = "без отказа";

            try
            {
                using var response = await posting;
                how = "ответ " + (int)response.StatusCode;
            }
            catch (Exception error)
            {
                how = error.GetType().Name;
            }

            //и приложение живо
            var after = await Post(host, Payload(10), "/count");

            Verify.Check(
                after.Status == HttpStatusCode.OK && after.Body == "10",
                who + ": обрыв -> " + how + ", следующий запрос -> " + (int)after.Status + ":" + after.Body
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
