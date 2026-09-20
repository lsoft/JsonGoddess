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
        /// Полиморфный субъект членом - обслуживается вместе с держателем
        /// (PLAN.md §15, O11 (а)). До этого не обслуживался ни тот, ни другой,
        /// и держатель при этом ронял сборку.
        /// </summary>
        [Fact]
        public void A_polymorphic_member_is_served_and_so_is_its_holder()
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

            Assert.True(bridge is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));

            Assert.Contains("BridgeRead_Sample_Animal", bridge);
            Assert.Contains("BridgeRead_Sample_Owner", bridge);
            Assert.Contains("BridgeReadBody_Sample_Dog_As_Sample_Animal", bridge);

            //откат у моста - копия читателя: он структура, и копия есть
            //полноценное сохранённое состояние
            Assert.Contains("var __beforeDiscriminator = reader;", bridge);
            Assert.Contains("reader = __beforeDiscriminator;", bridge);

            //дискриминатор не первым свойством - отказ, как у эталона
            Assert.Contains("the type discriminator must be the first property", bridge);

            Assert.DoesNotContain("JGD006", run.DiagnosticIds);
        }

        /// <summary>
        /// Полиморфный тип <b>корнем</b> в реестр моста не попадает, и это
        /// отказ эталона, а не наш: чужой конвертер в свою полиморфную
        /// машинерию он не пускает, причём не отступает к себе, а бросает
        /// <c>NotSupportedException</c> (снято пробой в
        /// <c>JsonGoddess.CompatTests</c>). Зарегистрировать такой тип значило
        /// бы поменять тихое отступление на исключение у потребителя.
        /// </summary>
        [Fact]
        public void A_polymorphic_root_is_not_registered_and_the_reason_is_named()
        {
            var run = GeneratorHarness.Run(@"
using System.Text.Json.Serialization;
using JsonGoddess.Compat;

namespace Sample
{
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal
    {
        public string? Name { get; set; }
    }

    public class Dog : Animal
    {
        public bool Barks { get; set; }
    }

    public static class Caller
    {
        public static string Write(Animal animal) => JsonSerializer.Serialize(animal);
    }
}
");

            Assert.Empty(run.CompilationErrors);
            Assert.Contains("JGD006", run.DiagnosticIds);

            var said = run.GeneratorDiagnostics.First(d => d.Id == "JGD006").GetMessage();

            Assert.Contains("Sample.Animal", said);
            Assert.Contains("polymorphic metadata", said);

            var bridge = run.GeneratedFiles
                .Where(f => f.Key.Contains(".Bridge."))
                .Select(f => f.Value)
                .FirstOrDefault();

            //читатель напечатан - он нужен везде, где дискриминатор разбираем
            //мы, - а регистрации нет
            if (bridge is not null)
            {
                Assert.DoesNotContain("BridgeBinding<global::Sample.Animal>", bridge);
            }
        }

        /// <summary>
        /// Субъект-коллекция членом - обслуживается вместе с держателем
        /// (PLAN.md §15, O11 (б)). Проверяется отдельно от полиморфного: путь
        /// в связывателе у них разный, хоть условие и стои́т в одной
        /// неподвижной точке.
        /// </summary>
        [Fact]
        public void A_collection_subject_member_is_served_and_so_is_its_holder()
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
            Assert.DoesNotContain("JGD006", run.DiagnosticIds);

            var bridge = run.GeneratedFiles
                .Where(f => f.Key.Contains(".Bridge."))
                .Select(f => f.Value)
                .FirstOrDefault();

            Assert.True(bridge is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));

            Assert.Contains("BridgeRead_Sample_Basket", bridge);
            Assert.Contains("BridgeRead_Sample_Owner", bridge);

            //элемент кладётся через приведение к интерфейсу, а не прямым
            //вызовом: субъект мог реализовать его явно
            Assert.Contains("global::System.Collections.Generic.ICollection<", bridge);
            Assert.Contains(")result).Add(", bridge);
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
