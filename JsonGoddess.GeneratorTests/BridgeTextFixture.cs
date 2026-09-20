using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Мост (§10, маршрут B) - кого он берёт на себя и о ком молчать не имеет
    /// права.
    ///
    /// <para>
    /// Главное утверждение здесь - не текст, а <b>пустота
    /// <see cref="GeneratorRun.CompilationErrors"/></b>. Мост обслуживает не
    /// всякий субъект, и отбор до этих тестов шёл по одному типу за раз, без
    /// оглядки на членов: субъект, у которого <b>членом</b> стои́т
    /// необслуживаемый тип, проходил отбор сам и звал
    /// <c>BridgeRead_</c> читателя, которого никто не напечатал. Получалась не
    /// медленная сборка потребителя, а несобирающаяся - <c>CS0103</c> прямо в
    /// порождённом файле.
    /// </para>
    /// </summary>
    public class BridgeTextFixture
    {
        private static string Errors(GeneratorRun run)
        {
            return string.Join("; ", run.CompilationErrors.Select(e => e.Id + ": " + e.GetMessage()));
        }

        /// <summary>
        /// Полиморфный субъект членом. Мост его не берёт (это объявлено), но
        /// обязан не взять и того, кто его держит.
        /// </summary>
        [Fact]
        public void A_polymorphic_member_takes_its_holder_out_of_the_bridge()
        {
            var run = GeneratorHarness.Run(@"
using System.Text.Json.Serialization;
using JsonGoddess.Compat;

namespace Sample
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = ""$type"")]
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal
    {
        public string? Name { get; set; }
    }

    public class Dog : Animal
    {
        public bool Barks { get; set; }
    }

    public class Owner
    {
        public int Id { get; set; }
        public Animal? Pet { get; set; }
    }

    public static class Caller
    {
        public static string Write(Owner owner) => JsonSerializer.Serialize(owner);
    }
}
");

            Assert.Empty(run.CompilationErrors);

            var bridge = run.GeneratedFiles
                .Where(f => f.Key.Contains(".Bridge."))
                .Select(f => f.Value)
                .FirstOrDefault();

            //ни самого полиморфного типа, ни его держателя
            if (bridge is not null)
            {
                Assert.DoesNotContain("BridgeRead_Sample_Animal", bridge);
                Assert.DoesNotContain("BridgeRead_Sample_Owner", bridge);
            }

            //и об этом сказано - иначе ускорение, которого не случилось,
            //пришлось бы искать замером
            Assert.Contains("JGD006", run.DiagnosticIds);

            var said = run.GeneratorDiagnostics.First(d => d.Id == "JGD006").GetMessage();

            Assert.Contains("Sample.Owner", said);
            Assert.Contains("Pet", said);
            Assert.Contains("polymorphic", said);
        }

        /// <summary>
        /// Субъект-коллекция членом - вторая форма той же болезни, и проверять
        /// её отдельно обязательно: причина отказа у них общая, а путь в
        /// связывателе разный.
        /// </summary>
        [Fact]
        public void A_collection_subject_member_takes_its_holder_out_of_the_bridge()
        {
            var run = GeneratorHarness.Run(@"
using System.Collections.Generic;
using JsonGoddess.Compat;

namespace Sample
{
    public class Basket : List<string>
    {
    }

    public class Owner
    {
        public int Id { get; set; }
        public Basket? Items { get; set; }
    }

    public static class Caller
    {
        public static string Write(Owner owner) => JsonSerializer.Serialize(owner);
    }
}
");

            Assert.Empty(run.CompilationErrors);
            Assert.Contains("JGD006", run.DiagnosticIds);

            var said = run.GeneratorDiagnostics.First(d => d.Id == "JGD006").GetMessage();

            Assert.Contains("Sample.Owner", said);
            Assert.Contains("Items", said);
            Assert.Contains("collection subject", said);
        }

        /// <summary>
        /// Обратная проверка, и она не менее важна первых двух: отказ обязан
        /// быть <b>узким</b>. Здоровый граф проходит в мост целиком и ни одной
        /// диагностики не получает - иначе первые два теста проходили бы и на
        /// генераторе, отключившем мост вовсе.
        /// </summary>
        [Fact]
        public void A_plain_graph_still_goes_through_the_bridge_in_full()
        {
            var run = GeneratorHarness.Run(@"
using System.Collections.Generic;
using JsonGoddess.Compat;

namespace Sample
{
    public class Line
    {
        public string? Sku { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public List<Line>? Lines { get; set; }
    }

    public static class Caller
    {
        public static string Write(Order order) => JsonSerializer.Serialize(order);
    }
}
");

            Assert.Empty(run.CompilationErrors);
            Assert.DoesNotContain("JGD006", run.DiagnosticIds);

            var bridge = run.GeneratedFiles
                .Where(f => f.Key.Contains(".Bridge."))
                .Select(f => f.Value)
                .FirstOrDefault();

            Assert.True(bridge is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));
            Assert.Contains("BridgeRead_Sample_Order", bridge);
            Assert.Contains("BridgeRead_Sample_Line", bridge);
        }
    }
}
