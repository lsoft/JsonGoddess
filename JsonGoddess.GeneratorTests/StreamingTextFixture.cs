using System.Collections.Generic;
using System.Linq;
using JsonGoddess.GeneratorTests.Harness;
using Xunit;

namespace JsonGoddess.GeneratorTests
{
    /// <summary>
    /// Потоковый читатель (PLAN.md §12.9, фаза 10, пункт 6б) - второй
    /// partial-файл хоста, печатаемый в форме «вернуть <c>false</c> вместо
    /// отказа, когда байты кончились».
    ///
    /// <para>
    /// Главное утверждение здесь - не текст, а <b>пустота
    /// <see cref="GeneratorRun.CompilationErrors"/></b>: она означает, что
    /// напечатанное действительно компилируется вместе с обычным читателем в
    /// одном классе. Поведение же проверяется не здесь, а прогоном: драйвер
    /// прототипа зовёт именно порождённый читатель, и его 1120 смещений обрыва,
    /// нарезка по одному байту и запрос в ASP.NET - это проверки печати.
    /// </para>
    /// </summary>
    public class StreamingTextFixture
    {
        private static readonly IReadOnlyDictionary<string, string> On =
            new Dictionary<string, string> { ["JsonGoddessStreaming"] = "enable", };

        private static readonly IReadOnlyDictionary<string, string> Off =
            new Dictionary<string, string> { ["JsonGoddessStreaming"] = "disable", };

        private static GeneratorRun Run(string source, IReadOnlyDictionary<string, string> properties)
        {
            return GeneratorHarness.Run(new[] { new SourceFile("Subject.cs", source), }, properties);
        }

        private static string? Streaming(GeneratorRun run)
        {
            return run.GeneratedFiles
                .Where(f => f.Key.Contains(".Streaming."))
                .Select(f => f.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// Консольному приложению потоковый путь не нужен ни разу: тело там уже
        /// в памяти целиком, и обычный читатель на нём быстрее. Молчание здесь -
        /// это отсутствие второго читателя на каждый тип, а не мелочь.
        /// </summary>
        [Fact]
        public void Nothing_is_printed_without_asking_and_without_aspnet()
        {
            var run = GeneratorHarness.Run(Sources.Host("public int Id { get; set; }"));

            Assert.Null(Streaming(run));
        }

        [Fact]
        public void The_property_turns_it_off_even_where_it_would_be_printed()
        {
            var run = Run(Sources.Host("public int Id { get; set; }"), Off);

            Assert.Null(Streaming(run));
        }

        [Fact]
        public void The_property_turns_it_on_and_what_it_prints_compiles()
        {
            var run = Run(
                Sources.Host(
                    @"public int Id { get; set; }
        public string? Name { get; set; }
        public bool Paid { get; set; }
        public decimal Total { get; set; }
        public System.Collections.Generic.List<int>? Numbers { get; set; }"
                    ),
                On
                );

            Assert.Empty(run.CompilationErrors);

            var text = Streaming(run);
            Assert.NotNull(text);

            Assert.Contains("TryRead_Demo_Subject", text);
            Assert.Contains("TryReadScalar_Int32", text);
            Assert.Contains("TryReadCollection_", text);
            Assert.Contains("bool final,", text);
        }

        /// <summary>
        /// Единственное отличие от обычного читателя, которое обязано быть
        /// видно в каждом вызове сканера: исход «не хватило».
        /// </summary>
        [Fact]
        public void Every_scanner_call_carries_the_finality_flag()
        {
            var text = Streaming(Run(Sources.Host("public int Id { get; set; }"), On))!;

            Assert.Contains("__TryScan.Expect(json, ref position, __TryScan.OpenBrace, final)", text);
            Assert.Contains("return false;", text);
            Assert.DoesNotContain("__Scan.", text);
        }

        [Fact]
        public void Guards_and_features_reach_the_streaming_reader_too()
        {
            var text = Streaming(
                Run(
                    Sources.Host(
                        "public int Id { get; set; }",
                        "[JsonGuard(JsonGuard.StrictNumbers | JsonGuard.MaxDepth, MaxDepth = 7)]"
                        ),
                    On
                    )
                )!;

            Assert.Contains("__TryScan.ReadNumberRawStrict", text);
            Assert.Contains("context.Depth++", text);

            //глубина возвращается на любом выходе, включая «не хватило»
            Assert.Contains("context.Depth--", text);
            Assert.Contains("finally", text);
        }

        [Fact]
        public void Comments_reach_the_streaming_reader_too()
        {
            var text = Streaming(
                Run(
                    Sources.Host("public int Id { get; set; }", "[JsonFeature(JsonFeature.Comments)]"),
                    On
                    )
                )!;

            Assert.Contains("__TryScan.SkipWhitespaceAndComments(json, ref position, final)", text);
        }

        /// <summary>
        /// Полиморфный субъект обслуживается - и сам, и на месте члена
        /// (PLAN.md §15, O11 (а)). До этого он не читался потоком вовсе, и
        /// неспособность была <b>заразна</b>: один <c>[JsonDerivedType]</c> на
        /// дне графа снимал обслуживание со всех, кто до него дотягивался.
        ///
        /// <para>
        /// Утверждений здесь два, и второе не слабее первого: печатается тело
        /// каждого производного отдельным читателем, а держатель полиморфного
        /// члена обслуживается наравне с прочими - то есть каскад снят.
        /// </para>
        /// </summary>
        [Fact]
        public void A_polymorphic_subject_is_served_and_so_is_its_holder()
        {
            var source = @"
using System;
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal
    {
        public int Id { get; set; }
    }

    public class Dog : Animal
    {
        public bool Good { get; set; }
    }

    public class Shelter
    {
        public Animal? Tenant { get; set; }
    }

    public class Plain
    {
        public int Id { get; set; }
    }

    [JsonSubject(typeof(Shelter), true)]
    [JsonSubject(typeof(Animal), false)]
    [JsonSubject(typeof(Dog), false)]
    [JsonSubject(typeof(Plain), true)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run);

            Assert.Empty(run.CompilationErrors);
            Assert.True(
                text is not null,
                "файлы: " + string.Join(", ", run.GeneratedFiles.Keys)
                + "; диагностики: " + string.Join(", ", run.DiagnosticIds)
                );

            //сам полиморфный тип, тело его производного и тот, кто держит
            //полиморфный тип членом
            Assert.Contains("TryRead_Demo_Animal", text);
            Assert.Contains("TryReadBody_Demo_Dog_As_Demo_Animal", text);
            Assert.Contains("TryRead_Demo_Shelter", text);

            //сосед по хосту как был, так и остался
            Assert.Contains("TryRead_Demo_Plain", text);

            //диспетчер дискриминатора: имя, откат позиции и уход в тело
            Assert.Contains("__hasDiscriminator", text);
            Assert.Contains("position = __discriminatorStart;", text);

            //каскада больше нет - жаловаться не на что
            Assert.DoesNotContain("JGD005", run.DiagnosticIds);
        }

        /// <summary>
        /// Корнем полиморфный тип читается <b>массивом</b>, но не одиночным
        /// объектом, и это отказ по числу, а не по сложности: поимущественный
        /// драйвер создаёт объект до чтения свойств, а тип известен только
        /// после дискриминатора. Одиночный объект корнем даёт 0.96 по лестнице
        /// - выигрывать там нечего (PLAN.md §15, O11 (в)).
        /// </summary>
        [Fact]
        public void A_polymorphic_root_gets_the_array_driver_but_not_the_single_one()
        {
            var source = @"
using System;
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    [JsonDerivedType(typeof(Dog), ""dog"")]
    public class Animal
    {
        public int Id { get; set; }
    }

    public class Dog : Animal
    {
        public bool Good { get; set; }
    }

    [JsonSubject(typeof(Animal), true)]
    [JsonSubject(typeof(Dog), false)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run);

            Assert.Empty(run.CompilationErrors);
            Assert.True(text is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));

            Assert.Contains("StreamReadArray_Demo_Animal", text);
            Assert.Contains("StreamReadList_Demo_Animal", text);

            Assert.DoesNotContain("StreamReadOne_Demo_Animal", text);
            Assert.DoesNotContain("TryReadName_Demo_Animal", text);
        }

        /// <summary>
        /// Драйвер (пункт 6в) печатается на корень, и только на него: спускаться
        /// по свойствам имеет смысл там, где объект и есть весь документ.
        /// </summary>
        [Fact]
        public void A_root_gets_both_drivers_and_the_descent_into_a_collection()
        {
            var source = @"
using System;
using JsonGoddess;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Demo
{
    public class Line
    {
        public int Quantity { get; set; }
    }

    public class Subject
    {
        public int Id { get; set; }
        public List<Line>? Lines { get; set; }
    }

    [JsonSubject(typeof(Subject), true)]
    [JsonSubject(typeof(Line), false)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run)!;

            Assert.Empty(run.CompilationErrors);

            //массив и список читает один автомат, объект - другой
            Assert.Contains("StreamReadArray_Demo_Subject", text);
            Assert.Contains("StreamReadList_Demo_Subject", text);
            Assert.Contains("StreamReadOne_Demo_Subject", text);

            //поимущественное чтение и спуск в коллекцию
            Assert.Contains("TryReadName_Demo_Subject", text);
            Assert.Contains("TryReadValue_Demo_Subject", text);
            Assert.Contains("спуск до элемента", text);

            //вложенный тип корнем не объявлен - драйвера у него нет
            Assert.DoesNotContain("StreamReadArray_Demo_Line", text);
        }

        /// <summary>
        /// Отложенная сборка снимает поимущественное чтение: пока не прочитано
        /// всё, объекта не существует - класть свойство некуда. Единицей
        /// переигрывания остаётся объект целиком, и это не отказ, а другая цена.
        /// </summary>
        [Fact]
        public void A_constructor_bound_root_keeps_the_whole_object_as_the_retry_unit()
        {
            var source = @"
using System;
using JsonGoddess;
using System.Text.Json.Serialization;

namespace Demo
{
    public class Subject
    {
        public Subject(int id) => Id = id;

        public int Id { get; }
    }

    [JsonSubject(typeof(Subject), true)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run)!;

            Assert.Empty(run.CompilationErrors);

            Assert.Contains("StreamReadArray_Demo_Subject", text);
            Assert.DoesNotContain("StreamReadOne_Demo_Subject", text);
            Assert.DoesNotContain("TryReadName_Demo_Subject", text);
        }

        /// <summary>
        /// Субъект-коллекция обслуживается: членом, элементом и элементом
        /// корневого массива (PLAN.md §15, O11 (б)). Раньше не обслуживался
        /// вовсе, и неспособность была заразна - вместе с ним выпадал весь
        /// граф над ним.
        /// </summary>
        [Fact]
        public void A_collection_subject_is_served_and_so_is_its_holder()
        {
            var source = @"
using System;
using System.Collections.Generic;
using JsonGoddess;

namespace Demo
{
    public class Basket : List<string>
    {
    }

    public class Tags : Dictionary<string, int>
    {
    }

    public class Cart
    {
        public int Id { get; set; }
        public Basket? Items { get; set; }
        public Tags? Marks { get; set; }
    }

    [JsonSubject(typeof(Cart), true)]
    [JsonSubject(typeof(Basket), false)]
    [JsonSubject(typeof(Tags), false)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run);

            Assert.Empty(run.CompilationErrors);
            Assert.True(text is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));

            //обе формы субъекта-коллекции и держатель
            Assert.Contains("TryRead_Demo_Basket", text);
            Assert.Contains("TryRead_Demo_Tags", text);
            Assert.Contains("TryRead_Demo_Cart", text);

            //элемент кладётся через приведение к интерфейсу, а не прямым
            //вызовом: субъект мог реализовать его явно
            Assert.Contains("global::System.Collections.Generic.ICollection<", text);
            Assert.Contains(")result).Add(item);", text);
            Assert.Contains("global::System.Collections.Generic.IDictionary<string, ", text);
            Assert.Contains(")result)[key] = item;", text);

            //держатель читается корнем-одиночкой как ни в чём не бывало
            Assert.Contains("StreamReadOne_Demo_Cart", text);
            Assert.DoesNotContain("JGD005", run.DiagnosticIds);
        }

        /// <summary>
        /// А вот корнем-одиночкой субъект-коллекция не читается - единицей
        /// переигрывания был бы элемент, и такого драйвера ещё нет, - и
        /// <b>об этом сказано</b>. Молчаливого отказа здесь быть не должно:
        /// массив такого типа форматтер читает, одиночное значение отдаёт
        /// эталону, и разницу иначе пришлось бы искать замером.
        /// </summary>
        [Fact]
        public void A_collection_subject_root_is_read_as_an_array_but_not_alone_and_that_is_said()
        {
            var source = @"
using System;
using System.Collections.Generic;
using JsonGoddess;

namespace Demo
{
    public class Basket : List<string>
    {
    }

    [JsonSubject(typeof(Basket), true)]
    public partial class SubjectSerializer
    {
    }
}
";

            var run = Run(source, On);
            var text = Streaming(run);

            Assert.Empty(run.CompilationErrors);
            Assert.True(text is not null, "файлы: " + string.Join(", ", run.GeneratedFiles.Keys));

            Assert.Contains("StreamReadArray_Demo_Basket", text);
            Assert.DoesNotContain("StreamReadOne_Demo_Basket", text);

            Assert.Contains("JGD005", run.DiagnosticIds);

            var said = run.GeneratorDiagnostics.First(d => d.Id == "JGD005").GetMessage();

            Assert.Contains("Demo.Basket", said);
            Assert.Contains("collection subject", said);
            Assert.Contains("a single one is not", said);
        }
    }
}
