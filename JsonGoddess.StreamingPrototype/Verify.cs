using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Проверки прототипа. Ожидания нигде не записаны литералами - их даёт
    /// <c>System.Text.Json</c> прямо в тесте, как и во всём остальном проекте.
    /// </summary>
    internal static class Verify
    {
        internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Размеры куска, которыми кормим трубу, и размеры её сегментов.
        /// Единица тут не для красоты: при ней граница приходится на каждый
        /// байт, то есть переигрывается всё, что можно переиграть.
        /// </summary>
        private static readonly int[] Chunks = { 1, 2, 3, 7, 13, 64, 1024, 4096, 16 * 1024, int.MaxValue, };

        private static readonly int[] Segments = { 16, 128, 4096, };

        internal static int Failures;

        internal static async Task All()
        {
            await Paths();
            await Differential();
            await ByteAtATime();
            await Escapes();
            await Truncation();
            await Malformed();
            await Empty();
        }

        /// <summary>
        /// Окно кончается на каждом байте - в том числе перед закрывающей
        /// кавычкой, посреди суррогатной пары и между запятой и следующим
        /// именем. Это и есть настоящая проверка корректности; подача через
        /// обычный <c>Pipe</c> её не даёт, потому что писатель обгоняет
        /// читателя и окно приходит большим.
        /// </summary>
        private static async Task ByteAtATime()
        {
            Console.WriteLine("=== 1b. По одному байту за обращение ===");

            var order = Order.CreateSample();
            order.Customer = "кавычка \" слэш \\ юникод é中😀";
            order.Lines![1].Note = "перевод\nстроки";

            foreach (var count in new[] { 1, 3, })
            {
                var many = new Order[count];
                for (var i = 0; i < count; i++)
                {
                    many[i] = order;
                }

                var body = Reference.SerializeToUtf8Bytes(many, Web);
                var expected = Canonical(Reference.Deserialize<Order[]>(body, Web)!);

                foreach (var segmented in new[] { false, true, })
                {
                    var stats = new DriverStats();
                    var pipe = new Dribble(body, segmented);
                    Driver.Trace = false;

                    try
                    {
                        var items = await Driver.ReadOrders(pipe, stats);

                        Check(
                            Canonical(items) == expected,
                            "объектов " + count + ", тело " + body.Length + " Б, "
                            + (segmented ? "сегмент на байт" : "один сегмент")
                            + ": обращений " + pipe.Reads + ", переигрываний " + stats.Retries
                            + ", окно до " + stats.LargestWindow + " Б"
                            );
                    }
                    catch (Exception error)
                    {
                        var around = Math.Max(0, pipe.Start - 20);
                        var length = Math.Min(60, body.Length - around);

                        Check(
                            false,
                            "объектов " + count + ", " + (segmented ? "сегмент на байт" : "один сегмент")
                            + ": " + error.Message
                            + "; потреблено " + pipe.Start + " из " + body.Length
                            + "; вокруг: «" + Encoding.UTF8.GetString(body, around, length).Replace("\n", "\\n") + "»"
                            );
                    }
                }
            }
        }

        internal static void Check(bool condition, string what)
        {
            if (condition)
            {
                Console.WriteLine("  ок   " + what);
                return;
            }

            Failures++;
            Console.WriteLine("  ПЛОХО " + what);
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

        private static async Task<(Order[] Items, DriverStats Stats)> Through(byte[] body, int chunk, int segment)
        {
            var pipe = new Pipe(new PipeOptions(minimumSegmentSize: segment, useSynchronizationContext: false));
            var stats = new DriverStats();

            var reading = Driver.ReadOrders(pipe.Reader, stats);
            var writing = Feed(pipe.Writer, body, chunk);

            try
            {
                var items = await reading;
                await writing;
                return (items, stats);
            }
            catch
            {
                await writing;
                throw;
            }
        }

        private static async Task Feed(PipeWriter writer, byte[] body, int chunk)
        {
            for (var offset = 0; offset < body.Length; offset += chunk)
            {
                var length = Math.Min(chunk, body.Length - offset);
                await writer.WriteAsync(body.AsMemory(offset, length));
            }

            await writer.CompleteAsync();
        }

        private static string Canonical(IEnumerable<Order> items)
        {
            return Reference.Serialize(items, Web);
        }

        /// <summary>
        /// Путь до места отказа. Штатный форматтер MVC кладёт в
        /// <c>ModelState</c> ключ из <c>JsonException.Path</c>, то есть от
        /// этого пути зависит тело 400-го ответа - а оно у клиентов разбирается.
        ///
        /// <para>
        /// Ожидания здесь не записаны литералами: их даёт сам эталон, прямо в
        /// проверке.
        /// </para>
        /// </summary>
        private static async Task Paths()
        {
            Console.WriteLine("=== 0. Путь до места отказа - как у эталона ===");

            var cases = new[]
            {
                "[{\"id\":\"нет\"}]",
                "[{\"id\":1},{\"id\":\"нет\"}]",
                "[{\"id\":1},{\"id\":2},{\"total\":\"нет\"}]",
                "[{\"lines\":[{\"quantity\":\"нет\"}]}]",
                "[{\"id\":1},{\"lines\":[{\"sku\":\"a\"},{\"price\":\"нет\"}]}]",
                "[{\"paid\":\"нет\"}]",
                "[{\"id\":1,}]",
                "[{\"id\":01}]",

                //отказы ВНЕ элемента - здесь путь ставит не наш читатель, а
                //драйвер, и угадывать его нельзя
                string.Empty,
                "   ",
                "не json вовсе",
                "[{\"id\":1}",
                "[{\"id\":1},",
                "[",
                "{\"id\":1}",
                "[1]",
            };

            foreach (var text in cases)
            {
                var body = Encoding.UTF8.GetBytes(text);

                var theirs = "не отказал";
                try
                {
                    Reference.Deserialize<Order[]>(body, Web);
                }
                catch (JsonException error)
                {
                    theirs = error.Path ?? "<null>";
                }

                var ours = "не отказал";
                try
                {
                    await Through(body, int.MaxValue, 4096);
                }
                catch (JsonDocumentException error)
                {
                    ours = error.Path ?? "<null>";
                }

                Check(theirs == ours, text.PadRight(52) + " эталон " + theirs.PadRight(18) + " мы " + ours);
            }
        }

        private static async Task Differential()
        {
            Console.WriteLine("=== 1. Тот же документ при любой нарезке ===");

            var body = Payload(200);
            var expected = Canonical(Reference.Deserialize<Order[]>(body, Web)!);

            foreach (var segment in Segments)
            {
                foreach (var chunk in Chunks)
                {
                    var (items, stats) = await Through(body, chunk, segment);

                    Check(
                        Canonical(items) == expected,
                        "сегмент " + segment + ", куски по " + (chunk == int.MaxValue ? "всё сразу" : chunk.ToString())
                        + "  -> " + stats
                        );
                }
            }
        }

        private static async Task Escapes()
        {
            Console.WriteLine("=== 2. Экранирование и не-ASCII под разрезом ===");

            var order = Order.CreateSample();
            order.Customer = "кавычка \" слэш \\ таб \t юникод é中😀 конец";
            order.Lines![0].Note = "перевод\nстроки и  управляющий";

            var body = Reference.SerializeToUtf8Bytes(new[] { order, order, }, Web);
            var expected = Canonical(Reference.Deserialize<Order[]>(body, Web)!);

            foreach (var chunk in Chunks)
            {
                var (items, _) = await Through(body, chunk, 16);
                Check(Canonical(items) == expected, "куски по " + (chunk == int.MaxValue ? "всё сразу" : chunk.ToString()));
            }
        }

        /// <summary>
        /// Главная проверка схемы: обрезанный документ обязан кончиться
        /// отказом, а не молча принятым результатом. Режем на каждом байте.
        /// </summary>
        private static async Task Truncation()
        {
            Console.WriteLine("=== 3. Обрыв на каждом смещении - отказ, а не тишина ===");

            var body = Payload(3);
            var accepted = new List<int>();

            for (var length = 0; length < body.Length; length++)
            {
                var prefix = new byte[length];
                Array.Copy(body, prefix, length);

                try
                {
                    var (items, _) = await Through(prefix, 7, 16);
                    accepted.Add(length);
                }
                catch (JsonDocumentException)
                {
                }
                catch (Exception error)
                {
                    Failures++;
                    Console.WriteLine("  ПЛОХО обрыв на " + length + ": " + error.GetType().Name + " " + error.Message);
                    break;
                }
            }

            Check(
                accepted.Count == 0,
                "обрезков " + body.Length + ", принято молча " + accepted.Count
                + (accepted.Count == 0 ? string.Empty : " (первые: " + string.Join(", ", accepted.GetRange(0, Math.Min(5, accepted.Count))) + ")")
                );
        }

        private static async Task Malformed()
        {
            Console.WriteLine("=== 4. Кривой документ - отказ при любой нарезке ===");

            var cases = new[]
            {
                "[{\"id\":1,}]",
                "[{\"id\":01}]",
                "[{\"id\":1} {\"id\":2}]",
                "[{\"id\":\"нет\"}]",
                "[{id:1}]",
                "[{\"id\":1}",
                "[tru]",
                "[{\"paid\":tru}]",
                "{\"id\":1}",
            };

            foreach (var text in cases)
            {
                var body = Encoding.UTF8.GetBytes(text);
                var theirs = Rejects(body);
                var ours = true;

                foreach (var chunk in new[] { 1, 3, int.MaxValue, })
                {
                    try
                    {
                        await Through(body, chunk, 16);
                        ours = false;
                    }
                    catch (JsonDocumentException)
                    {
                    }
                }

                Check(ours == theirs, text + "  (эталон отвергает: " + theirs + ", мы: " + ours + ")");
            }
        }

        private static bool Rejects(byte[] body)
        {
            try
            {
                Reference.Deserialize<Order[]>(body, Web);
                return false;
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private static async Task Empty()
        {
            Console.WriteLine("=== 5. Пустой массив и пробелы вокруг ===");

            foreach (var text in new[] { "[]", "  [ ]  ", "[ { } ]", })
            {
                var body = Encoding.UTF8.GetBytes(text);
                var expected = Canonical(Reference.Deserialize<Order[]>(body, Web)!);

                foreach (var chunk in new[] { 1, 2, int.MaxValue, })
                {
                    var (items, _) = await Through(body, chunk, 16);
                    Check(Canonical(items) == expected, "'" + text + "' кусками по " + (chunk == int.MaxValue ? "всё" : chunk.ToString()));
                }
            }
        }
    }
}
