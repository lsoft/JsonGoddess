using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Compat;
using Xunit;

using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Отказ на быстром пути - глазами потребителя, который уже написал
    /// <c>catch (JsonException)</c> и про подмену не знает.
    ///
    /// <para>
    /// Это не косметика. <c>JsonException</c> эталона наследует прямо
    /// <c>Exception</c>, наш <c>JsonDocumentException</c> -
    /// <c>InvalidOperationException</c>; общего предка, кроме
    /// <c>Exception</c>, у них нет. Не переодень фасад отказ - и обработчик,
    /// стоявший у потребителя до подмены, после неё молча перестал бы
    /// срабатывать. Документ тот же, скорость выше, а ошибка улетела мимо
    /// <c>catch</c>: тише поломки не бывает.
    /// </para>
    /// </summary>
    public class RefusalFixture
    {
        /// <summary>
        /// Документы, на которых отказывают оба. Набор выбран так, чтобы
        /// сработали разные слои: значение, вложенный объект, элемент массива,
        /// хвост за документом.
        /// </summary>
        public static TheoryData<string> Malformed =>
            new TheoryData<string>
            {
                "{\"Id\":1,\"ShipTo\":{\"City\":\"Псков\",\"Zip\":@}}",
                "{\"Id\":1,\"Lines\":[{\"Sku\":\"a\",\"Quantity\":1,\"Price\":1},{\"Sku\":@}]}",
                "{\"Id\":1} и ещё кое-что",
                "{\"Id\":1,\"Counters\":{\"views\":@}}",
                "{\"Id\":",
            };

        [Theory]
        [MemberData(nameof(Malformed))]
        public void The_fast_path_refuses_with_the_exception_type_the_consumer_catches(string json)
        {
            //эталон прогоняется здесь же: если он вдруг это примет, тест обязан
            //сломаться, а не молча проверять нас против собственной выдумки
            var theirs = Assert.Throws<JsonException>(() => Reference.Deserialize<Order>(json));

            var ours = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Order>(json));

            Assert.Equal(theirs.Path, ours.Path);
            Assert.Equal(theirs.LineNumber, ours.LineNumber);
        }

        [Fact]
        public void The_fast_path_was_the_one_that_refused()
        {
            //проверка самой проверки: если бы связка не заполнилась, всё выше
            //меряло бы эталон против эталона и было бы зелёным всегда
            Assert.True(CompatBinding<Order>.IsBound);
        }

        /// <summary>
        /// Переодетый отказ не должен ничего терять: наш несёт якорь и смещение
        /// от начала документа, которых у <c>JsonException</c> нет вовсе.
        /// </summary>
        [Fact]
        public void The_original_refusal_travels_inside_as_the_inner_exception()
        {
            var failure = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<Order>("{\"Id\":1,\"ShipTo\":{\"Zip\":@}}")
                );

            var inner = Assert.IsType<JsonDocumentException>(failure.InnerException);

            Assert.Equal(failure.Path, inner.Path);
            Assert.True(inner.BytePosition > 0);
        }

        /// <summary>
        /// Сообщение - той же формы, что у эталона: причина, затем путь,
        /// строка и смещение. Текст самой причины наш и совпадать не обязан
        /// (docs/stj-divergences.md), а вот хвост обязан, иначе разойдутся логи.
        /// </summary>
        [Fact]
        public void The_message_carries_the_path_the_way_the_reference_carries_it()
        {
            const string json = "{\"Id\":1,\"ShipTo\":{\"Zip\":@}}";

            var theirs = Assert.Throws<JsonException>(() => Reference.Deserialize<Order>(json));
            var ours = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Order>(json));

            var tail = " Path: " + theirs.Path + " | LineNumber: " + theirs.LineNumber + " | BytePositionInLine: ";

            Assert.Contains(tail, theirs.Message, StringComparison.Ordinal);
            Assert.Contains(tail, ours.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// А на отступлении к эталону переодевать нечего: там отказ его
        /// собственный, и трогать его мы не имеем права.
        /// </summary>
        [Fact]
        public void A_refusal_from_the_fallback_path_arrives_untouched()
        {
            var utf8 = Encoding.UTF8.GetBytes("{\"Id\":1,\"Weird\":@}");

            //WithConverter обслужить нельзя (JGD001), значит путь тут эталонный
            var failure = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<WithConverter>(utf8)
                );

            Assert.False(CompatBinding<WithConverter>.IsBound);
            Assert.Null(failure.InnerException as JsonDocumentException);
        }
    }
}
