using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JsonGoddess.Internal;
using JsonGoddess.Tests.Generated;
using JsonGoddess.Tests.Stj;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// Фаза 7, устойчивость: <b>ни один</b> испорченный документ не должен
    /// выносить из читателя ничего, кроме отказа, который мы объявили.
    ///
    /// Критерий фазы сформулирован в плане как «ни одного
    /// <c>IndexOutOfRangeException</c> на корпусе», и это не про один тип
    /// исключения, а про сорт ошибки: читатель ходит по спану индексами, и
    /// любое обращение за конец - признак того, что проверка границы забыта.
    /// Такая ошибка не чинится сообщением: на другом входе та же забытая
    /// проверка прочитает чужую память вместо того, чтобы упасть.
    ///
    /// Корпус <b>порождается</b>, а не лежит файлами, и порождается
    /// детерминированно: одни и те же мутации при каждом прогоне. Файлы
    /// пришлось бы обновлять руками при каждой правке модели, а сгенерированные
    /// мутации следуют за ней сами.
    ///
    /// Мутации четырёх родов, и каждый ловит свой сорт забытой проверки:
    /// <list type="bullet">
    /// <item><b>обрезание</b> - каждый префикс документа; ловит чтение за
    /// концом там, где код рассчитывал на «дальше точно что-то есть»;</item>
    /// <item><b>подмена байта</b> - на структурные и на злые
    /// (<c>0x00</c>, <c>0x80</c>, <c>0xFF</c>); ловит разбор, который доверяет
    /// тому, что уже видел;</item>
    /// <item><b>удаление байта</b> - ловит рассинхронизацию структуры;</item>
    /// <item><b>вставка байта</b> - то же самое с другой стороны.</item>
    /// </list>
    /// </summary>
    public class MalformedInputFixture
    {
        /// <summary>
        /// Байты, которыми стоит портить. Структурные - потому что ими
        /// документ ломается «правдоподобно», и разбор заходит дальше, чем на
        /// мусоре; <c>0x00</c>, <c>0x80</c> и <c>0xFF</c> - потому что первый
        /// управляющий, а два других не бывают началом кодовой точки UTF-8.
        /// </summary>
        private static readonly byte[] Nasty =
        {
            (byte)'"', (byte)'\\', (byte)'{', (byte)'}', (byte)'[', (byte)']',
            (byte)':', (byte)',', (byte)'0', (byte)'e', (byte)'-', (byte)'.',
            (byte)'t', 0x00, 0x80, 0xFF,
        };

        /// <summary>
        /// Что читателю позволено выбрасывать на испорченном документе.
        ///
        /// <c>JsonDocumentException</c> - наш отказ по §6.4.
        /// <c>FormatException</c> - канал инжектора: значение прочитано как
        /// лексема, но не разобралось в тип члена. Оба - объявленное
        /// поведение; всё остальное означает, что читатель споткнулся, а не
        /// отказал.
        /// </summary>
        private static bool IsDeclaredRefusal(Exception e) =>
            e is JsonDocumentException || e is FormatException;

        [Fact]
        public void No_mutation_of_a_flat_document_escapes_as_anything_but_a_refusal()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Flat.CreateSample()));

            Check(json, utf8 => FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out _));
        }

        [Fact]
        public void No_mutation_of_a_nested_document_escapes_as_anything_but_a_refusal()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Basket.CreateSample()));

            Check(json, utf8 => BasketSerializer.Deserialize(DefaultInjector.Instance, utf8, out _));
        }

        /// <summary>
        /// Документ со строками, escape'ами и не-ASCII: мутации здесь падают в
        /// декодер строк, которого плоский документ почти не задевает.
        /// </summary>
        [Fact]
        public void No_mutation_of_a_unicode_document_escapes_as_anything_but_a_refusal()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Unicode.CreateSample()));

            Check(json, utf8 => UnicodeSerializer.Deserialize(DefaultInjector.Instance, utf8, out _));
        }

        /// <summary>
        /// Хост со стражами: тот же корпус, но каждый отказ вдобавок проходит
        /// через построитель пути (§6.4). Построитель работает поверх заведомо
        /// битого документа, и исключение из него подменило бы настоящую
        /// причину отказа - а обнаружилось бы это как чужое исключение,
        /// которое <see cref="Check"/> и ловит.
        /// </summary>
        [Fact]
        public void No_mutation_escapes_the_path_builder_of_a_guarded_host()
        {
            var json = Encoding.UTF8.GetBytes(
                Reference.Write(
                    new PathRoot
                    {
                        Head = new PathItem { Qty = 7, Inner = new PathInner { Deep = 8, }, },
                        Orders = new List<PathOrder>
                        {
                            new PathOrder { Id = 1, Name = "первый", },
                            new PathOrder
                            {
                                Id = 2,
                                Name = "второй",
                                Items = new List<PathItem> { new PathItem { Qty = 3, }, },
                            },
                        },
                    }
                    )
                );

            Check(json, utf8 => PathRootSerializer.Deserialize(DefaultInjector.Instance, utf8, out PathRoot? _));
        }

        /// <summary>
        /// Мутации по одному байту не создают <b>сочетаний</b>: испорченная
        /// кавычка плюс испорченная скобка - это другой путь разбора, чем
        /// каждая из них по отдельности. Здесь портится сразу по нескольку
        /// байт, а зерно фиксировано: тест, который иногда красный, не
        /// сообщение об ошибке, а лотерея.
        /// </summary>
        [Fact]
        public void Multi_byte_corruption_escapes_as_anything_but_a_refusal()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Basket.CreateSample()));
            var random = new Random(20260918);
            var found = new List<string>();

            for (var i = 0; i < 20000; i++)
            {
                var mutant = (byte[])json.Clone();
                var hits = 1 + random.Next(6);

                for (var h = 0; h < hits; h++)
                {
                    mutant[random.Next(mutant.Length)] = Nasty[random.Next(Nasty.Length)];
                }

                try
                {
                    BasketSerializer.Deserialize(DefaultInjector.Instance, mutant, out _);
                }
                catch (Exception e) when (IsDeclaredRefusal(e))
                {
                    continue;
                }
                catch (Exception e)
                {
                    if (!found.Any(f => f.StartsWith(e.GetType().FullName!, StringComparison.Ordinal)))
                    {
                        found.Add(e.GetType().FullName + " on " + Describe(mutant) + " | " + e.Message);
                    }
                }
            }

            Assert.True(
                found.Count == 0,
                "испорченный документ вынес из читателя не отказ:" + Environment.NewLine
                + string.Join(Environment.NewLine, found)
                );
        }

        /// <summary>
        /// Документы, до которых мутации сами не доберутся: они не «испорченный
        /// правильный», а сразу патологические. Здесь же место всему, что
        /// когда-либо ломало читателя, - по одной строке на находку.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("{")]
        [InlineData("[")]
        [InlineData("\"")]
        [InlineData("{\"")]
        [InlineData("{\"a")]
        [InlineData("{\"a\"")]
        [InlineData("{\"a\":")]
        [InlineData("{\"a\":1")]
        [InlineData("{\"a\":1,")]
        [InlineData("{\"Id\":")]
        [InlineData("{\"Id\":-")]
        [InlineData("{\"Id\":-e")]
        [InlineData("{\"Id\":1e")]
        [InlineData("{\"Id\":1e+")]
        [InlineData("{\"Id\":.5}")]
        [InlineData("{\"Id\":99999999999999999999999999}")]
        [InlineData("{\"Customer\":\"\\u")]
        [InlineData("{\"Customer\":\"\\uD83D\"}")]
        [InlineData("{\"Customer\":\"\\uDE00\"}")]
        [InlineData("{\"Customer\":\"\\q\"}")]
        [InlineData("{\"Customer\":\"\xff\xfe\"}")]
        [InlineData("}")]
        [InlineData("]")]
        [InlineData(":")]
        [InlineData(",")]
        [InlineData("nul")]
        [InlineData("tru")]
        [InlineData("{\"Id\":1}extra")]
        public void Handcrafted_pathological_document_is_refused_not_crashed(string text)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);

            try
            {
                FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out _);
            }
            catch (Exception e) when (IsDeclaredRefusal(e))
            {
                //объявленный отказ - ровно то, чего мы и хотим
            }
        }

        /// <summary>
        /// Глубокая вложенность в <b>незнакомом</b> свойстве стек не рушит:
        /// пропуск поддерева (<c>JsonScan.SkipValue</c>) держит глубину
        /// счётчиком, а не рекурсией. Это утверждение о реализации, и потому
        /// закреплено тестом, а не комментарием.
        /// </summary>
        [Fact]
        public void Deeply_nested_unknown_subtree_does_not_recurse()
        {
            const int Depth = 20000;

            var text = new StringBuilder("{\"Unknown\":");
            text.Append('[', Depth);
            text.Append(']', Depth);
            text.Append('}');

            var utf8 = Encoding.UTF8.GetBytes(text.ToString());

            FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out var result);

            Assert.NotNull(result);
        }

        /// <summary>
        /// Обрезание той же глубокой вложенности: отказ, а не срыв.
        /// </summary>
        [Fact]
        public void Deeply_nested_unknown_subtree_cut_short_is_refused()
        {
            const int Depth = 20000;

            var text = new StringBuilder("{\"Unknown\":");
            text.Append('[', Depth);

            var utf8 = Encoding.UTF8.GetBytes(text.ToString());

            var e = Record.Exception(
                () => FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out _)
                );

            Assert.NotNull(e);
            Assert.True(IsDeclaredRefusal(e!), "вышло " + e!.GetType().FullName + ": " + e.Message);
        }

        /// <summary>
        /// Прогон корпуса. Собирает <b>все</b> находки и падает один раз со
        /// списком: чинить их всё равно придётся пачкой, а падение на первой
        /// прятало бы остальные.
        /// </summary>
        /// <summary>
        /// Корпус обязан быть непустым, и проверять это надо отдельно: фуз-тест
        /// с пустым корпусом проходит так же уверенно, как с полным, - он
        /// просто не проверяет ничего. Числа ниже посчитаны из формулы
        /// мутаций, а не подогнаны под факт.
        /// </summary>
        [Fact]
        public void Corpus_is_not_empty()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Flat.CreateSample()));
            var expected = (json.Length + 1) + json.Length * (1 + 2 * Nasty.Length) - Duplicates(json);

            Assert.True(json.Length > 100, "документ-основа подозрительно короток: " + json.Length);
            Assert.Equal(expected, Mutations(json).Count());
        }

        /// <summary>
        /// Подмена байта на него же пропускается - иначе в корпус попал бы
        /// исходный документ, и тест на нём ничего бы не проверил.
        /// </summary>
        private static int Duplicates(byte[] json) =>
            json.Count(b => Nasty.Contains(b));

        /// <summary>
        /// <b>Тест на сам тест.</b> Фуз-прогон, который ничего не ловит,
        /// выглядит точно так же, как прогон, которому нечего ловить, - и
        /// первый хуже, чем никакого. Поэтому в корпус подсовывается читатель,
        /// который заведомо спотыкается, и проверяется, что находка
        /// зарегистрирована.
        /// </summary>
        [Fact]
        public void The_corpus_run_actually_reports_a_foreign_exception()
        {
            var json = Encoding.UTF8.GetBytes(Reference.Write(Flat.CreateSample()));

            var found = Collect(json, _ => throw new IndexOutOfRangeException("подброшено"));

            Assert.Single(found);
            Assert.Contains("IndexOutOfRangeException", found[0], StringComparison.Ordinal);
        }

        private static void Check(byte[] json, Action<byte[]> read)
        {
            var found = Collect(json, read);

            Assert.True(
                found.Count == 0,
                "испорченный документ вынес из читателя не отказ:" + Environment.NewLine
                + string.Join(Environment.NewLine, found)
                );
        }

        private static List<string> Collect(byte[] json, Action<byte[]> read)
        {
            var found = new List<string>();

            foreach (var mutant in Mutations(json))
            {
                try
                {
                    read(mutant);
                }
                catch (Exception e) when (IsDeclaredRefusal(e))
                {
                    continue;
                }
                catch (Exception e)
                {
                    var report = e.GetType().FullName + " on " + Describe(mutant);

                    //по одной строке на тип исключения: одна и та же забытая
                    //проверка даёт сотни мутантов, и список из сотен строк
                    //ничего не добавляет к списку из одной
                    if (!found.Any(f => f.StartsWith(e.GetType().FullName!, StringComparison.Ordinal)))
                    {
                        found.Add(report + " | " + e.Message);
                    }
                }
            }

            return found;
        }

        private static IEnumerable<byte[]> Mutations(byte[] json)
        {
            //обрезание: каждый префикс, включая пустой
            for (var length = 0; length <= json.Length; length++)
            {
                var cut = new byte[length];
                Array.Copy(json, cut, length);
                yield return cut;
            }

            for (var i = 0; i < json.Length; i++)
            {
                //подмена
                foreach (var b in Nasty)
                {
                    if (json[i] == b)
                    {
                        continue;
                    }

                    var changed = (byte[])json.Clone();
                    changed[i] = b;
                    yield return changed;
                }

                //удаление
                var shorter = new byte[json.Length - 1];
                Array.Copy(json, 0, shorter, 0, i);
                Array.Copy(json, i + 1, shorter, i, json.Length - i - 1);
                yield return shorter;

                //вставка
                foreach (var b in Nasty)
                {
                    var longer = new byte[json.Length + 1];
                    Array.Copy(json, 0, longer, 0, i);
                    longer[i] = b;
                    Array.Copy(json, i, longer, i + 1, json.Length - i);
                    yield return longer;
                }
            }
        }

        /// <summary>
        /// Документ в отчёте - печатаемым текстом: непечатаемое байтом в hex,
        /// иначе сообщение о падении само становится нечитаемым.
        /// </summary>
        private static string Describe(byte[] json)
        {
            var text = new StringBuilder(json.Length + 2);
            text.Append('\'');

            foreach (var b in json)
            {
                if (b >= 0x20 && b < 0x7F)
                {
                    text.Append((char)b);
                }
                else
                {
                    text.Append("\\x").Append(b.ToString("X2"));
                }
            }

            text.Append('\'');
            return text.ToString();
        }
    }
}
