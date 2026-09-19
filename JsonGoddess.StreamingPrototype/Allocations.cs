using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Аллокации за вычетом полезной нагрузки.
    ///
    /// <para>
    /// Общее число аллокаций отвечает на вопрос «сколько стои́т запрос», но не
    /// на вопрос «чей разбор аккуратнее»: львиная доля там - сам объектный
    /// граф, и он у всех участников одинаков по построению. Поэтому здесь
    /// граф измеряется <b>отдельно</b> - построением точно таких же объектов с
    /// точно такими же строками - и вычитается.
    /// </para>
    ///
    /// <para>
    /// Строки создаются заново (<c>new string(span)</c>), а не берутся
    /// готовыми: разбор их аллоцирует, и взяв интернированные литералы, мы
    /// занизили бы нагрузку и завысили бы накладные расходы обоим.
    /// </para>
    /// </summary>
    internal static class Allocations
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static string[] _customers = Array.Empty<string>();

        private static string?[] _notes = Array.Empty<string>();

        private static string[] _skus = Array.Empty<string>();

        private static int _lines;

        internal static async Task All()
        {
            Console.WriteLine("=== 8. Аллокации за вычетом полезной нагрузки ===");

            foreach (var count in new[] { 1, 1000, 10000, })
            {
                await One(count);
            }
        }

        private static async Task One(int count)
        {
            var sample = Order.CreateSample();
            _lines = sample.Lines!.Count;
            _skus = new string[_lines];
            _notes = new string?[_lines];

            for (var j = 0; j < _lines; j++)
            {
                _skus[j] = sample.Lines[j].Sku!;
                _notes[j] = sample.Lines[j].Note;
            }

            _customers = new string[count];
            var many = new Order[count];

            for (var i = 0; i < count; i++)
            {
                var order = Order.CreateSample();
                order.Id += i;
                order.Customer = "Acme Industrial Supplies, branch " + i;
                order.Total += i;
                many[i] = order;
                _customers[i] = order.Customer;
            }

            var body = Reference.SerializeToUtf8Bytes(many, Web);

            var payload = Measure(() => BuildGraph(count));
            var stj = Measure(() => Reference.Deserialize<Order[]>(body, Web));
            var stjPipe = await MeasureAsync(
                async () => (object?)await Reference.DeserializeAsync<Order[]>(new Dribble(body, false, 4096), Web)
                );
            var ours = Measure(() => Speed.OneShot(body));
            var oursPipe = await MeasureAsync(
                async () => (object?)await Driver.ReadOrders(new Dribble(body, false, 4096), new DriverStats())
                );

            Console.WriteLine();
            Console.WriteLine("  объектов " + count + ", тело " + body.Length + " Б");
            Console.WriteLine("    полезная нагрузка (тот же граф руками): " + Text(payload));
            Row("эталон, целый буфер", stj, payload);
            Row("эталон, из трубы", stjPipe, payload);
            Row("мы, целый буфер", ours, payload);
            Row("мы, из трубы", oursPipe, payload);
        }

        private static void Row(string what, long total, long payload)
        {
            var overhead = total - payload;

            Console.WriteLine(
                "    " + what.PadRight(24)
                + " всего " + Text(total).PadLeft(10)
                + "   накладные " + Text(overhead).PadLeft(10)
                + "   (" + (overhead * 100.0 / payload).ToString("F1") + "% от нагрузки)"
                );
        }

        private static string Text(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024.0).ToString("F2") + " МБ";
            }

            return bytes >= 1024 ? (bytes / 1024.0).ToString("F1") + " КБ" : bytes + " Б";
        }

        /// <summary>
        /// Тот же граф, что получается разбором: те же объекты, те же списки,
        /// строки той же длины и каждая своя.
        /// </summary>
        private static Order[] BuildGraph(int count)
        {
            var many = new Order[count];

            for (var i = 0; i < count; i++)
            {
                var lines = new List<OrderLine>();

                for (var j = 0; j < _lines; j++)
                {
                    lines.Add(
                        new OrderLine
                        {
                            Sku = Fresh(_skus[j]),
                            Quantity = j,
                            Price = j,
                            Note = _notes[j] is null ? null : Fresh(_notes[j]!),
                        });
                }

                many[i] = new Order
                {
                    Id = i,
                    Customer = Fresh(_customers[i]),
                    Created = DateTime.UtcNow,
                    Total = i,
                    Paid = true,
                    Reference = Guid.Empty,
                    Lines = lines,
                };
            }

            return many;
        }

        private static string Fresh(string source) => new string(source.AsSpan());

        private static long Measure(Func<object?> action)
        {
            for (var i = 0; i < 20; i++)
            {
                action();
            }

            const int Rounds = 40;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < Rounds; i++)
            {
                action();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / Rounds;
        }

        private static async Task<long> MeasureAsync(Func<Task<object?>> action)
        {
            for (var i = 0; i < 20; i++)
            {
                await action();
            }

            const int Rounds = 40;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < Rounds; i++)
            {
                await action();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / Rounds;
        }
    }
}
