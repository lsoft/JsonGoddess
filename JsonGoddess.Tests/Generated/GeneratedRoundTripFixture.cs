using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Internal;
using JsonGoddess.Tests.Stj;
using Xunit;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Критерий готовности фазы 2: плоский POCO со всеми builtin'ами, код
    /// которому пишет <b>генератор</b>, а не рука.
    ///
    /// Ожидание нигде не записано литералом. Документ сравнивается с тем, что
    /// пишет <c>System.Text.Json</c>, а прочитанный объект - с тем, что читает
    /// он же; сравнивать прочитанное с исходным значило бы позволить
    /// симметричной ошибке писателя и читателя погасить друг друга.
    /// </summary>
    public class GeneratedRoundTripFixture
    {
        [Fact]
        public void Flat_document_matches_system_text_json_byte_for_byte()
        {
            var value = Flat.CreateSample();

            Assert.Equal(Reference.Write(value), Serialize(value));
        }

        [Fact]
        public void Flat_document_is_read_the_way_system_text_json_reads_it()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Write(Flat.CreateSample()));

            var theirs = JsonSerializer.Deserialize<Flat>(utf8, Reference.Relaxed);
            FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Тридцать одинаковой длины имён - форма, на которой эмиттер выбирает
        /// switch по ключу. Здесь эта форма исполняется, а не только печатается.
        /// </summary>
        [Fact]
        public void Switch_by_key_reads_and_writes_the_same_document()
        {
            var value = Wide.CreateSample();
            var text = Reference.Write(value);

            Assert.Equal(text, SerializeWide(value));

            WideSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(text), out var back);
            Assert.Equal(text, Reference.Write(back));
        }

        /// <summary>
        /// Четыре имени с одним ключом: если бы эмиттер не собрал их в одну
        /// ветку и не разделил сравнением, сюда приехали бы перепутанные
        /// значения - а компилятор бы промолчал, потому что каждая ветка сама
        /// по себе законна.
        /// </summary>
        [Fact]
        public void Colliding_keys_do_not_mix_members_up()
        {
            var value = Colliding.CreateSample();
            var text = Reference.Write(value);

            CollidingSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(text), out var back);

            Assert.NotNull(back);
            Assert.Equal(1, back!.ReferenceA);
            Assert.Equal(2, back.ReferenceB);
            Assert.Equal(3, back.ReferenceC);
            Assert.Equal(4, back.ReferenceD);
        }

        [Fact]
        public void Renamed_and_ignored_members_follow_system_text_json()
        {
            var value = Renamed.CreateSample();
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            RenamedSerializer.Serialize(exhauster, value);

            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));
            Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);

            RenamedSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(text), out var back);
            Assert.Equal(Reference.Write(JsonSerializer.Deserialize<Renamed>(text, Reference.Relaxed)), Reference.Write(back));
        }

        /// <summary>
        /// Хост без <c>[JsonExhauster]</c>: перегрузка порождается под базовый
        /// класс, вызовы виртуальные, документ обязан быть тем же самым.
        /// </summary>
        [Fact]
        public void Host_without_registered_sinks_produces_the_same_document()
        {
            var value = Renamed.CreateSample();

            using var exhauster = new PooledUtf8Exhauster();
            RenamedBaseSerializer.Serialize(exhauster, value);

            Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));

            RenamedBaseSerializer.Deserialize(
                DefaultInjector.Instance,
                Encoding.UTF8.GetBytes(Reference.Write(value)),
                out var back
                );

            Assert.Equal(Reference.Write(value), Reference.Write(back));
        }

        [Fact]
        public void Null_root_is_written_and_read_as_null()
        {
            using var exhauster = new PooledUtf8Exhauster();
            FlatSerializer.Serialize(exhauster, null);

            Assert.Equal("null", Encoding.UTF8.GetString(exhauster.ToArray()));

            FlatSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes("null"), out var back);
            Assert.Null(back);
        }

        [Fact]
        public void Empty_object_leaves_every_member_at_its_default()
        {
            FlatSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes("{}"), out var back);

            Assert.NotNull(back);
            Assert.Equal(Reference.Write(new Flat()), Reference.Write(back));
        }

        /// <summary>
        /// Неизвестное свойство пропускается вместе со всем поддеревом - это
        /// поведение по умолчанию, такое же, как у System.Text.Json. Отказ
        /// появится в фазе 6 стражем <c>UnknownProperties</c>.
        /// </summary>
        [Theory]
        [InlineData("{\"Unknown\":{\"a\":[1,2,{\"b\":null}]},\"Id\":7}")]
        [InlineData("{\"Id\":7,\"Unknown\":[[[]]]}")]
        [InlineData("{\"Id\":7,\"Unknown\":\"a string with a } brace\"}")]
        public void Unknown_properties_are_skipped_whole(string json)
        {
            FlatSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(json), out var back);

            Assert.NotNull(back);
            Assert.Equal(7, back!.Id);
        }

        /// <summary>
        /// Экранированное имя - законный JSON: <c>"\u0049d"</c> означает
        /// <c>Id</c>, и означать что-то другое не может. Диспетчер сравнивает
        /// сырые байты и с такой формой не совпадает, поэтому имя
        /// разэкранируется и проходит через тот же switch второй раз.
        ///
        /// Платят за это только документы, в которых такое имя есть: до
        /// разэкранирования управление доходит лишь после того, как диспетчер
        /// никого не нашёл, а найденный член уходит по goto раньше.
        /// </summary>
        [Theory]
        [InlineData("{\"\\u0049d\":7}")]
        [InlineData("{\"I\\u0064\":7}")]
        [InlineData("{\"\\u0049\\u0064\":7}")]
        public void Escaped_property_name_names_the_same_member(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.NotNull(ours);
            Assert.Equal(7, ours!.Id);

            //и то же самое читает эталон - утверждение здесь дифференциальное,
            //а не про наши ожидания
            Assert.Equal(JsonSerializer.Deserialize<Flat>(utf8, Reference.Relaxed)!.Id, ours.Id);
        }

        /// <summary>
        /// Экранированное имя, которое никакому члену не соответствует, обязано
        /// просто пропуститься - так же, как пропустилось бы неэкранированное.
        /// Второй заход в диспетчер не должен превращаться в бесконечный цикл.
        /// </summary>
        [Fact]
        public void Escaped_unknown_property_is_skipped_like_any_other()
        {
            var utf8 = Encoding.UTF8.GetBytes("{\"\\u0055nknown\":{\"a\":[1,2]},\"Id\":7}");

            FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out var back);

            Assert.NotNull(back);
            Assert.Equal(7, back!.Id);
        }

        /// <summary>
        /// Суррогатная пара в имени: два <c>\uXXXX</c> складываются в одну
        /// кодовую точку и четыре байта UTF-8. Сверяется с тем, как то же имя
        /// пишет System.Text.Json без агрессивного энкодера.
        /// </summary>
        [Fact]
        public void Escaped_surrogate_pair_in_a_name_resolves_to_the_same_bytes()
        {
            var raw = Encoding.UTF8.GetBytes("{\"\\ud83d\\ude00\":\"Ada\"}");

            var expected = Encoding.UTF8.GetBytes(Reference.Write(new Astral { Name = "Ada", }));
            AstralSerializer.Deserialize(DefaultInjector.Instance, expected, out var direct);
            AstralSerializer.Deserialize(DefaultInjector.Instance, raw, out var escaped);

            Assert.Equal("Ada", direct!.Name);
            Assert.Equal("Ada", escaped!.Name);
        }

        [Theory]
        [InlineData("{\"\\u004\":7}")]
        [InlineData("{\"\\q\":7}")]
        [InlineData("{\"\\\":7}")]
        public void Malformed_escape_in_a_name_is_a_document_error(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            Assert.Throws<JsonDocumentException>(
                () => FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out _)
                );
        }

        /// <summary>
        /// Не теоретический случай, а тот, который выдаёт
        /// <c>System.Text.Json</c> с опциями по умолчанию: его энкодер
        /// разворачивает в <c>\uXXXX</c> весь не-ASCII, и имя свойства - не
        /// исключение. То есть POCO с кириллическим <c>[JsonPropertyName]</c>
        /// сериализуется эталоном в форму, которую мы обязаны уметь читать,
        /// иначе совместимость односторонняя.
        ///
        /// Обе формы одного имени проверяются рядом: экранированная и сырая
        /// обязаны дать один и тот же объект.
        /// </summary>
        [Fact]
        public void Name_escaped_by_system_text_json_defaults_is_read_back()
        {
            var value = Unicode.CreateSample();

            var escaped = Reference.WriteDefaultEncoder(value);
            Assert.Contains("\\u0438", escaped, StringComparison.Ordinal);

            var raw = Reference.Write(value);
            Assert.Contains("имя", raw, StringComparison.Ordinal);

            UnicodeSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(escaped), out var fromEscaped);
            UnicodeSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(raw), out var fromRaw);

            Assert.Equal("Ada", fromEscaped!.Name);
            Assert.Equal("Ada", fromRaw!.Name);

            //и эталон на том же документе даёт то же самое
            Assert.Equal(
                JsonSerializer.Deserialize<Unicode>(escaped, Reference.Relaxed)!.Name,
                fromEscaped.Name
                );
        }

        [Fact]
        public void Nullable_members_accept_both_null_and_a_value()
        {
            var json = "{\"MaybeCount\":null,\"MaybeReference\":\"6f9619ff-8b86-d011-b42d-00cf4fc964ff\",\"Customer\":null}";

            FlatSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(json), out var back);

            Assert.NotNull(back);
            Assert.Null(back!.MaybeCount);
            Assert.Null(back.Customer);
            Assert.Equal(new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"), back.MaybeReference);
        }

        private static string Serialize(Flat value)
        {
            using var exhauster = new PooledUtf8Exhauster();
            FlatSerializer.Serialize(exhauster, value);
            return Encoding.UTF8.GetString(exhauster.ToArray());
        }

        private static string SerializeWide(Wide value)
        {
            using var exhauster = new PooledUtf8Exhauster();
            WideSerializer.Serialize(exhauster, value);
            return Encoding.UTF8.GetString(exhauster.ToArray());
        }
    }
}
