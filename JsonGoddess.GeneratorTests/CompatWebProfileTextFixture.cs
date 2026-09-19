using System.Collections.Generic;
using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Веб-профиль (§10, маршрут B) со стороны генератора: какой код он
    /// печатает под <c>JsonSerializerDefaults.Web</c> и когда отказывается.
    ///
    /// <para>
    /// Сквозная проверка «документ тот же, что у эталона» живёт в
    /// <c>JsonGoddess.CompatTests</c>: она требует исполнить порождённый код,
    /// а этот проект его только читает.
    /// </para>
    /// </summary>
    public class CompatWebProfileTextFixture
    {
        private static readonly IReadOnlyDictionary<string, string> WebEnabled =
            new Dictionary<string, string> { { "JsonGoddessCompatWeb", "enable" }, };

        private const string WebHostFile = "JsonGoddess.Compat.Generated.JsonGoddessCompatWebHost.g.cs";

        private static string Graph(string members)
        {
            return @"
using JsonGoddess.Compat;
using System.Text.Json.Serialization;

namespace Sample
{
    public class Order
    {
" + members + @"
    }

    public static class Caller
    {
        public static string Write(Order order) => JsonSerializer.Serialize(order);
    }
}
";
        }

        private static GeneratorRun Run(string members)
        {
            return GeneratorHarness.Run(
                new[] { new SourceFile("Subject.cs", Graph(members)), },
                WebEnabled
                );
        }

        /// <summary>
        /// Раковина веб-профиля - <c>EncoderUtf8Exhauster</c>, а не
        /// <c>CompatUtf8Exhauster</c>.
        ///
        /// <para>
        /// Разница не в имени типа. Умолчательная раковина несёт <b>свой</b>
        /// набор экранируемого, и он верен ровно под умолчательным энкодером;
        /// ASP.NET Core пишет ответ релаксированным, и повторить его нечем - в
        /// его наборе все незанятые кодовые точки Unicode. Раковина
        /// веб-профиля набора не несёт, а спрашивает энкодер, лежащий в
        /// опциях.
        /// </para>
        /// </summary>
        [Fact]
        public void The_web_host_writes_into_the_sink_that_asks_the_encoder()
        {
            var run = Run("        public string? Customer { get; set; }");

            var host = run.GeneratedFiles[WebHostFile];

            Assert.Contains("global::JsonGoddess.Compat.EncoderUtf8Exhauster", host, System.StringComparison.Ordinal);
            Assert.DoesNotContain("global::JsonGoddess.CompatUtf8Exhauster", host, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Умолчательный профиль при этом остаётся на своей раковине: она
        /// быстрее (один проход вместо двух) и измерена, а энкодер там всегда
        /// один и тот же.
        /// </summary>
        [Fact]
        public void The_default_host_keeps_its_own_sink()
        {
            var run = Run("        public string? Customer { get; set; }");

            var host = run.GeneratedFiles["JsonGoddess.Compat.Generated.JsonGoddessCompatHost.g.cs"];

            Assert.Contains("global::JsonGoddess.CompatUtf8Exhauster", host, System.StringComparison.Ordinal);
            Assert.DoesNotContain("EncoderUtf8Exhauster", host, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Имя, которое разные энкодеры пишут по-разному, закрывает тип для
        /// веб-профиля - и закрывает <b>громко</b>.
        ///
        /// <para>
        /// Строки значений экранирует энкодер из опций, и они верны при любом.
        /// Имена же печатаются константами, на компиляции, когда энкодера ещё
        /// нет: <c>&amp;</c> умолчательный разворачивает в <c>&</c>, а
        /// релаксированный оставляет как есть, и одной константой обслужить
        /// оба нельзя. Промолчать тут значило бы выдать под веб-опциями
        /// валидный документ, отличающийся от эталонного, - худший из исходов.
        /// </para>
        ///
        /// <para>
        /// <c>JGD027</c> такое имя не ловит: он отвергает только управляющие,
        /// кавычку и слэш. Символы, о которых речь, - обычные печатные ASCII.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("a&b")]
        [InlineData("a'b")]
        [InlineData("a+b")]
        [InlineData("a<b")]
        [InlineData("a>b")]
        [InlineData("a`b")]
        public void A_name_that_different_encoders_write_differently_closes_the_web_profile(string name)
        {
            var run = Run(
                "        [JsonPropertyName(\"" + name + "\")]\n"
                + "        public string? Customer { get; set; }"
                );

            Assert.Contains("JGD004", run.DiagnosticIds);
            Assert.DoesNotContain(WebHostFile, run.GeneratedFiles.Keys);

            var message = run.GeneratorDiagnostics
                .Single(d => d.Id == "JGD004")
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);

            Assert.Contains(name, message, System.StringComparison.Ordinal);
            Assert.Contains("different encoders", message, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// А умолчательный профиль такое имя обслуживает как ни в чём не
        /// бывало: там энкодер один, и константа под него верна.
        ///
        /// <para>
        /// Это половина обещания «отказ относится только к веб-профилю», и без
        /// неё первая половина ничего не стоила бы.
        /// </para>
        /// </summary>
        [Fact]
        public void The_same_name_is_served_by_the_default_profile()
        {
            var run = Run(
                "        [JsonPropertyName(\"a&b\")]\n"
                + "        public string? Customer { get; set; }"
                );

            var host = run.GeneratedFiles["JsonGoddess.Compat.Generated.JsonGoddessCompatHost.g.cs"];

            //в тексте порождённого файла это C#-литерал, поэтому обратный слэш
            //JSON-экранирования удвоен: "a\\u0026b"
            Assert.Contains("a\\\\u0026b", host, System.StringComparison.Ordinal);
            Assert.Empty(run.CompilationErrors);
        }

        /// <summary>
        /// Обычное имя веб-профиль печатает, и печатает camelCase'ом - то
        /// есть отказ выше относится к имени, а не к веб-профилю вообще.
        /// </summary>
        [Fact]
        public void An_ordinary_name_still_gets_the_web_profile()
        {
            var run = Run("        public string? Customer { get; set; }");

            Assert.DoesNotContain("JGD004", run.DiagnosticIds);
            Assert.Contains("\\\"customer\\\"", run.GeneratedFiles[WebHostFile]);
        }
    }
}
