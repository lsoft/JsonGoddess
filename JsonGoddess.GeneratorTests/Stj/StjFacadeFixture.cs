using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Маршрут B из §11.1 и приёмка фазы 8: их корпус, прогнанный <b>поверх
    /// фасада</b>.
    ///
    /// <para>
    /// Разница с <see cref="StjCorpusFixture"/> в том, кто зовёт. Там вызов
    /// шёл в порождённый хост по имени - проверялся генератор. Здесь код
    /// написан так, как его пишет человек: <c>JsonSerializer.Serialize(value)</c>
    /// и опции по умолчанию. Всё, что между этой строкой и результатом -
    /// перехват, связывание, регистрация, отступление - обязано сработать
    /// само.
    /// </para>
    ///
    /// <para>
    /// Эталон здесь берётся <b>с настройками по умолчанию</b>, а не с
    /// <c>UnsafeRelaxedJsonEscaping</c>, как в маршруте A. Потребитель фасада
    /// энкодер не выбирал: он поменял пакет, а не код. Сверять его с эталоном,
    /// настроенным под нас, значило бы проверять удобную нам подстановку
    /// вместо той, которую он получит.
    /// </para>
    ///
    /// <para>
    /// Сравниваются <b>исходы</b>, а не значения: у чужого корпуса половина
    /// типов не обслуживается и самим эталоном (<c>System.Type</c> в членах,
    /// коллекция без сеттера, комментарии в документе), и он отвечает на них
    /// исключением. Требование тут то же самое - фасад обязан отказать там же
    /// и тем же видом исключения; сравнение одних только успехов эти случаи
    /// молча выбросило бы из проверки.
    /// </para>
    /// </summary>
    public class StjFacadeFixture
    {
        /// <summary>
        /// Направления, по которым сверяется каждый тип. Ключ - «тип и
        /// направление»: и тесты, и отчёт спрашивают <see cref="Compare"/>, и
        /// разъехаться им негде.
        /// </summary>
        private const string Write = "=";
        private const string WriteUtf8 = "u8";
        private const string ReadReference = "→";
        private const string ReadTheirs = "←";
        private const string TheirVerify = "V";

        /// <summary>
        /// Расхождения, о которых мы знаем и которые считаем своими.
        ///
        /// <para>
        /// Список закрытый и проверяется в обе стороны: незаявленное
        /// расхождение красит тест, а заявленное, которое <b>перестало</b>
        /// расходиться, красит его тоже. Второе не придирка: решение о том,
        /// что фасад пишет не то же, что эталон, - последнее, которое можно
        /// отменить молча.
        /// </para>
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> Declared =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ClassWithUnicodeProperty|" + Write] =
                    "набор экранируемого: энкодер эталона по умолчанию разворачивает весь не-ASCII в "
                    + "`\\uXXXX`, включая имена свойств; мы пишем минимум RFC 8259 §7 (§8.4)",
                ["ClassWithUnicodeProperty|" + WriteUtf8] =
                    "то же расхождение на UTF-8-выходе",
            };

        public static IEnumerable<object[]> Served =>
            StjFacade.Specimens.Where(s => s.IsBound).Select(s => new object[] { s, });

        public static IEnumerable<object[]> Forwarded =>
            StjFacade.Specimens.Where(s => !s.IsBound).Select(s => new object[] { s, });

        public static IEnumerable<object[]> WithDocument =>
            StjFacade.Specimens.Where(s => s.Document is not null).Select(s => new object[] { s, });

        public static IEnumerable<object[]> WithVerify =>
            StjFacade.Specimens.Where(s => s.Verify is not null).Select(s => new object[] { s, });

        /// <summary>
        /// Код потребителя вместе со всем, что генератор по нему напечатал,
        /// <b>собирается</b>. Это первое утверждение фазы 8 и самое дешёвое:
        /// перехват, не ломающий чужую сборку, - минимальное, что drop-in
        /// обязан.
        /// </summary>
        [Fact]
        public void Consumer_code_over_the_facade_compiles()
        {
            Assert.Empty(StjFacade.CompilationErrors.Select(e => e.ToString()));
        }

        /// <summary>
        /// Проверка самой проверки. Все утверждения ниже сравнивают фасад с
        /// эталоном, а у типа, ушедшего эталону, фасад <b>и есть</b> эталон.
        /// Обслужи быстрый путь ноль типов - весь этот файл был бы зелёным и
        /// не проверял бы ничего.
        /// </summary>
        [Fact]
        public void The_fast_path_actually_served_part_of_the_corpus()
        {
            var served = StjFacade.Specimens.Count(s => s.IsBound);

            Assert.True(
                served >= 30,
                "быстрый путь обслужил " + served + " типов корпуса; всё, что проверяется ниже, "
                + "имеет смысл только пока это число не ноль"
                );
        }

        /// <summary>
        /// Отступление обязано быть <b>сказано</b>. Тип, ушедший эталону,
        /// работает - но потребитель, считающий, что ускорился весь его код,
        /// должен узнать обратное от компилятора, а не от профайлера.
        /// </summary>
        [Fact]
        public void The_retreat_is_said_out_loud()
        {
            var retreats = StjFacade.Diagnostics.Where(d => d.Id == "JGD001").ToList();

            Assert.NotEmpty(retreats);
            Assert.All(retreats, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));

            //JGD002 - это дыра в генераторе: обход граф принял, а печать
            //сорвалась. На чужом корпусе из ста с лишним типов его отсутствие -
            //утверждение, которого своими тестами не получить
            Assert.Empty(StjFacade.Diagnostics.Where(d => d.Id == "JGD002").Select(d => d.GetMessage()));
        }

        [Theory]
        [MemberData(nameof(Served))]
        public void The_document_is_the_one_the_reference_writes(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, Write));
        }

        [Theory]
        [MemberData(nameof(Served))]
        public void The_utf8_document_is_the_one_the_reference_writes(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, WriteUtf8));
        }

        /// <summary>
        /// Читается документ <b>эталона</b>, а не наш: чтение обязано
        /// проверяться отдельно от записи, иначе симметричная ошибка писателя
        /// и читателя погасит друг друга.
        /// </summary>
        [Theory]
        [MemberData(nameof(Served))]
        public void Reading_the_reference_document_gives_what_the_reference_reads(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, ReadReference));
        }

        /// <summary>
        /// <b>Их</b> документ - тот, что накоплен багрепортами, - через фасад.
        /// Форматирование в нём чужое: пробелы, иной порядок членов,
        /// комментарии.
        /// </summary>
        [Theory]
        [MemberData(nameof(WithDocument))]
        public void Reading_their_own_document_gives_what_the_reference_reads(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, ReadTheirs));
        }

        /// <summary>
        /// Их собственное утверждение об объекте - на том, что вернул фасад.
        /// Сверяется не «прошло», а «прошло там же, где у эталона»: их
        /// <c>Verify</c> проверяет и те члены, которых эталон по умолчанию не
        /// пишет вовсе (поля без <c>[JsonInclude]</c>), и на них падает сам.
        /// </summary>
        [Theory]
        [MemberData(nameof(WithVerify))]
        public void Their_own_assertions_hold_where_they_hold_for_the_reference(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, TheirVerify));
        }

        /// <summary>
        /// Тип, от которого генератор отказался, обязан <b>продолжать
        /// работать</b>. Это и есть смысл отступления, и проверяется оно тем
        /// же способом, что и обслуженный: сравнением с эталоном. Утверждение
        /// слабое по содержанию (фасад здесь буквально зовёт эталон) и сильное
        /// по смыслу: оно ловит случай, когда перехват сломал вызов, который
        /// был обязан просто пройти насквозь.
        /// </summary>
        [Theory]
        [MemberData(nameof(Forwarded))]
        public void A_type_the_generator_refused_still_works(StjFacadeSpecimen specimen)
        {
            Assert.Null(Unexpected(specimen, Write));
            Assert.Null(Unexpected(specimen, ReadReference));
        }

        /// <summary>
        /// Заявленное расхождение обязано <b>быть</b>. Иначе список
        /// осознанных расхождений тихо превращается в список исторических.
        /// </summary>
        [Fact]
        public void Every_declared_divergence_still_diverges()
        {
            foreach (var declared in Declared)
            {
                var parts = declared.Key.Split('|');
                var specimen = StjFacade.Specimens.SingleOrDefault(s => s.Name == parts[0]);

                Assert.True(specimen is not null, "в корпусе не стало типа " + parts[0]);

                Assert.False(
                    Compare(specimen!, parts[1]) is null,
                    "расхождение `" + declared.Key + "` заявлено как осознанное, но его больше нет: "
                    + declared.Value
                    );
            }
        }

        /// <summary>
        /// Таблица «проходим / не проходим» поверх фасада - приёмка фазы 8.
        /// Печатается тем же тестом, который её проверяет: рукописная
        /// устаревала бы молча.
        /// </summary>
        [Fact]
        public void Facade_report_is_written_next_to_the_assembly()
        {
            var report = new StringBuilder();
            var undeclared = new List<string>();

            var specimens = StjFacade.Specimens;
            var served = specimens.Count(s => s.IsBound);

            report.Append("# Набор тестов System.Text.Json: что проходит поверх фасада\n\n");
            report.Append("Отчёт порождается тестом `StjFacadeFixture`; править руками нечего.\n");
            report.Append("Корпус вендорен дословно, коммит зафиксирован в `Stj/PINNED.md`.\n\n");

            report.Append("Здесь проверяется не генератор, а **drop-in**. Код потребителя печатается\n");
            report.Append("таким, каким его пишет человек — `JsonSerializer.Serialize(value)`, опции по\n");
            report.Append("умолчанию, ни одного нашего имени в строке, — и подменяется только тип\n");
            report.Append("`JsonSerializer`, ровно как это делает `global using` из §10.\n\n");

            report.Append("Эталон берётся **с настройками по умолчанию**, а не с\n");
            report.Append("`UnsafeRelaxedJsonEscaping`, как в `stj-corpus-report.md`: потребитель фасада\n");
            report.Append("энкодер не выбирал, он поменял пакет, а не код.\n\n");

            report.Append("Сравниваются **исходы**. Половина чужого корпуса не обслуживается и самим\n");
            report.Append("эталоном — он отвечает на неё исключением, и требование к фасаду то же:\n");
            report.Append("отказать там же и тем же видом исключения.\n\n");

            report.Append("Из **").Append(N(specimens.Count)).Append("** типов корпуса, которые вообще можно создать, быстрый путь\n");
            report.Append("обслуживает **").Append(N(served)).Append("**, остальные **").Append(N(specimens.Count - served));
            report.Append("** уходят настоящему `System.Text.Json`.\n\n");

            report.Append("Столбец `быстрый` — не украшение: у типа, ушедшего эталону, фасад **и есть**\n");
            report.Append("эталон, и все его ячейки зелены тождественно.\n\n");

            report.Append("| Тип | быстрый | `=` | `u8` | `→` | `←` | `V` |\n");
            report.Append("|---|:-:|:-:|:-:|:-:|:-:|:-:|\n");

            foreach (var specimen in specimens)
            {
                report.Append("| `").Append(specimen.Name).Append("` | ")
                    .Append(specimen.IsBound ? "да" : "—").Append(" | ");

                report.Append(Cell(specimen, Write, undeclared)).Append(" | ");
                report.Append(Cell(specimen, WriteUtf8, undeclared)).Append(" | ");
                report.Append(Cell(specimen, ReadReference, undeclared)).Append(" | ");

                report.Append(specimen.Document is null ? "—" : Cell(specimen, ReadTheirs, undeclared)).Append(" | ");
                report.Append(specimen.Verify is null ? "—" : Cell(specimen, TheirVerify, undeclared));

                report.Append(" |\n");
            }

            report.Append('\n');
            report.Append("## Осознанные расхождения\n\n");

            foreach (var declared in Declared.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                report.Append("- `").Append(declared.Key).Append("` — ").Append(declared.Value).Append('\n');
            }

            report.Append('\n');

            if (undeclared.Count == 0)
            {
                report.Append("Незаявленных расхождений нет.\n");
            }
            else
            {
                report.Append("## Незаявленные расхождения\n\n");
                foreach (var failure in undeclared)
                {
                    report.Append("- ").Append(failure).Append('\n');
                }
            }

            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "stj-facade-report.md"),
                report.ToString(),
                new UTF8Encoding(false)
                );

            //отчёт, зелёный при красных ячейках, был бы хуже отсутствия отчёта
            Assert.Empty(undeclared);
        }

        /// <summary>
        /// Расхождение, о котором не договаривались. <c>null</c> - либо
        /// совпало, либо расхождение заявлено в <see cref="Declared"/>.
        /// </summary>
        private static string? Unexpected(StjFacadeSpecimen specimen, string direction)
        {
            var failure = Compare(specimen, direction);

            return failure is null || Declared.ContainsKey(specimen.Name + "|" + direction)
                ? null
                : failure;
        }

        /// <summary>
        /// Одно направление одного типа: исход у эталона против исхода у
        /// фасада. <c>null</c> - совпали.
        /// </summary>
        private static string? Compare(StjFacadeSpecimen specimen, string direction)
        {
            switch (direction)
            {
                case Write:
                    return Match(
                        () => JsonSerializer.Serialize(specimen.Create(), specimen.SubjectType),
                        () => specimen.Write(specimen.Create())
                        );

                case WriteUtf8:
                    return Match(
                        () => Encoding.UTF8.GetString(
                            JsonSerializer.SerializeToUtf8Bytes(specimen.Create(), specimen.SubjectType)
                            ),
                        () => Encoding.UTF8.GetString(specimen.WriteUtf8(specimen.Create()))
                        );

                case ReadReference:
                    return Match(
                        () => Reserialize(
                            JsonSerializer.Deserialize(ReferenceDocument(specimen), specimen.SubjectType),
                            specimen
                            ),
                        () => Reserialize(specimen.ReadText(ReferenceDocument(specimen)), specimen)
                        );

                case ReadTheirs:
                    return Match(
                        () => Reserialize(
                            JsonSerializer.Deserialize(specimen.Document!, specimen.SubjectType),
                            specimen
                            ),
                        () => Reserialize(specimen.ReadUtf8(specimen.Document!), specimen)
                        );

                case TheirVerify:
                    return Match(
                        () => Verified(JsonSerializer.Deserialize(TheirDocument(specimen), specimen.SubjectType), specimen),
                        () => Verified(specimen.ReadUtf8(TheirDocument(specimen)), specimen)
                        );

                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, "неизвестное направление");
            }
        }

        /// <summary>
        /// Документ эталона - им проверяется чтение отдельно от записи.
        /// Бросает ровно так же, как бросил бы у потребителя: тип, который
        /// эталон не умеет писать, не умеет и фасад, и это совпадение исходов,
        /// а не пропуск.
        /// </summary>
        private static string ReferenceDocument(StjFacadeSpecimen specimen) =>
            JsonSerializer.Serialize(specimen.Create(), specimen.SubjectType);

        private static byte[] TheirDocument(StjFacadeSpecimen specimen) =>
            specimen.Document ?? Encoding.UTF8.GetBytes(ReferenceDocument(specimen));

        private static string Verified(object? value, StjFacadeSpecimen specimen)
        {
            Assert.NotNull(value);
            specimen.Verify!(value!);
            return "проверено";
        }

        /// <summary>
        /// Исходы совпали?
        ///
        /// <para>
        /// У успеха сравнивается значение, у отказа - <b>вид</b> исключения, а
        /// не текст. Текст причины у нас свой и совпадать не обязан
        /// (docs/stj-divergences.md); требовать совпадения слов значило бы
        /// красить тест за то, что мы объясняем понятнее.
        /// </para>
        /// </summary>
        private static string? Match(Func<string> reference, Func<string> facade)
        {
            var theirs = Outcome(reference);
            var ours = Outcome(facade);

            if (theirs.Failure is null && ours.Failure is null)
            {
                return string.Equals(theirs.Value, ours.Value, StringComparison.Ordinal)
                    ? null
                    : "ожидалось `" + Trim(theirs.Value!) + "`, получено `" + Trim(ours.Value!) + "`";
            }

            if (theirs.Failure is not null && ours.Failure is not null)
            {
                return string.Equals(theirs.Failure, ours.Failure, StringComparison.Ordinal)
                    ? null
                    : "эталон отказал " + theirs.Failure + " (" + Trim(theirs.Message!) + "), фасад - "
                        + ours.Failure + " (" + Trim(ours.Message!) + ")";
            }

            return theirs.Failure is null
                ? "эталон отдал `" + Trim(theirs.Value!) + "`, фасад отказал " + ours.Failure
                    + " (" + Trim(ours.Message!) + ")"
                : "эталон отказал " + theirs.Failure + " (" + Trim(theirs.Message!) + "), фасад отдал `"
                    + Trim(ours.Value!) + "`";
        }

        private readonly struct Result
        {
            public Result(string? value, string? failure, string? message)
            {
                Value = value;
                Failure = failure;
                Message = message;
            }

            public string? Value { get; }

            public string? Failure { get; }

            public string? Message { get; }
        }

        private static Result Outcome(Func<string> action)
        {
            try
            {
                return new Result(action(), null, null);
            }
            catch (Exception exception)
            {
                var root = exception.GetBaseException();
                return new Result(null, root.GetType().Name, root.Message);
            }
        }

        private static string Cell(StjFacadeSpecimen specimen, string direction, List<string> undeclared)
        {
            var failure = Compare(specimen, direction);
            var key = specimen.Name + "|" + direction;

            if (failure is null)
            {
                //заявленное расхождение, которого не стало, ловит отдельный
                //тест: здесь ячейка просто зелена
                return "да";
            }

            if (Declared.ContainsKey(key))
            {
                return "*осознанно*";
            }

            undeclared.Add(
                "`" + specimen.Name + "`" + (specimen.IsBound ? "" : " (эталонный путь)")
                + ", направление `" + direction + "`: " + failure
                );

            return "**нет**";
        }

        private static string Reserialize(object? value, StjFacadeSpecimen specimen) =>
            JsonSerializer.Serialize(value, specimen.SubjectType);

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Trim(string value) =>
            value.Length <= 160 ? value : value.Substring(0, 160) + "…";
    }
}
