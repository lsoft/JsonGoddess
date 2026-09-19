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
            return type == typeof(Order[]) || type == typeof(List<Order>) || type == typeof(Order);
        }

        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            Interlocked.Increment(ref Invocations);

            var stats = new DriverStats();

            try
            {
                if (context.ModelType == typeof(Order))
                {
                    //корень - один объект: единицей переигрывания становится
                    //свойство, иначе пришлось бы держать всё тело
                    var one = await SingleDriver.ReadOne(
                        context.HttpContext.Request.BodyReader,
                        stats,
                        Driver.DefaultCap,
                        context.HttpContext.RequestAborted
                        );

                    Last = new DriverStatsSnapshot(stats.Reads, stats.Retries, stats.Gathers, stats.LargestWindow);

                    return one is null
                        ? InputFormatterResult.Success(null)
                        : InputFormatterResult.Success(one);
                }

                //Список и массив читает один автомат, и различаются они только
                //тем, во что накопитель отдаёт прочитанное: копия из одного в
                //другое стоила бы ровно того, ради чего накопитель и заведён.
                //
                //Драйвер создаётся здесь, а не точкой входа StreamReadArray_,
                //только ради счётчиков: без них потоковый путь нечем проверять.
                var driver = new Generated.OrderHost.Stream_JsonGoddess_PerformanceTests_Model_Order(
                    DefaultInjector.Instance,
                    asList: context.ModelType != typeof(Order[])
                    );

                var items = await driver.ReadAsync(
                    context.HttpContext.Request.BodyReader,
                    Driver.DefaultCap,
                    context.HttpContext.RequestAborted
                    );

                Last = new DriverStatsSnapshot(
                    driver.Reads, driver.Retries, driver.Gathers, driver.LargestWindow
                    );

                return InputFormatterResult.Success(items);
            }
            catch (JsonDocumentException error)
            {
                //Ключ - путь, ровно как у штатного форматтера, который берёт
                //его из JsonException.Path. Именно ключ клиенты и разбирают в
                //400-м ответе; текст сообщения у нас свой и совпадать с
                //эталонным не обязан.
                context.ModelState.TryAddModelError(error.Path ?? string.Empty, error.Message);

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
