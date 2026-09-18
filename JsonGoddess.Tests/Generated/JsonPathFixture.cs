using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Путь в исключении (§6.4). Ожидание здесь ни разу не записано литералом
    /// «из головы»: для каждого документа путь спрашивается у самого
    /// <c>System.Text.Json</c> прогоном внутри теста и сравнивается с нашим.
    /// Литералы в <c>[InlineData]</c> есть, но это <b>документы</b>, а не
    /// ответы.
    ///
    /// Оба разбора обязаны отказать. Если эталон документ принял, а мы нет
    /// (или наоборот), тест обязан упасть на этом, а не молча сравнить два
    /// «отказа не было».
    /// </summary>
    public class JsonPathFixture
    {
        private const string Accepted = "<документ принят>";

        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

        private delegate void Read(byte[] utf8);

        private static string TheirPath<T>(string json)
        {
            try
            {
                JsonSerializer.Deserialize<T>(json);
                return Accepted;
            }
            catch (JsonException e)
            {
                return e.Path ?? "<null>";
            }
        }

        private static string OurPath(string json, Read read)
        {
            try
            {
                read(Utf8(json));
                return Accepted;
            }
            catch (JsonDocumentException e)
            {
                return e.Path ?? "<null>";
            }
        }

        private static void Same<T>(string json, Read read)
        {
            var theirs = TheirPath<T>(json);
            var ours = OurPath(json, read);

            Assert.NotEqual(Accepted, theirs);
            Assert.NotEqual(Accepted, ours);
            Assert.Equal(theirs, ours);
        }

        private static void ReadRoot(byte[] utf8)
        {
            PathRootSerializer.Deserialize(DefaultInjector.Instance, utf8, out PathRoot? _);
        }

        private static void ReadOdd(byte[] utf8)
        {
            PathOddSerializer.Deserialize(DefaultInjector.Instance, utf8, out PathOdd? _);
        }

        private static void ReadDemanding(byte[] utf8)
        {
            PathDemandingSerializer.Deserialize(DefaultInjector.Instance, utf8, out PathDemandingRoot? _);
        }

        // ---------- сегменты: свойство, индекс, вложенность ----------

        [Theory]
        //член не того рода в четвёртом элементе массива - индекс обязан быть
        //именно 3, а не 0 и не 4
        [InlineData(@"{""Orders"":[{""Id"":1},{""Id"":2},{""Id"":3},{""Id"":""abc""}]}")]
        //массив внутри объекта внутри массива
        [InlineData(@"{""Orders"":[{""Items"":[{""Qty"":1},{""Qty"":""x""}]}]}")]
        //третий уровень объектов
        [InlineData(@"{""Orders"":[{""Items"":[{""Inner"":{""Deep"":""x""}}]}]}")]
        //объект вне массива
        [InlineData(@"{""Head"":{""Qty"":""x""}}")]
        //null туда, где ждали число
        [InlineData(@"{""Orders"":[{""Id"":null}]}")]
        public void The_path_names_the_member_that_did_not_fit(string json)
        {
            Same<PathRoot>(json, ReadRoot);
        }

        [Theory]
        //синтаксис сломан внутри второго элемента
        [InlineData(@"{""Orders"":[{""Id"":1},{""Id"":,}]}")]
        //документ обрезан посреди значения
        [InlineData(@"{""Orders"":[{""Id"":1")]
        //обрезан сразу после имени
        [InlineData(@"{""Orders"":[{""Id""")]
        //мусор сразу за закрытым элементом: элемент дочитан, и индекс обязан
        //быть уже следующим
        [InlineData(@"{""Orders"":[{""Id"":1}@]}")]
        //запятая, за которой ничего нет: имя свойства уже сброшено
        [InlineData(@"{""Orders"":[{""Id"":1,}]}")]
        //корень не открылся
        [InlineData("{")]
        //мусор перед документом
        [InlineData("xx{}")]
        //мусор после документа
        [InlineData("{} x")]
        //пустой документ
        [InlineData("")]
        //корень не того рода
        [InlineData("[]")]
        public void The_path_of_a_syntax_failure_matches_the_reference(string json)
        {
            Same<PathRoot>(json, ReadRoot);
        }

        [Fact]
        public void An_escaped_property_name_appears_in_the_path_unescaped()
        {
            //пробой: эталон показывает в пути разэкранированное имя - Orders,
            //а не Orders
            Same<PathRoot>(@"{""Orders"":[{""Id"":""x""}]}", ReadRoot);
        }

        // ---------- скобочная форма сегмента ----------

        [Theory]
        [InlineData(@"{""na.me"":""x""}")]
        [InlineData(@"{""has space"":""x""}")]
        [InlineData(@"{""has'quote"":""x""}")]
        [InlineData(@"{""plain_name-1"":""x""}")]
        [InlineData(@"{""имя"":""x""}")]
        public void A_name_is_bracketed_exactly_when_the_reference_brackets_it(string json)
        {
            Same<PathOdd>(json, ReadOdd);
        }

        // ---------- отказ инжектора: якорь AfterToken ----------

        [Fact]
        public void A_token_the_injector_refused_is_named_by_the_member_it_did_not_fit()
        {
            //число прочитано сканером целиком и отвергнуто уже при укладке в
            //int - смещение к этому моменту стоит за концом лексемы, и без
            //отдельного якоря путь потерял бы последний сегмент
            Same<PathRoot>(@"{""Orders"":[{""Id"":99999999999999999999}]}", ReadRoot);
        }

        [Fact]
        public void The_injector_failure_arrives_as_a_document_exception_on_a_guarded_host()
        {
            var failure = Assert.Throws<JsonDocumentException>(
                () => ReadRoot(Utf8(@"{""Orders"":[{""Id"":99999999999999999999}]}"))
                );

            Assert.IsType<FormatException>(failure.InnerException);
            Assert.Equal("$.Orders[0].Id", failure.Path);
        }

        // ---------- отказ по объекту целиком: якорь EnclosingObject ----------

        [Theory]
        //обязательный член не пришёл во втором элементе: путь обязан назвать
        //элемент 1, а не 2 - элемента 2 в документе нет вовсе
        [InlineData(@"{""Orders"":[{""Id"":1},{""Name"":""x""}]}")]
        [InlineData(@"{""Orders"":[{}]}")]
        [InlineData(@"{""Orders"":[{""Id"":1},{""Id"":2},{""Name"":""x""}]}")]
        public void The_path_of_a_missing_required_member_names_the_object_that_lacked_it(string json)
        {
            Same<PathDemandingRoot>(json, ReadDemanding);
        }

        // ---------- строка и смещение внутри неё ----------

        [Fact]
        public void The_line_and_column_are_split_the_way_the_reference_splits_them()
        {
            var json = "{\n  \"Orders\": [\n    { \"Id\": \"x\" }\n  ]\n}";

            JsonException theirs;
            try
            {
                JsonSerializer.Deserialize<PathRoot>(json);
                throw new InvalidOperationException("эталон документ принял");
            }
            catch (JsonException e)
            {
                theirs = e;
            }

            var ours = Assert.Throws<JsonDocumentException>(() => ReadRoot(Utf8(json)));

            Assert.Equal(theirs.Path, ours.Path);
            Assert.Equal(theirs.LineNumber, ours.LineNumber);

            //смещение внутри строки у нас своё: эталон показывает конец
            //отвергнутой лексемы, а наш сканер - байт, на котором споткнулся.
            //Совпадает разбивка, а не число; расхождение записано в
            //docs/stj-divergences.md
            Assert.Equal(2, ours.LineNumber);
            Assert.True(ours.BytePositionInLine >= 0);
        }

        [Fact]
        public void The_message_carries_the_path_the_way_the_reference_carries_it()
        {
            var failure = Assert.Throws<JsonDocumentException>(
                () => ReadRoot(Utf8(@"{""Orders"":[{""Id"":""x""}]}"))
                );

            Assert.Contains("Path: $.Orders[0].Id", failure.Message);
            Assert.Contains("| LineNumber: 0 |", failure.Message);
            Assert.Contains("BytePositionInLine: ", failure.Message);
        }

        // ---------- хост без стражей платит ноль ----------

        [Fact]
        public void A_host_without_guards_gets_no_path_at_all()
        {
            var failure = Assert.Throws<JsonDocumentException>(
                () => PathRootPlainSerializer.Deserialize(
                    DefaultInjector.Instance, Utf8(@"{""Orders"":[{""Id"":""x""}]}"), out PathRoot? _
                    )
                );

            Assert.Null(failure.Path);
            Assert.Equal(-1, failure.LineNumber);
            Assert.Equal(-1, failure.BytePositionInLine);

            //короткое сообщение осталось ровно таким, каким было
            Assert.Contains("(byte ", failure.Message);
            Assert.DoesNotContain("Path:", failure.Message);
        }

        [Fact]
        public void A_host_without_guards_lets_the_injector_failure_through_untouched()
        {
            Assert.Throws<FormatException>(
                () => PathRootPlainSerializer.Deserialize(
                    DefaultInjector.Instance,
                    Utf8(@"{""Orders"":[{""Id"":99999999999999999999}]}"),
                    out PathRoot? _
                    )
                );
        }

        // ---------- осознанное расхождение с эталоном ----------

        [Fact]
        public void Inside_an_unknown_property_our_path_goes_deeper_than_the_reference()
        {
            //У эталона путь - это стек конвертеров, и внутрь незнакомого
            //свойства он не заходит: $.Unknown и точка. У нас путь отражает
            //структуру документа, и место названо точнее. Расхождение
            //осознанное, записано в docs/stj-divergences.md; тест закрепляет
            //его, а не прячет.
            var json = @"{""Unknown"":{""a"":[1,2,@]},""Orders"":[]}";

            Assert.Equal("$.Unknown", TheirPath<PathRoot>(json));
            Assert.Equal("$.Unknown.a[2]", OurPath(json, ReadRoot));
        }

        // ---------- MaxDepth ----------

        [Fact]
        public void The_path_of_a_depth_refusal_matches_the_reference()
        {
            //предел у хоста - 3; эталону он задаётся тем же числом, иначе
            //сравнивались бы два разных утверждения
            var json = @"{""Value"":1,""Child"":{""Value"":2,""Child"":{""Value"":3,""Child"":{""Value"":4}}}}";
            var options = new JsonSerializerOptions
            {
                MaxDepth = 3,
            };

            string theirs;
            try
            {
                JsonSerializer.Deserialize<GuardChainNode>(json, options);
                theirs = Accepted;
            }
            catch (JsonException e)
            {
                theirs = e.Path ?? "<null>";
            }

            var ours = OurPath(
                json,
                utf8 => MaxDepthNodeSerializer.Deserialize(DefaultInjector.Instance, utf8, out GuardChainNode? _)
                );

            Assert.NotEqual(Accepted, theirs);
            Assert.Equal(theirs, ours);
        }

        // ---------- сам построитель пути ----------

        [Fact]
        public void The_path_builder_never_throws_and_always_answers_with_a_path()
        {
            //построитель работает поверх заведомо битого документа, и
            //исключение из него подменило бы настоящую причину отказа
            var random = new Random(20260918);
            var bytes = new byte[64];

            for (var i = 0; i < 20000; i++)
            {
                random.NextBytes(bytes);

                for (var j = 0; j < bytes.Length; j++)
                {
                    //без структурных байтов случайный шум не даёт проходу
                    //ничего интересного
                    if ((bytes[j] & 7) == 0)
                    {
                        bytes[j] = (byte)"{}[],:\"\\"[bytes[j] % 8];
                    }
                }

                var anchor = (JsonPathAnchor)(i % 3);
                var path = JsonPath.Build(bytes, i % (bytes.Length + 2), anchor);

                Assert.StartsWith("$", path);
            }
        }

        [Fact]
        public void The_path_builder_tolerates_a_position_outside_the_document()
        {
            var document = Utf8(@"{""Orders"":[{""Id"":1}]}");

            Assert.Equal("$", JsonPath.Build(document, -5, JsonPathAnchor.AtByte));
            Assert.Equal("$", JsonPath.Build(document, 0, JsonPathAnchor.AtByte));
            Assert.Equal("$", JsonPath.Build(document, document.Length + 100, JsonPathAnchor.AtByte));
        }
    }
}
