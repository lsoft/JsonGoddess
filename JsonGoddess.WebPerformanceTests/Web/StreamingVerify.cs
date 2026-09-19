using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.WebPerformanceTests.Web
{
    /// <summary>
    /// Порождённый входной форматтер против штатного - в настоящем приложении
    /// ASP.NET Core (PLAN.md §12.9, фаза 10, пункт 6г).
    ///
    /// <para>
    /// Прототип проверял <b>рукописный</b> форматтер над рукописным хостом;
    /// здесь проверяется то, что печатает генератор, и над тем хостом, от
    /// которого он его печатает, - веб-профилем моста. Это разные утверждения:
    /// набор стражей у профиля свой, и именно он решает, совпадём ли мы с
    /// эталоном на <c>01</c> и подобном.
    /// </para>
    /// </summary>
    internal static class StreamingVerify
    {
        internal static int Failures;

        internal static async Task All()
        {
            Console.WriteLine("=== Порождённый форматтер: тот же ответ, тот же отказ ===");

            await using var stock = await Pipeline.StartAsync(goddess: false);
            await using var ours = await Pipeline.StartAsync(goddess: false, streaming: true);

            Check(Registered(ours), "форматтер зарегистрирован первым");

            await SameAnswer(stock, ours, 1);
            await SameAnswer(stock, ours, 200);
            await SameAnswer(stock, ours, 1000);

            await SameOne(stock, ours);

            await SameRefusal(stock, ours, "/mvc/orders", "[{\"id\":1,}]", "висячая запятая");
            await SameRefusal(stock, ours, "/mvc/orders", "[{\"id\":01}]", "ведущий ноль");
            await SameRefusal(stock, ours, "/mvc/orders", "[{\"id\":1}", "обрыв");
            await SameRefusal(stock, ours, "/mvc/orders", string.Empty, "пустое тело");
            await SameRefusal(stock, ours, "/mvc/order", "{\"id\":01}", "объект: ведущий ноль");
            await SameRefusal(
                stock, ours, "/mvc/order",
                "{\"id\":1,\"lines\":[{\"sku\":\"a\",\"quantity\":1,\"price\":1},{\"sku\":\"b\",\"quantity\":2,\"price\":01}]}",
                "объект: ведущий ноль во втором элементе"
                );

            await OtherCharsetGoesToTheStockFormatter(ours);

            Console.WriteLine();
        }

        /// <summary>
        /// Форматтер обязан стоять <b>первым</b>: они опрашиваются по порядку, и
        /// штатный json-форматтер согласится читать всё, до чего мы не
        /// дотянулись.
        /// </summary>
        private static bool Registered(Pipeline pipeline)
        {
            return pipeline.InputFormatters.Count > 0
                && pipeline.InputFormatters[0].GetType().Name == "JsonGoddessStreamingInputFormatter";
        }

        private static byte[] Payload(int count)
        {
            var many = new Order[count];

            for (var i = 0; i < count; i++)
            {
                var order = Order.CreateSample();
                order.Id += i;
                order.Customer = "Acme Industrial Supplies, branch " + i;
                many[i] = order;
            }

            return JsonSerializer.SerializeToUtf8Bytes(many, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        private static async Task<(HttpStatusCode Status, string Body)> Post(
            Pipeline pipeline,
            byte[] body,
            string path,
            string contentType = "application/json"
            )
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);

            using var response = await pipeline.Client.PostAsync(path, content);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task SameAnswer(Pipeline stock, Pipeline ours, int count)
        {
            var body = Payload(count);

            var theirs = await Post(stock, body, "/mvc/orders");
            var mine = await Post(ours, body, "/mvc/orders");

            Check(
                theirs.Status == mine.Status && theirs.Body == mine.Body,
                "тело из " + count + " объектов (" + body.Length + " Б): " + theirs.Body + " / " + mine.Body
                );
        }

        private static async Task SameOne(Pipeline stock, Pipeline ours)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(
                Order.CreateSample(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                );

            var theirs = await Post(stock, body, "/mvc/order");
            var mine = await Post(ours, body, "/mvc/order");

            Check(
                theirs.Status == mine.Status && theirs.Body == mine.Body,
                "корневой объект: " + theirs.Body + " / " + mine.Body
                );
        }

        private static async Task SameRefusal(
            Pipeline stock,
            Pipeline ours,
            string path,
            string text,
            string what
            )
        {
            var body = Encoding.UTF8.GetBytes(text);

            var theirs = await Post(stock, body, path);
            var mine = await Post(ours, body, path);

            //Сверяются код и КЛЮЧИ ModelState - то есть то, что клиент
            //разбирает машинно. Тексты сообщений у нас свои и совпадать не
            //обязаны: объявленное расхождение, а не недосмотр.
            var theirKeys = Keys(theirs.Body);
            var myKeys = Keys(mine.Body);

            Check(
                theirs.Status == mine.Status && theirKeys == myKeys,
                what + ": код " + (int)theirs.Status + "/" + (int)mine.Status
                + ", ключи «" + theirKeys + "» / «" + myKeys + "»"
                );
        }

        /// <summary>
        /// Чужая кодировка - не наше дело: читатель работает по UTF-8, а
        /// эталонный форматтер умеет и другие. Запрос обязан дойти до штатного и
        /// быть прочитанным, а не отвергнутым.
        /// </summary>
        private static async Task OtherCharsetGoesToTheStockFormatter(Pipeline ours)
        {
            var body = Encoding.Unicode.GetBytes("[]");
            var answer = await Post(ours, body, "/mvc/orders", "application/json; charset=utf-16");

            Check(
                answer.Status == HttpStatusCode.OK && answer.Body == "0",
                "utf-16 дочитал штатный форматтер: код " + (int)answer.Status + ", ответ " + answer.Body
                );
        }

        private static string Keys(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty("errors", out var errors))
                {
                    return "<нет errors>";
                }

                var names = new List<string>();

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

        private static void Check(bool ok, string what)
        {
            if (!ok)
            {
                Failures++;
            }

            Console.WriteLine("  " + (ok ? "ок  " : "ПЛОХО") + " " + what);
        }
    }
}
