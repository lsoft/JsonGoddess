using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.Compat.Interop;
using Xunit;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Мост (§10, маршрут B): порождённый код, выданный эталону под видом
    /// <c>JsonTypeInfo</c>.
    ///
    /// <para>
    /// Проверяется ровно одно, но с двух сторон: документ, прошедший через
    /// мост, обязан совпадать с документом эталона байт в байт, а прочитанное
    /// через мост - с прочитанным эталоном. Ожидание нигде не записано
    /// литералом: его называет <c>System.Text.Json</c> внутри теста.
    /// </para>
    ///
    /// <para>
    /// Опции у моста - те же самые, что у эталона рядом, плюс наш резолвер.
    /// Иначе сравнивались бы не два способа сделать одно, а два разных
    /// поведения.
    /// </para>
    /// </summary>
    public class BridgeFixture
    {
        private static readonly JsonSerializerOptions Bridge = new JsonSerializerOptions().UseJsonGoddess();

        [Fact]
        public void The_bridge_serves_at_least_one_type()
        {
            Assert.True(
                JsonGoddess.Compat.Interop.JsonGoddess.ServedTypeCount > 0,
                "the generator registered nothing with the bridge"
                );

            Assert.Contains(
                "serves",
                JsonGoddess.Compat.Interop.JsonGoddess.Explain(typeof(Order), Bridge),
                StringComparison.Ordinal
                );
        }

        [Fact]
        public void Writing_through_the_bridge_gives_the_reference_document()
        {
            var order = Order.CreateSample();

            Assert.Equal(Reference.Serialize(order), Reference.Serialize(order, Bridge));
        }

        [Fact]
        public void Writing_a_nested_root_goes_through_the_bridge_for_every_element()
        {
            var batch = new[] { Order.CreateSample(), Order.CreateSample(), };
            batch[1].Id = 43;

            Assert.Equal(Reference.Serialize(batch), Reference.Serialize(batch, Bridge));
        }

        [Fact]
        public void Reading_through_the_bridge_gives_what_the_reference_reads()
        {
            var json = Reference.Serialize(Order.CreateSample());

            var theirs = Reference.Deserialize<Order>(json);
            var ours = Reference.Deserialize<Order>(json, Bridge);

            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        [Fact]
        public void Reading_a_nested_root_goes_through_the_bridge_for_every_element()
        {
            var json = Reference.Serialize(new[] { Order.CreateSample(), Order.CreateSample(), });

            var theirs = Reference.Deserialize<Order[]>(json);
            var ours = Reference.Deserialize<Order[]>(json, Bridge);

            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        /// <summary>
        /// Экранированное имя свойства. Сырые байты <c>"\u0049d"</c> не
        /// совпадут с литералом <c>"Id"</c> ни при каком сравнении, и без
        /// медленного пути член остался бы непрочитанным <b>молча</b> - то
        /// есть документ прочитался бы, но неправильно.
        /// </summary>
        [Fact]
        public void An_escaped_property_name_is_still_matched()
        {
            const string json = "{\"\\u0049d\":7,\"Customer\":\"\\u041F\\u0451\\u0442\\u0440\"}";

            var theirs = Reference.Deserialize<Order>(json);
            var ours = Reference.Deserialize<Order>(json, Bridge);

            Assert.Equal(7, ours!.Id);
            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        /// <summary>
        /// Экранированное имя, которое не принадлежит ни одному члену. Путь
        /// самый длинный из всех: диспетчер промахивается, цепочка
        /// <c>ValueTextEquals</c> промахивается тоже, и только тогда свойство
        /// пропускается. После того как проверку имени убрали с горячего пути,
        /// этот случай стал единственным, где она вообще исполняется.
        /// </summary>
        [Fact]
        public void An_escaped_unknown_property_is_skipped()
        {
            const string json = "{\"Id\":3,\"\\u004Eope\":{\"deep\":[1,2]},\"Customer\":\"a\"}";

            Assert.Equal(
                Reference.Serialize(Reference.Deserialize<Order>(json)),
                Reference.Serialize(Reference.Deserialize<Order>(json, Bridge))
                );
        }

        /// <summary>
        /// Имя, разрезанное между сегментами, и имя экранированное - в одном
        /// документе и на разрезанном потоке. Обе формы промахиваются мимо
        /// диспетчера, и обе обязаны найтись медленным путём.
        /// </summary>
        [Fact]
        public async Task An_escaped_name_on_a_chunked_stream_is_still_matched()
        {
            var utf8 = Encoding.UTF8.GetBytes(
                "{\"\\u0049d\":11,\"Customer\":\"\\u041F\\u0451\\u0442\\u0440\",\"Nope\":1}"
                );

            using var theirsStream = new ChunkedStream(utf8, 3);
            using var oursStream = new ChunkedStream(utf8, 3);

            var theirs = await Reference.DeserializeAsync<Order>(theirsStream);
            var ours = await Reference.DeserializeAsync<Order>(oursStream, Bridge);

            Assert.Equal(11, ours!.Id);
            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        /// <summary>
        /// Отказ на негодном значении: текст совпадает с эталонным дословно,
        /// путь - нет.
        ///
        /// <para>
        /// Это объявленное расхождение (<c>docs/stj-divergences.md</c>, 4.6), и
        /// обойти его нечем: <c>JsonException.Path</c> доступен только на
        /// чтение, а заданный конструктором эталон не дополняет своим
        /// префиксом, а заменяет им (проверено пробой). Путь поэтому
        /// обрывается на входе в наш тип - но обрывается с верным началом, а не
        /// с верным концом.
        /// </para>
        ///
        /// <para>
        /// Тест закрепляет обе половины. Совпадение текста - потому что его
        /// читает человек и ловит чужой <c>catch</c>. Расхождение пути - потому
        /// что объявленное расхождение, никем не проверяемое, через полгода
        /// перестанет быть объявленным.
        /// </para>
        /// </summary>
        [Fact]
        public void A_refusal_repeats_the_reference_wording_but_not_its_path()
        {
            const string json = "{\"Id\":1,\"ShipTo\":{\"City\":7}}";

            var theirs = Assert.Throws<JsonException>(() => Reference.Deserialize<Order>(json));
            var ours = Assert.Throws<JsonException>(() => Reference.Deserialize<Order>(json, Bridge));

            const string sentence = "The JSON value could not be converted to System.String.";

            Assert.Contains(sentence, theirs.Message, StringComparison.Ordinal);
            Assert.Contains(sentence, ours.Message, StringComparison.Ordinal);

            //эталон называет член, мост - только тип, в котором споткнулся
            Assert.Equal("$.ShipTo.City", theirs.Path);
            Assert.Equal("$", ours.Path);
        }

        [Fact]
        public void An_unknown_property_is_ignored_the_way_the_reference_ignores_it()
        {
            const string json = "{\"Id\":1,\"Nope\":{\"deep\":[1,2,{\"x\":null}]},\"Customer\":\"a\"}";

            Assert.Equal(
                Reference.Serialize(Reference.Deserialize<Order>(json)),
                Reference.Serialize(Reference.Deserialize<Order>(json, Bridge))
                );
        }

        [Fact]
        public void A_repeated_property_is_accepted_the_way_the_reference_accepts_it()
        {
            const string json = "{\"Id\":1,\"Id\":2}";

            Assert.Equal(
                Reference.Serialize(Reference.Deserialize<Order>(json)),
                Reference.Serialize(Reference.Deserialize<Order>(json, Bridge))
                );
        }

        /// <summary>
        /// Имя в другом регистре эталон по умолчанию не узнаёт и пропускает
        /// как незнакомое. Мост обязан пропустить его так же, а не «понять».
        /// </summary>
        [Fact]
        public void A_name_in_another_case_is_not_matched()
        {
            const string json = "{\"id\":7}";

            Assert.Equal(
                Reference.Serialize(Reference.Deserialize<Order>(json)),
                Reference.Serialize(Reference.Deserialize<Order>(json, Bridge))
                );
        }

        /// <summary>
        /// Async-поток - та самая причина, ради которой мост и строился:
        /// маршрут A сюда не достаёт вовсе. Поток нарезан по семь байт
        /// намеренно: имя свойства при этом приезжает кусками, и читатель
        /// отдаёт его последовательностью, а не куском памяти.
        /// </summary>
        [Fact]
        public async Task An_async_stream_reads_the_same_way()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Serialize(Order.CreateSample()));

            using var theirsStream = new ChunkedStream(utf8, 7);
            using var oursStream = new ChunkedStream(utf8, 7);

            var theirs = await Reference.DeserializeAsync<Order>(theirsStream);
            var ours = await Reference.DeserializeAsync<Order>(oursStream, Bridge);

            Assert.Equal(Reference.Serialize(theirs), Reference.Serialize(ours));
        }

        [Fact]
        public async Task An_async_stream_writes_the_same_way()
        {
            var order = Order.CreateSample();

            using var theirs = new MemoryStream();
            using var ours = new MemoryStream();

            await Reference.SerializeAsync(theirs, order);
            await Reference.SerializeAsync(ours, order, Bridge);

            Assert.Equal(theirs.ToArray(), ours.ToArray());
        }

        /// <summary>
        /// Опции, отличающиеся от умолчаний, мост не берёт. Порождённый код
        /// написан под одно поведение, и отдать ему чужие опции значило бы
        /// выдать валидный документ, отличающийся от эталонного, - худший из
        /// исходов.
        /// </summary>
        /// <summary>
        /// Энкодер, выписанный явно в умолчание, мост берёт.
        ///
        /// Так поступает minimal API, и без этого мост отступал бы там по
        /// причине, которой нет. Одного этого, впрочем, мало: остальные
        /// веб-умолчания (camelCase, регистр, числа из строк) он всё ещё не
        /// умеет - см. PLAN.md §10.
        /// </summary>
        [Fact]
        public void The_default_encoder_written_out_explicitly_does_not_disable_the_bridge()
        {
            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
            }.UseJsonGoddess();

            var order = Order.CreateSample();

            Assert.Contains(
                "serves",
                JsonGoddess.Compat.Interop.JsonGoddess.Explain(typeof(Order), options),
                StringComparison.Ordinal
                );

            Assert.Equal(Reference.Serialize(order), Reference.Serialize(order, options));
        }

        [Theory]
        [MemberData(nameof(NonDefaultOptions))]
        public void Non_default_options_go_to_the_reference(string what, JsonSerializerOptions options)
        {
            var order = Order.CreateSample();

            var withoutBridge = new JsonSerializerOptions(options);
            var withBridge = new JsonSerializerOptions(options).UseJsonGoddess();

            Assert.Equal(Reference.Serialize(order, withoutBridge), Reference.Serialize(order, withBridge));

            Assert.Contains(
                "the options differ",
                JsonGoddess.Compat.Interop.JsonGoddess.Explain(typeof(Order), withBridge),
                StringComparison.Ordinal
                );

            Assert.False(string.IsNullOrEmpty(what));
        }

        public static IEnumerable<object[]> NonDefaultOptions => new[]
        {
            new object[] { "indented", new JsonSerializerOptions { WriteIndented = true, }, },
            new object[]
            {
                "camelCase",
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, },
            },
            new object[]
            {
                "case-insensitive",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, },
            },
            new object[]
            {
                "skip nulls",
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                },
            },
        };

        /// <summary>
        /// Отступление слышно, и слышно по делу: в сообщении названы те самые
        /// свойства, из-за которых мост отступил.
        ///
        /// <para>
        /// Набор опций взят не выдуманный, а тот, что строит ASP.NET Core
        /// (<c>JsonSerializerDefaults.Web</c>). Человек, у которого «не
        /// ускорилось», должен увидеть в сообщении настройки, которых он сам не
        /// ставил, - иначе искать ему нечего.
        /// </para>
        /// </summary>
        [Fact]
        public void The_bridge_says_out_loud_why_it_stepped_aside()
        {
            var heard = new List<string>();
            void Listen(Type type, string why) => heard.Add(type.Name + ": " + why);

            JsonGoddess.Compat.Interop.JsonGoddess.Declined += Listen;
            try
            {
                var web = new JsonSerializerOptions(JsonSerializerDefaults.Web).UseJsonGoddess();

                Reference.Serialize(Order.CreateSample(), web);
            }
            finally
            {
                JsonGoddess.Compat.Interop.JsonGoddess.Declined -= Listen;
            }

            var message = Assert.Single(heard);

            Assert.Contains("Order", message, StringComparison.Ordinal);
            Assert.Contains(nameof(JsonSerializerOptions.PropertyNamingPolicy), message, StringComparison.Ordinal);
            Assert.Contains(
                nameof(JsonSerializerOptions.PropertyNameCaseInsensitive),
                message,
                StringComparison.Ordinal
                );
            Assert.Contains(nameof(JsonSerializerOptions.NumberHandling), message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Без подписчика не должно случаться ничего - ни исключения, ни
        /// перечисления свойств рефлексией. Событие обязано быть бесплатным для
        /// того, кто им не пользуется.
        /// </summary>
        [Fact]
        public void Nobody_listening_costs_nothing_and_breaks_nothing()
        {
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web).UseJsonGoddess();
            var order = Order.CreateSample();

            Assert.Equal(
                Reference.Serialize(order, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Reference.Serialize(order, web)
                );
        }

        /// <summary>
        /// Поток, отдающий по кусочку за раз. Существует затем, чтобы читатель
        /// эталона действительно оказался в многосегментном режиме: с
        /// <c>MemoryStream</c> он вычитывает всё разом, и медленный путь имён
        /// не проверялся бы вовсе.
        /// </summary>
        private sealed class ChunkedStream : Stream
        {
            private readonly byte[] _data;
            private readonly int _chunk;
            private int _position;

            public ChunkedStream(byte[] data, int chunk)
            {
                _data = data;
                _chunk = chunk;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => _data.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var left = _data.Length - _position;
                var take = Math.Min(Math.Min(count, _chunk), left);
                Array.Copy(_data, _position, buffer, offset, take);
                _position += take;
                return take;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
