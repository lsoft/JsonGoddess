using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Два приложения, различающиеся ровно одной строкой: встал ли наш
    /// форматтер первым в <c>MvcOptions.InputFormatters</c>.
    ///
    /// <para>
    /// <c>TestServer</c>, а не Kestrel на петле, и по той же причине, что у
    /// лестницы (§12.9): вопрос здесь про разбор тела, а сокет в эту долю не
    /// входит, зато шумит. Настоящая сеть добавляется ПОВЕРХ измеренного.
    /// </para>
    /// </summary>
    internal static class Pipeline
    {
        internal static async Task<IHost> Start(bool goddess)
        {
            return await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        var mvc = services.AddControllers(options =>
                        {
                            if (goddess)
                            {
                                options.InputFormatters.Insert(0, new StreamingInputFormatter());
                            }
                        });

                        //BenchmarkDotNet запускает всё из своей порождённой
                        //сборки, и контроллеры в ней сами не находятся
                        mvc.AddApplicationPart(typeof(OrdersController).Assembly);
                    });

                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
                })
                .StartAsync();
        }

        internal static HttpClient Client(IHost host) => host.GetTestClient();
    }
}
