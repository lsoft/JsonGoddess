using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Входной форматтер MVC поверх гибридного драйвера.
    ///
    /// <para>
    /// Встать в список <c>MvcOptions.InputFormatters</c> - законный,
    /// документированный способ: штатный <c>SystemTextJsonInputFormatter</c>
    /// лежит в том же списке и ничем не привилегирован. На типах, которых мы не
    /// обслуживаем, <see cref="CanReadType"/> отвечает «нет», и MVC молча идёт к
    /// следующему форматтеру - то же отступление, что и везде в проекте.
    /// </para>
    ///
    /// <para>
    /// Тело читается через <c>Request.BodyReader</c> - ту же трубу, которой
    /// пользуется штатный форматтер начиная с .NET 10.
    /// </para>
    /// </summary>
    public sealed class StreamingInputFormatter : InputFormatter
    {
        /// <summary>
        /// Сколько раз нас позвали. Нужен проверке: без него «мы быстрее»
        /// нельзя отличить от «нас не спросили».
        /// </summary>
        public static int Invocations;

        public static DriverStatsSnapshot? Last;

        public StreamingInputFormatter()
        {
            SupportedMediaTypes.Add("application/json");
            SupportedMediaTypes.Add("text/json");
        }

        protected override bool CanReadType(Type type)
        {
            return type == typeof(Order[]) || type == typeof(List<Order>);
        }

        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            Interlocked.Increment(ref Invocations);

            var stats = new DriverStats();

            try
            {
                var items = await Driver.ReadOrders(
                    context.HttpContext.Request.BodyReader,
                    stats,
                    Driver.DefaultCap,
                    context.HttpContext.RequestAborted
                    );

                Last = new DriverStatsSnapshot(stats.Reads, stats.Retries, stats.Gathers, stats.LargestWindow);

                //массив отдаётся КАК ЕСТЬ - он уже нужного размера. Копия
                //здесь стоила бы ровно того, ради чего заведён OrderBuilder.
                object model = context.ModelType == typeof(Order[])
                    ? items
                    : new List<Order>(items);

                return InputFormatterResult.Success(model);
            }
            catch (JsonDocumentException error)
            {
                //штатный форматтер кладёт ошибку в ModelState с ключом из
                //JsonException.Path; у нас пути пока нет - см. список находок
                context.ModelState.TryAddModelError(string.Empty, error.Message);

                return InputFormatterResult.Failure();
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    public sealed record DriverStatsSnapshot(int Reads, int Retries, int Gathers, int LargestWindow);
}
