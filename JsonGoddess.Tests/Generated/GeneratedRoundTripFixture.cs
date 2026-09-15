using System;
using System.Linq;
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

        /// <summary>
        /// Составной документ целиком: вложенный субъект, коллекция субъектов,
        /// массив чисел, коллекция строк, коллекция коллекций и <c>byte[]</c>,
        /// который обязан остаться base64-строкой.
        /// </summary>
        [Fact]
        public void Composite_document_matches_system_text_json_byte_for_byte()
        {
            var value = Basket.CreateSample();

            using var exhauster = new PooledUtf8Exhauster();
            BasketSerializer.Serialize(exhauster, value);

            Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));
        }

        [Fact]
        public void Composite_document_is_read_the_way_system_text_json_reads_it()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Write(Basket.CreateSample()));

            var theirs = JsonSerializer.Deserialize<Basket>(utf8, Reference.Relaxed);
            BasketSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Пустой объект, пустая коллекция, <c>null</c> на месте коллекции и
        /// <c>null</c> на месте элемента-субъекта - четыре формы, в которых
        /// ошибиться легче всего, и все четыре законны.
        /// </summary>
        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Lines\":[],\"Numbers\":[],\"Tags\":[],\"Matrix\":[]}")]
        [InlineData("{\"Lines\":null,\"Numbers\":null,\"Head\":null,\"Matrix\":null}")]
        [InlineData("{\"Lines\":[null],\"Tags\":[null],\"Matrix\":[null,[]]}")]
        [InlineData("{\"Matrix\":[[1],[2,3],[]],\"Numbers\":[7]}")]
        public void Degenerate_collection_forms_are_read_the_way_system_text_json_reads_them(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            var theirs = JsonSerializer.Deserialize<Basket>(utf8, Reference.Relaxed);
            BasketSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Тип, ссылающийся сам на себя: читатель и писатель взаимно
        /// рекурсивны, и глубина документа ограничена только стеком.
        /// </summary>
        [Fact]
        public void Self_referencing_type_round_trips()
        {
            var value = Node.CreateSample();
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            NodeSerializer.Serialize(exhauster, value);
            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));

            NodeSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(text), out var back);
            Assert.Equal(text, Reference.Write(back));
        }

        /// <summary>
        /// Условно опускаемые члены в обоих крайних состояниях: всё на
        /// умолчаниях (остаются только безусловные) и всё заполнено. Между ними
        /// лежит вся арифметика запятых, и байтовое совпадение с эталоном -
        /// единственная проверка, которая её ловит.
        /// </summary>
        [Fact]
        public void Conditional_members_are_omitted_exactly_where_system_text_json_omits_them()
        {
            foreach (var value in new[] { Sparse.CreateEmpty(), Sparse.CreateFull(), })
            {
                using var exhauster = new PooledUtf8Exhauster();
                SparseSerializer.Serialize(exhauster, value);

                Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));
            }
        }

        /// <summary>
        /// Когда условны все члены, документ может оказаться пустым объектом - и
        /// фигурная скобка перестаёт склеиваться с именем первого члена.
        /// </summary>
        [Theory]
        [InlineData(null, null)]
        [InlineData("a", null)]
        [InlineData(null, "b")]
        [InlineData("a", "b")]
        public void Object_with_only_conditional_members_still_matches(string? a, string? b)
        {
            var value = new AllSparse { A = a, B = b, };

            using var exhauster = new PooledUtf8Exhauster();
            SparseSerializer.Serialize(exhauster, value);

            Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));
        }

        /// <summary>
        /// Опущенный на записи член читается как обычный: условие относится к
        /// записи и только к ней.
        /// </summary>
        [Fact]
        public void Conditionally_omitted_members_are_still_read()
        {
            var utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"Ada\",\"Count\":7,\"Maybe\":0}");

            var theirs = JsonSerializer.Deserialize<Sparse>(utf8, Reference.Relaxed);

            //тип назван явно: у хоста несколько корней, и перегрузки Deserialize
            //различаются только out-параметром, так что out var неоднозначен
            SparseSerializer.Deserialize(DefaultInjector.Instance, utf8, out Sparse? ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
            Assert.Equal(7, ours!.Count);
        }

        [Fact]
        public void Explicit_member_order_is_applied_on_top_of_the_hierarchy()
        {
            var value = new OrderedDerived { BaseA = 1, BaseEarly = 2, DerivedA = 3, Late = 4, LateToo = 5, };
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            SparseSerializer.Serialize(exhauster, value);

            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));

            Assert.Equal(
                new[] { "BaseEarly", "DerivedA", "BaseA", "Late", "LateToo", },
                System.Text.RegularExpressions.Regex.Matches(text, "\"(\\w+)\":")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToArray()
                );
        }

        [Fact]
        public void Enum_document_matches_system_text_json_byte_for_byte()
        {
            var value = Marks.CreateSample();

            using var exhauster = new PooledUtf8Exhauster();
            MarksSerializer.Serialize(exhauster, value);

            Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));
        }

        /// <summary>
        /// Формы, в которых enum отличается от всего остального. Строковый
        /// режим принимает четыре разных написания одного значения, числовой -
        /// только число; значение вне набора законно в обоих. Каждое
        /// утверждение снимается с эталона на том же документе.
        /// </summary>
        [Theory]
        [InlineData("{\"Plain\":40}")]
        [InlineData("{\"Plain\":123}")]
        [InlineData("{\"Plain\":0,\"Maybe\":1}")]
        [InlineData("{\"Maybe\":null}")]
        [InlineData("{\"Big\":18446744073709551615}")]
        [InlineData("{\"Mode\":\"Sent\"}")]
        [InlineData("{\"Mode\":\"sent\"}")]
        [InlineData("{\"Mode\":\"SENT\"}")]
        [InlineData("{\"Mode\":\"\\u0053ent\"}")]
        [InlineData("{\"Mode\":\"sent-out\"}")]
        [InlineData("{\"Mode\":1}")]
        [InlineData("{\"Mode\":\"1\"}")]
        [InlineData("{\"Mode\":77}")]
        [InlineData("{\"MaybeMode\":null}")]
        [InlineData("{\"Modes\":[\"Draft\",\"sent-out\"]}")]
        [InlineData("{\"States\":{\"a\":40}}")]
        public void Enum_forms_are_read_the_way_system_text_json_reads_them(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            var theirs = JsonSerializer.Deserialize<Marks>(utf8, Reference.Relaxed);
            MarksSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// И обратная сторона: то, что эталон отвергает, обязаны отвергать и
        /// мы. Имя члена, переименованного через
        /// <c>[JsonStringEnumMemberName]</c>, в другом регистре - именно такой
        /// случай, и угадать его было нельзя: у остальных членов регистр не
        /// значим.
        ///
        /// Утверждение здесь про <b>отказ</b>, а не про тип исключения: тип
        /// зависит от слоя, на котором документ споткнулся. Лексема не той
        /// формы - это <c>JsonDocumentException</c> от сканера, а лексема
        /// нужной формы с нечитаемым содержимым - <c>FormatException</c> от
        /// инжектора. Единый тип наружу - отдельный вопрос, и решать его внутри
        /// работы над enum'ами было бы не к месту.
        /// </summary>
        [Theory]
        [InlineData("{\"Mode\":\"Nope\"}")]
        [InlineData("{\"Mode\":\"SENT-OUT\"}")]
        [InlineData("{\"Mode\":\"Forwarded\"}")]
        [InlineData("{\"Plain\":\"Sent\"}")]
        [InlineData("{\"Plain\":null}")]
        public void Enum_forms_refused_by_system_text_json_are_refused_here_too(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Marks>(utf8, Reference.Relaxed));

            var ours = Record.Exception(() => MarksSerializer.Deserialize(DefaultInjector.Instance, utf8, out _));

            Assert.NotNull(ours);
            Assert.True(
                ours is JsonDocumentException or FormatException,
                "expected a document or format failure, got " + ours!.GetType().Name
                );
        }

        /// <summary>
        /// Словарь целиком: неудобные ключи, вложенный словарь, словарь
        /// субъектов с <c>null</c>-значением, словарь коллекций.
        /// </summary>
        [Fact]
        public void Dictionary_document_matches_system_text_json_byte_for_byte()
        {
            var value = Catalogue.CreateSample();

            using var exhauster = new PooledUtf8Exhauster();
            CatalogueSerializer.Serialize(exhauster, value);

            Assert.Equal(Reference.Write(value), Encoding.UTF8.GetString(exhauster.ToArray()));
        }

        [Fact]
        public void Dictionary_document_is_read_the_way_system_text_json_reads_it()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Write(Catalogue.CreateSample()));

            var theirs = JsonSerializer.Deserialize<Catalogue>(utf8, Reference.Relaxed);
            CatalogueSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Формы, в которых словарь отличается от списка: повторённый ключ,
        /// экранированный ключ, пустой объект и <c>null</c> на месте словаря.
        /// Повторённый ключ - тот случай, где <c>Add</c> бросил бы, а эталон
        /// оставляет последнее вхождение.
        /// </summary>
        [Theory]
        [InlineData("{\"Counts\":{\"a\":1,\"a\":2}}")]
        [InlineData("{\"Counts\":{\"\\u0061\":1}}")]
        [InlineData("{\"Counts\":{\"A\":1,\"a\":2}}")]
        [InlineData("{\"Counts\":{}}")]
        [InlineData("{\"Counts\":null,\"Items\":null}")]
        [InlineData("{\"Items\":{\"k\":null}}")]
        [InlineData("{\"Nested\":{\"o\":{}}}")]
        public void Degenerate_dictionary_forms_are_read_the_way_system_text_json_reads_them(string json)
        {
            var utf8 = Encoding.UTF8.GetBytes(json);

            var theirs = JsonSerializer.Deserialize<Catalogue>(utf8, Reference.Relaxed);
            CatalogueSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Ключ, экранированный энкодером эталона по умолчанию, обязан
        /// читаться - ровно как имя свойства. Разница в том, что для ключа это
        /// не обходной путь, а обычный: сравнивать его не с чем, он и так
        /// материализуется строкой.
        /// </summary>
        [Fact]
        public void Dictionary_key_escaped_by_system_text_json_defaults_is_read_back()
        {
            var value = new Catalogue
            {
                Counts = new System.Collections.Generic.Dictionary<string, int> { { "имя", 1 }, },
            };

            var escaped = Reference.WriteDefaultEncoder(value);
            Assert.Contains("\\u0438", escaped, StringComparison.Ordinal);

            CatalogueSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(escaped), out var ours);

            Assert.Equal(1, ours!.Counts!["имя"]);
        }

        /// <summary>
        /// Член без setter'а пишется, но не читается - и коллекция здесь не
        /// исключение, хотя наполнить уже созданный список технически можно.
        /// Эталон с опциями по умолчанию её тоже не наполняет, и утверждение
        /// снимается с него, а не с моего представления о нём.
        /// </summary>
        [Fact]
        public void Getter_only_collection_is_skipped_on_read_just_like_system_text_json_skips_it()
        {
            var utf8 = Encoding.UTF8.GetBytes("{\"Tags\":[1,2],\"Scalar\":9}");

            var theirs = JsonSerializer.Deserialize<Fixed>(utf8, Reference.Relaxed);
            FixedSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
            Assert.Empty(ours!.Tags);
        }

        /// <summary>
        /// Порядок членов при наследовании. Утверждение здесь не «мы печатаем
        /// от базового к производному» и не наоборот, а «документ совпадает с
        /// документом <c>System.Text.Json</c>» - потому что это единственное,
        /// что имеет значение, и единственное, что нельзя вывести из
        /// рассуждений.
        ///
        /// Проверка стои́т отдельно от сравнения строк: одинаковые значения
        /// членов сделали бы перепутанный порядок незаметным, поэтому значения
        /// в образце разные, а порядок дополнительно вынут именами.
        /// </summary>
        [Fact]
        public void Inherited_members_are_ordered_the_way_system_text_json_orders_them()
        {
            var value = DerivedEntity.CreateSample();
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            DerivedEntitySerializer.Serialize(exhauster, value);

            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));

            Assert.Equal(
                new[] { "Z", "M", "A", "B", },
                System.Text.RegularExpressions.Regex.Matches(text, "\"(\\w+)\":")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToArray()
                );
        }

        /// <summary>
        /// Структура-субъект: документ и порядок членов те же, что у эталона.
        /// Порядок вынут отдельно, потому что у структуры поле среди свойств -
        /// обычное дело, и правило «свойства раньше полей» здесь заметнее, чем
        /// у класса.
        /// </summary>
        [Fact]
        public void Struct_subject_writes_what_system_text_json_writes()
        {
            var value = Coordinate.CreateSample();
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            CoordinateSerializer.Serialize(exhauster, value);

            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));
            Assert.Equal(new[] { "X", "Y", "Sum", "Label", }, Names(text));
        }

        /// <summary>
        /// get-only свойство структуры эталон пишет, а на чтении молча роняет.
        /// Повторяем в точности: чтение сравнивается с его же чтением, а не с
        /// исходным объектом, - иначе потеря выглядела бы нашей ошибкой.
        /// </summary>
        [Fact]
        public void Struct_subject_is_read_the_way_system_text_json_reads_it()
        {
            var utf8 = Encoding.UTF8.GetBytes(Reference.Write(Coordinate.CreateSample()));

            var theirs = JsonSerializer.Deserialize<Coordinate>(utf8, Reference.Relaxed);
            CoordinateSerializer.Deserialize(DefaultInjector.Instance, utf8, out Coordinate ours);

            Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
        }

        /// <summary>
        /// Позиционная <c>record struct</c>: отдельного кода ей не нужно, но
        /// утверждение об этом стои́т прогоном.
        /// </summary>
        [Fact]
        public void Positional_record_struct_needs_no_special_case()
        {
            var value = new Segment(1, 2);
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            SegmentSerializer.Serialize(exhauster, value);
            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));

            SegmentSerializer.Deserialize(DefaultInjector.Instance, Encoding.UTF8.GetBytes(text), out Segment back);
            Assert.Equal(text, Reference.Write(back));
        }

        /// <summary>
        /// Структура на месте члена во всех видах сразу: сама,
        /// <c>Nullable&lt;&gt;</c> и внутри трёх коллекций. Два образца, потому
        /// что заполненный <c>Nullable</c> и пустая коллекция дают документы
        /// другого строения.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Struct_members_round_trip_in_every_shape(bool filled)
        {
            var value = filled ? Route.CreateFilled() : Route.CreateSample();
            var text = Reference.Write(value);

            using var exhauster = new PooledUtf8Exhauster();
            RouteSerializer.Serialize(exhauster, value);
            Assert.Equal(text, Encoding.UTF8.GetString(exhauster.ToArray()));

            var utf8 = Encoding.UTF8.GetBytes(text);
            RouteSerializer.Deserialize(DefaultInjector.Instance, utf8, out Route? ours);

            Assert.Equal(
                Reference.Write(JsonSerializer.Deserialize<Route>(utf8, Reference.Relaxed)),
                Reference.Write(ours)
                );
        }

        /// <summary>
        /// <c>null</c> на месте не-nullable структуры - отказ, и у эталона он
        /// тоже отказ (<c>JsonException</c>). Принять его значило бы принять
        /// документ, которого тип описать не может.
        /// </summary>
        [Fact]
        public void Null_where_a_struct_is_expected_is_refused_by_both()
        {
            var utf8 = Encoding.UTF8.GetBytes("{\"Start\":null}");

            Assert.ThrowsAny<Exception>(
                () => JsonSerializer.Deserialize<Route>(utf8, Reference.Relaxed)
                );

            Assert.ThrowsAny<Exception>(
                () => RouteSerializer.Deserialize(DefaultInjector.Instance, utf8, out Route? _)
                );
        }

        private static string[] Names(string text)
        {
            return System.Text.RegularExpressions.Regex.Matches(text, "\"(\\w+)\":")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .ToArray();
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
