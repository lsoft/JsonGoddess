using System.Collections.Generic;
using System.Text;
using JsonGoddess.Compat;
using Xunit;

using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Сквозная проверка маршрута A (§10): ни одного <c>[JsonSubject]</c> в
    /// проекте нет, а типы тем не менее обслуживаются порождённым кодом -
    /// генератор нашёл вызовы фасада сам, обошёл граф сам и зарегистрировал
    /// быстрый путь из <c>[ModuleInitializer]</c>.
    ///
    /// <para>
    /// Каждая проверка «мы совпадаем с эталоном» здесь ещё и прогоняет сам
    /// эталон: ожидание берётся из его ответа, а не из литерала. Литерал
    /// заморозил бы то, что мы сегодня делаем, а не то, что делает он.
    /// </para>
    /// </summary>
    public class CompatGeneratorFixture
    {
        [Fact]
        public void The_generator_bound_the_root_type_without_a_single_attribute()
        {
            //связка заполнена инициализатором модуля - то есть до первой строки
            //этого теста и без участия пользовательского кода
            Assert.True(CompatBinding<Order>.IsBound);
        }

        [Fact]
        public void The_whole_graph_was_walked_transitively()
        {
            //ни Address, ни Line, ни Dictionary<string,int> в вызовах фасада не
            //упоминаются: они попали в порождённый код только обходом членов
            var order = Order.CreateSample();

            Assert.Equal(
                Reference.Serialize(order, ReferenceOptions.Relaxed),
                JsonSerializer.Serialize(order)
                );
        }

        [Fact]
        public void A_document_written_by_the_facade_reads_back_through_the_reference()
        {
            var json = JsonSerializer.Serialize(Order.CreateSample());
            var theirs = Reference.Deserialize<Order>(json);

            Assert.Equal(42, theirs!.Id);
            Assert.Equal("Псков", theirs.ShipTo!.City);
            Assert.Equal(2, theirs.Lines!.Count);
            Assert.Equal(Status.Shipped, theirs.State);
        }

        [Fact]
        public void A_document_written_by_the_reference_reads_back_through_the_facade()
        {
            var json = Reference.Serialize(Order.CreateSample(), ReferenceOptions.Relaxed);
            var ours = JsonSerializer.Deserialize<Order>(json);

            Assert.Equal(42, ours!.Id);
            Assert.Equal("180000", ours.ShipTo!.Zip);
            Assert.Equal(19.99m, ours.Lines![0].Price);
            Assert.Equal(7, ours.Counters!["views"]);
        }

        [Fact]
        public void The_utf8_round_trip_matches_the_reference_byte_for_byte()
        {
            var order = Order.CreateSample();

            Assert.Equal(
                Reference.SerializeToUtf8Bytes(order, ReferenceOptions.Relaxed),
                JsonSerializer.SerializeToUtf8Bytes(order)
                );
        }

        /// <summary>
        /// Тип, который обслужить нельзя, обязан остаться работающим. Это и
        /// есть drop-in: медленнее, но правильно и без единой правки в коде
        /// пользователя.
        /// </summary>
        [Fact]
        public void A_type_the_generator_refused_keeps_working_through_the_reference()
        {
            var value = new WithConverter { Id = 1, Weird = "странное", };

            Assert.False(CompatBinding<WithConverter>.IsBound);
            Assert.Equal(Reference.Serialize(value), JsonSerializer.Serialize(value));

            var back = JsonSerializer.Deserialize<WithConverter>(Reference.Serialize(value));
            Assert.Equal(1, back!.Id);
            Assert.Equal("странное", back.Weird);
        }

        /// <summary>
        /// Строгость фасада - ровно эталонная. Ни строже (иначе он отверг бы
        /// документ, который заменяемая библиотека принимает), ни
        /// снисходительнее (иначе принял бы тот, который она отвергает).
        /// </summary>
        [Theory]
        //хвост после документа: эталон отвергает всегда
        [InlineData("{\"Id\":1} x")]
        //число вне грамматики RFC 8259
        [InlineData("{\"Id\":01}")]
        //висячая запятая: эталон по умолчанию не принимает
        [InlineData("{\"Id\":1,}")]
        //комментарий: эталон по умолчанию не принимает
        [InlineData("{\"Id\":1 /* нет */}")]
        public void What_the_reference_refuses_the_facade_refuses_too(string json)
        {
            Assert.True(Refused(() => Reference.Deserialize<Order>(json)), "эталон это принял");
            Assert.True(Refused(() => JsonSerializer.Deserialize<Order>(json)), "фасад это принял");
        }

        [Theory]
        //незнакомое свойство: эталон по умолчанию пропускает
        [InlineData("{\"Id\":1,\"Unknown\":[1,2,3]}")]
        //повтор свойства: эталон по умолчанию разрешает, побеждает последнее
        [InlineData("{\"Id\":1,\"Id\":2}")]
        public void What_the_reference_accepts_the_facade_accepts_too(string json)
        {
            Assert.False(Refused(() => Reference.Deserialize<Order>(json)), "эталон это отверг");
            Assert.False(Refused(() => JsonSerializer.Deserialize<Order>(json)), "фасад это отверг");
        }

        [Fact]
        public void A_repeated_property_wins_with_the_same_value_as_in_the_reference()
        {
            const string json = "{\"Id\":1,\"Id\":2}";

            Assert.Equal(
                Reference.Deserialize<Order>(json)!.Id,
                JsonSerializer.Deserialize<Order>(json)!.Id
                );
        }

        [Fact]
        public void Depth_is_limited_exactly_where_the_reference_limits_it()
        {
            //предел эталона по умолчанию - 64; проверяется его же ответом, а не
            //числом, записанным здесь
            Assert.True(Refused(() => Reference.Deserialize<Order>(Nested(65))), "эталон принял 65 уровней");
            Assert.True(Refused(() => JsonSerializer.Deserialize<Order>(Nested(65))), "фасад принял 65 уровней");

            Assert.False(Refused(() => Reference.Deserialize<Order>(Nested(60))), "эталон отверг 60 уровней");
            Assert.False(Refused(() => JsonSerializer.Deserialize<Order>(Nested(60))), "фасад отверг 60 уровней");
        }

        /// <summary>Документ с незнакомым свойством, внутри которого вложенность.</summary>
        private static string Nested(int depth)
        {
            var builder = new StringBuilder("{\"Unknown\":");
            builder.Append('[', depth);
            builder.Append(']', depth);
            builder.Append('}');
            return builder.ToString();
        }

        private static bool Refused(System.Func<object?> read)
        {
            try
            {
                read();
                return false;
            }
            catch (System.Exception)
            {
                return true;
            }
        }

        private static class ReferenceOptions
        {
            /// <summary>
            /// Опции, в которых набор экранируемого совпадает с нашим. Наш
            /// энкодер намеренно расходится с их умолчанием
            /// (docs/stj-divergences.md §1.1), и здесь это не проверяется - это
            /// проверяет другой тест в другом проекте.
            /// </summary>
            public static readonly System.Text.Json.JsonSerializerOptions Relaxed =
                new System.Text.Json.JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
        }
    }
}
