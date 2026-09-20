using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.Compat.Interop;
using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JsonGoddess.WebPerformanceTests.Web
{
    /// <summary>
    /// Приложение ASP.NET Core целиком, поднятое в этом же процессе. Их два -
    /// с мостом и без, - и отличаются они ровно одной строкой на каждую
    /// раковину опций.
    ///
    /// <para>
    /// <c>TestServer</c>, а не Kestrel на петле, по той же причине, по которой
    /// у лестницы три ступени, а не одна: вопрос стои́т про долю JSON в
    /// конвейере, и сокет в этой доле не участвует, зато шумит. Настоящее
    /// развёртывание добавляет сеть <b>поверх</b> измеренного - то есть делает
    /// нашу долю ещё меньше, и сказать об этом словами честнее, чем растворить
    /// в разбросе.
    /// </para>
    ///
    /// <para>
    /// Опции лестница берёт <b>отсюда</b>, а не строит рядом свои. Это и есть
    /// определение ступени: нижняя ступень обязана мерить ту самую раковину
    /// настроек, которой пользуется верхняя, иначе три числа не складываются в
    /// одну лестницу.
    /// </para>
    /// </summary>
    internal sealed class Pipeline : IAsyncDisposable
    {
        private readonly IHost _host;

        private Pipeline(
            IHost host,
            HttpClient client,
            SystemTextJsonOutputFormatter formatter,
            JsonSerializerOptions configured,
            JsonSerializerOptions minimal)
        {
            _host = host;
            Client = client;
            Formatter = formatter;
            ConfiguredOptions = configured;
            MinimalApiOptions = minimal;
        }

        internal HttpClient Client { get; }

        /// <summary>
        /// Форматтер, взятый <b>из этого приложения</b>, а не построенный
        /// рядом по тем же опциям.
        ///
        /// <para>
        /// Разница не формальная. Собери лестница свой экземпляр - и вопрос
        /// «те ли это опции, которыми пишет контроллер» остался бы на
        /// рассуждении, а рассуждение здесь уже один раз ошиблось. Взятый из
        /// DI объект делает вторую ступень частью третьей по построению.
        /// </para>
        /// </summary>
        internal SystemTextJsonOutputFormatter Formatter { get; }

        /// <summary>
        /// Опции, которыми пишет ответ форматтер MVC. <b>Не те</b>, что
        /// настроил потребитель, - и это находка лестницы.
        ///
        /// <para>
        /// MVC не отдаёт форматтеру раковину из <c>AddJsonOptions</c>, а
        /// <b>копирует</b> её, и в копии подменяет энкодер на
        /// <c>UnsafeRelaxedJsonEscaping</c> - если только потребитель не
        /// выставил энкодер сам. Пока раковина моста несла свой набор
        /// экранируемого, это означало отступление на каждом ответе.
        /// </para>
        ///
        /// <para>
        /// Теперь означает только то, что энкодер здесь другой, - а
        /// <c>EncoderUtf8Exhauster</c> берёт его из опций и потому верен при
        /// любом. Раковина остаётся в стенде отдельной строкой затем, чтобы
        /// отступление, если оно вернётся, было видно числом, а не осталось
        /// незамеченным.
        /// </para>
        /// </summary>
        internal JsonSerializerOptions MvcWriteOptions
        {
            get { return Formatter.SerializerOptions; }
        }

        /// <summary>
        /// Опции, которые настроил потребитель через <c>AddJsonOptions</c>.
        ///
        /// <para>
        /// Это не «наши игрушечные опции рядом с настоящими»: <b>читает</b>
        /// тело запроса MVC именно ими - у входного форматтера энкодер не
        /// подменяется, потому что на чтении он не при чём. Та же раковина
        /// стои́т в таблице и как потолок записи: отличается она от
        /// <see cref="MvcWriteOptions"/> одним свойством, и это свойство -
        /// единственное, что отделяет нас от ускорения ответа.
        /// </para>
        /// </summary>
        internal JsonSerializerOptions ConfiguredOptions { get; }

        /// <summary>
        /// Опции, которыми MVC <b>читает</b> тело запроса, - взятые у
        /// входного форматтера приложения, а не предположенные.
        ///
        /// <para>
        /// Ровно тем же приёмом, каким нашлась подмена энкодера на записи:
        /// спрашивать надо тот объект, который работает. Совпадает ли эта
        /// раковина с настроенной - вопрос к прогону, а не ко мне.
        /// </para>
        /// </summary>
        internal JsonSerializerOptions MvcReadOptions { get; private set; } = null!;

        /// <summary>
        /// Опции minimal API. Отдельная раковина, и не совпадающая с
        /// предыдущей: <c>MaxDepth</c> MVC выставляет в 32, minimal API
        /// оставляет умолчание. Мост это переживает намеренно - глубину
        /// проверяет <c>Utf8JsonReader</c>, созданный эталоном, - но
        /// <b>проверяется</b> это прогоном, а не рассуждением.
        /// </summary>
        internal JsonSerializerOptions MinimalApiOptions { get; }

        /// <summary>
        /// Входные форматтеры приложения - в том порядке, в котором их
        /// опрашивает MVC. Нужны затем, чтобы «форматтер зарегистрирован»
        /// проверялось у работающего приложения, а не у той строки, которой мы
        /// его туда клали.
        /// </summary>
        internal IReadOnlyList<IInputFormatter> InputFormatters { get; private set; } = null!;

        /// <summary>
        /// Поднять приложение. <paramref name="goddess"/> - звать ли
        /// <c>UseJsonGoddess</c>; больше приложения ничем не отличаются.
        ///
        /// <para>
        /// Одной строкой, и это существенно. Некоторое время стенд поднимал
        /// третье приложение, где энкодер выставлялся вручную в
        /// умолчательный, - MVC подменяет его <b>только когда потребитель
        /// оставил его пустым</b>, и такая строка включала мост на записи.
        /// Обходной путь работал и был измерен (0.42 на запросе), но платил
        /// за это документом: со строгим энкодером кириллица уезжает в
        /// <c>\uXXXX</c>. С <c>EncoderUtf8Exhauster</c> платить стало нечем, и
        /// третье приложение убрано - вместе с советом, который оно
        /// обосновывало.
        /// </para>
        /// </summary>
        /// <param name="streaming">
        /// Звать ли <c>AddJsonGoddess()</c> - порождённое расширение, ставящее
        /// потоковый входной форматтер первым (фаза 10, пункт 6г). Отдельно от
        /// <paramref name="goddess"/> намеренно: мост и потоковый путь - разные
        /// пути, и включаются они порознь.
        /// </param>
        internal static async Task<Pipeline> StartAsync(bool goddess, bool streaming = false)
        {
            var host = new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();

                    web.ConfigureServices(services =>
                    {
                        var mvc = services
                            .AddControllers(options =>
                            {
                                if (streaming)
                                {
                                    //одна строка - вся цена включения; форматтер
                                    //напечатан в эту же сборку
                                    JsonGoddess.Compat.Generated.JsonGoddessStreamingExtensions
                                        .AddJsonGoddess(options);
                                }
                            })
                            //BenchmarkDotNet запускает замер из СВОЕЙ сборки,
                            //и поиск контроллеров по входной сборке нашёл бы
                            //там ноль штук. Часть приложения называется явно
                            .AddApplicationPart(typeof(OrdersController).Assembly);

                        if (goddess)
                        {
                            mvc.AddJsonOptions(o => o.JsonSerializerOptions.UseJsonGoddess());
                            services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UseJsonGoddess());
                        }
                    });

                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();

                            //minimal API идёт другим путём эталона и другой
                            //раковиной опций. Стои́т здесь потому, что вопрос
                            //«достаёт ли мост до обоих» иначе остался бы без
                            //ответа
                            endpoints.MapGet("/minimal/order", () => Payload.One);
                            endpoints.MapGet("/minimal/orders", () => Payload.Many);

                            //Чтение тела в minimal API - вопрос O12, и до сих
                            //пор стенд его не мерил вовсе: здесь стоя́ли одни
                            //MapGet. Числа 0.99 сняты на MVC, а minimal API с
                            //.NET 10 идёт своей дорогой - через
                            //DeserializeAsync(PipeReader), - и переносить одно
                            //на другое нельзя.
                            //
                            //Ответ короткий нарочно, как и у контроллера:
                            //замер обязан мерить разбор тела, а не
                            //сериализацию ответа
                            endpoints.MapPost("/minimal/orders", (Order[] orders) => orders.Length);
                            endpoints.MapPost("/minimal/order", (Order order) => order.Id);
                        });
                    });
                })
                .Build();

            await host.StartAsync();

            var formatter = host.Services
                .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>()
                .Value
                .OutputFormatters
                .OfType<SystemTextJsonOutputFormatter>()
                .Single();

            var configured = host.Services
                .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
                .Value
                .JsonSerializerOptions;

            var input = host.Services
                .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>()
                .Value
                .InputFormatters
                .OfType<SystemTextJsonInputFormatter>()
                .Single();

            var minimalOptions = host.Services
                .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                .Value
                .SerializerOptions;

            return new Pipeline(host, host.GetTestClient(), formatter, configured, minimalOptions)
            {
                MvcReadOptions = input.SerializerOptions,
                InputFormatters = host.Services
                    .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>()
                    .Value
                    .InputFormatters,
            };
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
