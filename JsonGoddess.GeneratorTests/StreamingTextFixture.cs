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
        /// Полиморфный субъект потоковый читатель пока не печатает, и
        /// неспособность <b>заразна</b>: тип, у которого такой тип членом,
        /// прочитан потоком тоже быть не может. Отказ молчаливый в том смысле,
        /// что работа уходит обычному пути; голос ему даст пункт 8.
        /// </summary>
        [Fact]
        public void A_polymorphic_subject_is_not_served_and_the_refusal_spreads_upwards()
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

            //сам полиморфный тип и тот, кто держит его членом, - не обслужены
            Assert.DoesNotContain("TryRead_Demo_Animal", text);
            Assert.DoesNotContain("TryRead_Demo_Shelter", text);

            //а сосед по хосту от этого не страдает
            Assert.Contains("TryRead_Demo_Plain", text);
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
        /// Хост, где обслуживать нечего, не печатает пустого файла: файл,
        /// которого может не быть, лучше видно отсутствующим.
        /// </summary>
        [Fact]
        public void A_host_with_nothing_servable_prints_no_file_at_all()
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

            Assert.Null(Streaming(Run(source, On)));
        }
    }
}
