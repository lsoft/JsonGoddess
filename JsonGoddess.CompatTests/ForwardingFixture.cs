using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JsonGoddess.Compat;
using Xunit;

//оба пространства имён импортированы намеренно, и это само по себе проверка:
//у эталона пятнадцать из ста трёх методов - расширения над JsonDocument,
//JsonElement и JsonNode, и наши обязаны быть расширениями тоже. Если при этом
//вызов document.Deserialize<T>() станет неоднозначным, этот файл не соберётся.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Перегрузки без быстрого пути - те, что уходят эталону целиком. Их
    /// девяносто с лишним из ста трёх, и молчаливое расхождение в любой из них
    /// обошлось бы ровно так же дорого, как расхождение в быстром пути.
    ///
    /// <para>
    /// Проверяется не «работает», а «совпадает»: ответ эталона берётся тут же,
    /// в тесте. Литерал заморозил бы наше сегодняшнее поведение, а не его.
    /// </para>
    /// </summary>
    public class ForwardingFixture
    {
        private static JsonTypeInfo<Order> OrderInfo =>
            (JsonTypeInfo<Order>)JsonSerializerOptions.Default.GetTypeInfo(typeof(Order));

        // ---------- запись ----------

        [Fact]
        public void Serialize_by_Type_matches_the_reference()
        {
            var order = Order.CreateSample();

            Assert.Equal(
                Reference.Serialize(order, typeof(Order)),
                JsonSerializer.Serialize(order, typeof(Order))
                );
        }

        [Fact]
        public void Serialize_by_JsonTypeInfo_matches_the_reference()
        {
            var order = Order.CreateSample();

            //контракт назван явно - подменять его нашим нельзя, и здесь
            //проверяется именно то, что мы его не подменили
            Assert.Equal(
                Reference.Serialize(order, OrderInfo),
                JsonSerializer.Serialize(order, OrderInfo)
                );
        }

        [Fact]
        public void SerializeToNode_matches_the_reference()
        {
            var order = Order.CreateSample();

            var theirs = Reference.SerializeToNode(order);
            var ours = JsonSerializer.SerializeToNode(order);

            Assert.Equal(theirs!.ToJsonString(), ours!.ToJsonString());
        }

        [Fact]
        public void SerializeToElement_and_SerializeToDocument_match_the_reference()
        {
            var order = Order.CreateSample();

            var element = JsonSerializer.SerializeToElement(order);
            using var document = JsonSerializer.SerializeToDocument(order);

            var expected = Reference.SerializeToElement(order).GetRawText();

            Assert.Equal(expected, element.GetRawText());
            Assert.Equal(expected, document.RootElement.GetRawText());
        }

        [Fact]
        public void Serialize_into_a_stream_by_Type_matches_the_reference()
        {
            var order = Order.CreateSample();

            using var ours = new MemoryStream();
            using var theirs = new MemoryStream();

            JsonSerializer.Serialize(ours, order, typeof(Order));
            Reference.Serialize(theirs, order, typeof(Order));

            Assert.Equal(theirs.ToArray(), ours.ToArray());
        }

        // ---------- чтение ----------

        [Fact]
        public void Deserialize_by_Type_matches_the_reference()
        {
            var json = Reference.Serialize(Order.CreateSample());

            var ours = (Order?)JsonSerializer.Deserialize(json, typeof(Order));

            Assert.Equal(42, ours!.Id);
            Assert.Equal("Псков", ours.ShipTo!.City);
        }

        [Fact]
        public void Deserialize_by_JsonTypeInfo_matches_the_reference()
        {
            var json = Reference.Serialize(Order.CreateSample());

            var ours = JsonSerializer.Deserialize(json, OrderInfo);

            Assert.Equal(19.99m, ours!.Lines![0].Price);
        }

        [Fact]
        public void Deserialize_from_a_reader_matches_the_reference()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Serialize(Order.CreateSample()));

            var ourReader = new Utf8JsonReader(utf8);
            var theirReader = new Utf8JsonReader(utf8);

            var ours = JsonSerializer.Deserialize<Order>(ref ourReader);
            var theirs = Reference.Deserialize<Order>(ref theirReader);

            Assert.Equal(theirs!.Id, ours!.Id);
            Assert.Equal(theirs.Counters!["views"], ours.Counters!["views"]);
        }

        /// <summary>
        /// Форма расширения у потребителя, импортировавшего оба пространства
        /// имён. Она обязана <b>собираться</b> - и собирается только потому,
        /// что наши одноимённые методы расширениями быть перестали: пока они
        /// ими были, здесь стоял CS0121 на каждой из трёх строк.
        ///
        /// <para>
        /// Значение этого теста - в самом факте компиляции; проверки ниже лишь
        /// подтверждают, что связалось оно с рабочим кодом, а не с чем попало.
        /// </para>
        /// </summary>
        [Fact]
        public void The_extension_form_over_the_reference_model_stays_unambiguous()
        {
            var json = Reference.Serialize(Order.CreateSample());

            using var document = JsonDocument.Parse(json);
            var node = JsonNode.Parse(json);

            var fromDocument = document.Deserialize<Order>();
            var fromElement = document.RootElement.Deserialize<Order>();
            var fromNode = node.Deserialize<Order>();

            Assert.Equal(42, fromDocument!.Id);
            Assert.Equal(42, fromElement!.Id);
            Assert.Equal(42, fromNode!.Id);
        }

        /// <summary>
        /// Статическая форма тех же пятнадцати - она-то как раз наша: псевдоним
        /// <c>JsonSerializer</c> перекрывает импорт <c>System.Text.Json</c>, и
        /// то, что этот файл вообще собрался с обоими импортами, - его же и
        /// проверка.
        /// </summary>
        [Fact]
        public void The_static_form_over_the_reference_model_goes_through_the_facade()
        {
            var json = Reference.Serialize(Order.CreateSample());

            using var document = JsonDocument.Parse(json);

            var ours = JsonSerializer.Deserialize<Order>(document);
            var theirs = Reference.Deserialize<Order>(document);

            Assert.Equal(theirs!.Id, ours!.Id);
            Assert.Equal(theirs.Lines!.Count, ours.Lines!.Count);
        }

        // ---------- async ----------

        [Fact]
        public async Task DeserializeAsync_matches_the_reference()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Serialize(Order.CreateSample()));

            using var stream = new MemoryStream(utf8);
            var ours = await JsonSerializer.DeserializeAsync<Order>(stream);

            Assert.Equal("Пётр", ours!.Customer);
        }

        [Fact]
        public async Task SerializeAsync_matches_the_reference()
        {
            var order = Order.CreateSample();

            using var ours = new MemoryStream();
            using var theirs = new MemoryStream();

            await JsonSerializer.SerializeAsync(ours, order);
            await Reference.SerializeAsync(theirs, order);

            Assert.Equal(theirs.ToArray(), ours.ToArray());
        }

        [Fact]
        public async Task DeserializeAsyncEnumerable_matches_the_reference()
        {
            var utf8 = Encoding.UTF8.GetBytes("[{\"Sku\":\"A-1\",\"Quantity\":2,\"Price\":19.99},{\"Sku\":\"B-2\",\"Quantity\":1,\"Price\":5}]");

            using var stream = new MemoryStream(utf8);

            var skus = new List<string?>();
            await foreach (var line in JsonSerializer.DeserializeAsyncEnumerable<Line>(stream))
            {
                skus.Add(line!.Sku);
            }

            Assert.Equal(new[] { "A-1", "B-2", }, skus);
        }
    }
}
