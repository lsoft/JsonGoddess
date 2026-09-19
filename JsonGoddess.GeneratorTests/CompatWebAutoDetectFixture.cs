using System.Collections.Generic;
using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Кто получает веб-вариант кода и по какому признаку.
    ///
    /// <para>
    /// Признак - <b>факт ссылки на ASP.NET Core</b>, а не умолчание свойства.
    /// Раньше веб-вариант печатался только по явной просьбе, и цена
    /// незаданного свойства платилась молчанием: приложение работало,
    /// выдавало правильные документы и было медленным, а узнать об этом можно
    /// было, только заметив, что не ускорилось.
    /// </para>
    ///
    /// <para>
    /// Стенд генератора компилирует исходники без ссылки на ASP.NET Core -
    /// значит здесь проверяется <b>обратная</b> сторона: без ссылки
    /// веб-вариант не печатается, а явное <c>enable</c> печатает его всё
    /// равно. Что он появляется сам при наличии ссылки, проверяется там, где
    /// ссылка настоящая, - <c>JsonGoddess.WebPerformanceTests</c> не ставит
    /// свойства вовсе, и его <c>--verify</c> падает, если мост не обслужил
    /// опции ASP.NET.
    /// </para>
    /// </summary>
    public class CompatWebAutoDetectFixture
    {
        private const string WebHostFile = "JsonGoddess.Compat.Generated.JsonGoddessCompatWebHost.g.cs";

        private const string Graph = @"
using JsonGoddess.Compat;

namespace Sample
{
    public class Order
    {
        public int Id { get; set; }
        public string? Customer { get; set; }
    }

    public static class Caller
    {
        public static string Write(Order order) => JsonSerializer.Serialize(order);
    }
}
";

        private static GeneratorRun Run(IReadOnlyDictionary<string, string>? properties)
        {
            var files = new[] { new SourceFile("Subject.cs", Graph), };

            return properties is null
                ? GeneratorHarness.Run(files)
                : GeneratorHarness.Run(files, properties);
        }

        [Fact]
        public void Without_a_reference_to_aspnet_core_the_web_variant_is_not_printed()
        {
            Assert.DoesNotContain(WebHostFile, Run(null).GeneratedFiles.Keys);
        }

        /// <summary>
        /// Библиотека, чьи типы сериализует чужой веб-хост, про ссылки этого
        /// хоста ничего не знает: код печатается там, где объявлен тип. Для
        /// неё <c>enable</c> и оставлен.
        /// </summary>
        [Fact]
        public void An_explicit_enable_prints_it_anyway()
        {
            var run = Run(new Dictionary<string, string> { { "JsonGoddessCompatWeb", "enable" }, });

            Assert.Contains(WebHostFile, run.GeneratedFiles.Keys);
        }

        /// <summary>
        /// И наоборот: <c>disable</c> выключает веб-вариант там, где ссылка
        /// есть, а веб-методов нет. Здесь ссылки нет и так, но значение
        /// обязано быть <b>понято</b>, а не пропущено как незнакомое, - иначе
        /// оно молча ничего не делало бы в том проекте, где нужно.
        /// </summary>
        [Fact]
        public void An_explicit_disable_is_understood_rather_than_ignored()
        {
            var run = Run(new Dictionary<string, string> { { "JsonGoddessCompatWeb", "disable" }, });

            Assert.DoesNotContain(WebHostFile, run.GeneratedFiles.Keys);
            Assert.Empty(run.CompilationErrors);
        }

        /// <summary>
        /// Умолчательный вариант печатается в любом случае: веб-профиль - это
        /// добавка, а не замена.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("enable")]
        [InlineData("disable")]
        public void The_default_variant_is_printed_no_matter_what(string? value)
        {
            var run = value is null
                ? Run(null)
                : Run(new Dictionary<string, string> { { "JsonGoddessCompatWeb", value }, });

            Assert.Contains("JsonGoddess.Compat.Generated.JsonGoddessCompatHost.g.cs", run.GeneratedFiles.Keys);
        }
    }
}
