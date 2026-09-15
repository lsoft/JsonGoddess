using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Маршрут A из §11.1: их POCO и их документы через наш генератор.
    ///
    /// Смысл ровно в том, что набор чужой. Свой всегда описывает то, что автор
    /// подумал проверить; этот описывает то, что ломалось на самом деле -
    /// десять лет багрепортов на формат, который держат в продакшене. Первый
    /// же прогон это подтвердил: их <c>StringListWrapper : List&lt;string&gt;
    /// { }</c> мы принимали и писали <c>{}</c> вместо
    /// <c>["Hello","World"]</c> - молча, валидным кодом, с другим документом.
    ///
    /// Приговор каждому типу <b>снимается прогоном</b>. Рукописная таблица
    /// совместимости неизбежно врёт - не со зла, а потому что устаревает
    /// молча; эта устареть не может, потому что её печатает тот же тест,
    /// который её проверяет.
    ///
    /// Один таргет (net10.0) здесь - ограничение, а не решение: проект
    /// генераторных тестов однотаргетный, потому что текст и диагностики от
    /// рантайма не зависят. Исполнение зависит, и <c>#else</c>-ветки
    /// netstandard2.0 этим переносом не покрываются - их держит
    /// <c>JsonGoddess.Tests</c> на трёх таргетах.
    /// </summary>
    public class StjCorpusFixture
    {
        /// <summary>
        /// Опции, в которых набор экранируемого совпадает с нашим - минимум
        /// RFC 8259 §7. Энкодер эталона по умолчанию дополнительно
        /// разворачивает не-ASCII; это осознанное расхождение, закреплённое
        /// в <c>JsonGoddess.Tests</c>, и прятать его здесь было бы нечестно.
        /// </summary>
        private static readonly JsonSerializerOptions _relaxed = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static IEnumerable<object[]> Specimens =>
            StjExecution.Specimens.Select(s => new object[] { s, });

        public static IEnumerable<object[]> SpecimensWithDocument =>
            StjExecution.Specimens.Where(s => s.Document is not null).Select(s => new object[] { s, });

        public static IEnumerable<object[]> SpecimensWithVerify =>
            StjExecution.Specimens.Where(s => s.Verify is not null).Select(s => new object[] { s, });

        /// <summary>
        /// Код, напечатанный по чужим типам, <b>собирается</b>. Утверждение
        /// сильнее любого сравнения текста, и дешевле оно здесь не бывает:
        /// корпус и порождённое по нему компилируются вместе, одной
        /// компиляцией.
        /// </summary>
        [Fact]
        public void Generated_code_for_the_whole_corpus_compiles()
        {
            Assert.Empty(StjExecution.CompilationErrors.Select(e => e.ToString()));
        }

        /// <summary>
        /// Ни один их тип не роняет генератор. Отказ - это диагностика, а не
        /// исключение: сто двадцать восемь чужих типов, половина которых
        /// устроена неожиданно, - лучшая проверка на то, что отказ везде
        /// доведён до конца.
        /// </summary>
        [Fact]
        public void Every_type_ends_in_a_verdict_and_never_in_a_crash()
        {
            Assert.Equal(StjCatalogue.Candidates.Count, StjCatalogue.Verdicts.Count);

            foreach (var verdict in StjCatalogue.Verdicts.Where(v => !v.Accepted))
            {
                Assert.StartsWith("JGD", verdict.DiagnosticId);
                Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
            }
        }

        [Theory]
        [MemberData(nameof(Specimens))]
        public void Their_object_gives_the_document_they_give(StjSpecimen specimen)
        {
            var value = specimen.Create();

            Assert.Equal(
                JsonSerializer.Serialize(value, specimen.SubjectType, _relaxed),
                Encoding.UTF8.GetString(specimen.Write(value))
                );
        }

        /// <summary>
        /// Читаем мы, сравнивает эталон. Сравнивать прочитанное с исходным
        /// значило бы позволить симметричной ошибке писателя и читателя
        /// погасить друг друга.
        /// </summary>
        [Theory]
        [MemberData(nameof(Specimens))]
        public void We_read_our_own_document_the_way_they_read_it(StjSpecimen specimen)
        {
            var document = specimen.Write(specimen.Create());

            Assert.Equal(
                JsonSerializer.Serialize(
                    JsonSerializer.Deserialize(document, specimen.SubjectType, _relaxed),
                    specimen.SubjectType,
                    _relaxed
                    ),
                JsonSerializer.Serialize(specimen.Read(document), specimen.SubjectType, _relaxed)
                );
        }

        /// <summary>
        /// <b>Их</b> документ - тот самый, что накоплен багрепортами, - читаем
        /// мы. Форматирование в нём чужое: пробелы вокруг двоеточий, иной
        /// порядок членов, комментарии. Из наших направлений это не следует
        /// ни из чего, потому что свой документ мы пишем сами и потому знаем
        /// его наперёд.
        /// </summary>
        [Theory]
        [MemberData(nameof(SpecimensWithDocument))]
        public void We_read_the_document_from_their_own_test(StjSpecimen specimen)
        {
            Assert.Equal(
                JsonSerializer.Serialize(
                    JsonSerializer.Deserialize(specimen.Document!, specimen.SubjectType, _relaxed),
                    specimen.SubjectType,
                    _relaxed
                    ),
                JsonSerializer.Serialize(specimen.Read(specimen.Document!), specimen.SubjectType, _relaxed)
                );
        }

        /// <summary>
        /// Самая сильная ячейка во всём переносе: утверждение об объекте пишет
        /// не наш тест, а их. <c>ITestClass.Verify()</c> применяется к тому,
        /// что вернул наш читатель, - из их документа, если он есть, иначе из
        /// нашего.
        /// </summary>
        [Theory]
        [MemberData(nameof(SpecimensWithVerify))]
        public void Their_own_assertions_hold_on_what_we_read(StjSpecimen specimen)
        {
            var document = specimen.Document ?? specimen.Write(specimen.Create());

            var value = specimen.Read(document);

            Assert.NotNull(value);
            specimen.Verify!(value!);
        }

        /// <summary>
        /// Нижняя граница - чтобы перенос нельзя было обесточить, вычеркнув
        /// половину корпуса. Число не круглое: оно ровно такое, каким его
        /// сделал прогон, и растёт вместе с охватом фаз.
        /// </summary>
        [Fact]
        public void Corpus_and_coverage_do_not_shrink_silently()
        {
            Assert.True(
                StjCatalogue.Candidates.Count >= 128,
                "корпус усох до " + StjCatalogue.Candidates.Count + " типов"
                );

            Assert.True(
                StjExecution.Specimens.Count >= 21,
                "принятых типов стало " + StjExecution.Specimens.Count + ", было 21"
                );

            Assert.True(
                StjExecution.Specimens.Count(s => s.Document is not null) >= 8,
                "их документов стало " + StjExecution.Specimens.Count(s => s.Document is not null) + ", было 8"
                );
        }

        /// <summary>
        /// Таблица совместимости для README: не «мы поддерживаем почти всё», а
        /// «из N их типов мы обслуживаем M, отказываем K, вот они и вот
        /// почему».
        /// </summary>
        [Fact]
        public void Compatibility_report_is_written_next_to_the_assembly()
        {
            var report = new StringBuilder();
            var failures = new List<string>();

            var candidates = StjCatalogue.Candidates.Count;
            var accepted = StjCatalogue.Verdicts.Count(v => v.Accepted);

            report.Append("# Набор тестов System.Text.Json: что из него проходит\n\n");
            report.Append("Отчёт порождается тестом `StjCorpusFixture`; править руками нечего.\n");
            report.Append("Корпус вендорен дословно, коммит зафиксирован в `Stj/PINNED.md`.\n\n");

            report.Append("Из **").Append(N(candidates)).Append("** их типов генератор обслуживает **")
                .Append(N(accepted)).Append("**, отказывает **").Append(N(candidates - accepted)).Append("**.\n\n");

            report.Append("Отказ - это диагностика с причиной, а не пропуск: молча выданный валидный код,\n");
            report.Append("дающий другой документ, - худший из исходов. Список отказов ниже и есть честный\n");
            report.Append("перечень того, чего мы пока не умеем.\n\n");

            report.Append("## Проходит\n\n");
            report.Append("Столбцы: `=` — их объект даёт их документ байт в байт; `→` — свой документ мы\n");
            report.Append("читаем так же, как его читают они; `←` — их документ из их же теста читаем мы;\n");
            report.Append("`V` — их собственный `ITestClass.Verify()` на том, что вернул наш читатель.\n\n");

            report.Append("| Тип | Типов в замыкании | `=` | `→` | `←` | `V` |\n");
            report.Append("|---|:-:|:-:|:-:|:-:|:-:|\n");

            foreach (var specimen in StjExecution.Specimens)
            {
                var verdict = StjCatalogue.Verdicts.Single(v => v.Name == specimen.Name);

                report.Append("| `").Append(specimen.Name).Append("` | ")
                    .Append(N(verdict.ClosureSize)).Append(" | ");

                report.Append(Cell(specimen.Name, "=", failures, () =>
                {
                    var value = specimen.Create();
                    return Same(
                        JsonSerializer.Serialize(value, specimen.SubjectType, _relaxed),
                        Encoding.UTF8.GetString(specimen.Write(value))
                        );
                })).Append(" | ");

                report.Append(Cell(specimen.Name, "→", failures, () =>
                {
                    var document = specimen.Write(specimen.Create());
                    return Same(
                        Reserialize(JsonSerializer.Deserialize(document, specimen.SubjectType, _relaxed), specimen),
                        Reserialize(specimen.Read(document), specimen)
                        );
                })).Append(" | ");

                report.Append(specimen.Document is null
                    ? "—"
                    : Cell(specimen.Name, "←", failures, () => Same(
                        Reserialize(JsonSerializer.Deserialize(specimen.Document!, specimen.SubjectType, _relaxed), specimen),
                        Reserialize(specimen.Read(specimen.Document!), specimen)
                        ))).Append(" | ");

                report.Append(specimen.Verify is null
                    ? "—"
                    : Cell(specimen.Name, "V", failures, () =>
                    {
                        var document = specimen.Document ?? specimen.Write(specimen.Create());
                        specimen.Verify!(specimen.Read(document)!);
                        return null;
                    }));

                report.Append(" |\n");
            }

            report.Append('\n');
            report.Append("## Не проходит\n\n");

            foreach (var group in StjCatalogue.Verdicts
                .Where(v => !v.Accepted)
                .GroupBy(v => v.DiagnosticId)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                report.Append("### ").Append(group.Key).Append(" — отказов: ").Append(N(group.Count())).Append("\n\n");

                foreach (var reason in group
                    .GroupBy(v => Tail(v.Reason!))
                    .OrderByDescending(g => g.Count()))
                {
                    report.Append("**").Append(reason.Key).Append("**\n\n");
                    report.Append(string.Join(", ", reason.Select(Entry).OrderBy(n => n, StringComparer.Ordinal)));
                    report.Append("\n\n");
                }
            }

            AppendBlockers(report);

            if (failures.Count == 0)
            {
                report.Append("Незапланированных расхождений нет.\n");
            }
            else
            {
                report.Append("## Незапланированные расхождения\n\n");
                foreach (var failure in failures)
                {
                    report.Append("- ").Append(failure).Append('\n');
                }
            }

            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "stj-corpus-report.md"),
                report.ToString(),
                new UTF8Encoding(false)
                );

            //отчёт, зелёный при красных ячейках, был бы хуже отсутствия отчёта
            Assert.Empty(failures);
        }

        /// <summary>
        /// Кто мешает чаще всех — и, отдельно, кого снятие причины
        /// действительно откроет.
        ///
        /// Два столбца, а не один, и это исправление ошибки, которая уже успела
        /// соврать. По первой причине выходило, что <c>SimpleStruct</c> держит
        /// двадцать типов; поддержка структур открыла <b>один</b>. Остальные
        /// девятнадцать просто назвали следующую свою причину — у них их было
        /// по нескольку с самого начала.
        ///
        /// Поэтому второй столбец считает типы, у которых эта причина
        /// <b>единственная</b>. Только он и отвечает на вопрос «что даст
        /// работа», и только на него можно опираться, назначая порядок фаз.
        /// </summary>
        private static void AppendBlockers(StringBuilder report)
        {
            var refused = StjCatalogue.Verdicts.Where(v => !v.Accepted).ToList();

            var mentioned = new Dictionary<string, int>(StringComparer.Ordinal);
            var soleReason = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var verdict in refused)
            {
                //группировка по ПРИЧИНЕ, а не по виноватому типу: работа
                //снимает правило, а не конкретный чужой класс, и планировать
                //надо тем же, чем работаешь
                var kinds = verdict.Reasons
                    .Select(r => Kind(Tail(r)))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var kind in kinds)
                {
                    mentioned.TryGetValue(kind, out var count);
                    mentioned[kind] = count + 1;

                    if (kinds.Count == 1)
                    {
                        soleReason.TryGetValue(kind, out var sole);
                        soleReason[kind] = sole + 1;
                    }
                }
            }

            var blockers = mentioned
                .OrderByDescending(p => soleReason.TryGetValue(p.Key, out var s) ? s : 0)
                .ThenByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .Take(10)
                .ToList();

            if (blockers.Count == 0)
            {
                return;
            }

            report.Append("## Что даст следующая работа\n\n");
            report.Append("У типа причин бывает несколько, и снятие одной открывает не тип, а следующую\n");
            report.Append("причину. Поэтому столбца два. **Упоминается** — во скольких отказах причина\n");
            report.Append("названа вообще. **Единственная** — у скольких типов она одна, то есть сколько\n");
            report.Append("откроется, если её снять.\n\n");
            report.Append("Планировать можно только по второму. По первому уже вышла ошибка: отказ на\n");
            report.Append("структурах упоминался у двадцати типов, поддержка структур открыла **один** —\n");
            report.Append("остальные девятнадцать просто назвали следующую свою причину.\n\n");

            report.Append("| Причина | Упоминается | Единственная |\n|---|:-:|:-:|\n");

            foreach (var blocker in blockers)
            {
                report.Append("| `").Append(blocker.Key).Append("` | ").Append(N(blocker.Value)).Append(" | ")
                    .Append(N(soleReason.TryGetValue(blocker.Key, out var sole) ? sole : 0)).Append(" |\n");
            }

            report.Append('\n');
        }

        private static string Reserialize(object? value, StjSpecimen specimen) =>
            JsonSerializer.Serialize(value, specimen.SubjectType, _relaxed);

        private static string? Same(string expected, string produced) =>
            string.Equals(expected, produced, StringComparison.Ordinal)
                ? null
                : "ожидалось `" + Trim(expected) + "`, получено `" + Trim(produced) + "`";

        private static string Cell(string name, string direction, List<string> failures, Func<string?> check)
        {
            string? failure;

            try
            {
                failure = check();
            }
            catch (Exception exception)
            {
                failure = exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message;
            }

            if (failure is null)
            {
                return "да";
            }

            failures.Add("`" + name + "`, направление `" + direction + "`: " + failure);
            return "**нет**";
        }

        /// <summary>
        /// Причина отказа без имени конкретного типа: сообщение начинается с
        /// «Member 'X.Y' has type 'Z'», и группировать по нему целиком значило
        /// бы получить группу на каждый тип.
        /// </summary>
        private static string Tail(string reason)
        {
            var separator = reason.IndexOf(": ", StringComparison.Ordinal);
            return separator < 0 ? reason : reason.Substring(separator + 2);
        }

        /// <summary>
        /// Причина без имени типа внутри неё.
        ///
        /// «Тип не зарегистрирован» с именем в тексте - это одно правило, а не
        /// сорок; в таблице приоритетов оно обязано стоять одной строкой,
        /// иначе самое массовое выглядит самым мелким.
        /// </summary>
        private static string Kind(string reason)
        {
            const string NotRegistered = "the type is not registered";

            return reason.StartsWith(NotRegistered, StringComparison.Ordinal)
                ? NotRegistered + " (member type JsonGoddess cannot serve)"
                : reason;
        }

        /// <summary>
        /// Тип в таблице отказов - и, если отказ пришёлся не на него самого, то
        /// на кого именно.
        ///
        /// Без этого таблица врала бы в самом заметном месте: <c>SimpleTestClass</c>
        /// отказан из-за <c>SimpleStruct</c> среди его членов, и строка «только
        /// классы поддержаны» рядом с его именем читалась бы как «SimpleTestClass
        /// - структура».
        /// </summary>
        private static string Entry(StjVerdict verdict)
        {
            var culprit = Culprit(verdict);

            return culprit is null || culprit == verdict.Name
                ? "`" + verdict.Name + "`"
                : "`" + verdict.Name + "` (из-за `" + culprit + "`)";
        }

        /// <summary>
        /// Первое, что стоит в сообщении в одинарных кавычках: у <c>JGD021</c>
        /// это отказанный тип, у <c>JGD022</c> - член вида <c>Тип.Член</c>.
        /// Разбор своего же формата, а не чужого, - он наш и стабилен.
        /// </summary>
        private static string? Culprit(StjVerdict verdict) =>
            verdict.Reason is null ? null : CulpritOf(verdict.DiagnosticId + " " + verdict.Reason);

        /// <summary>
        /// То же по строке вида <c>«JGD0NN сообщение»</c>: так хранится каждая
        /// причина в <see cref="StjVerdict.Reasons"/>.
        /// </summary>
        private static string? CulpritOf(string? reason)
        {
            if (reason is null)
            {
                return null;
            }

            var isMember = reason.StartsWith("JGD022", StringComparison.Ordinal);

            var open = reason.IndexOf('\'');
            var close = open < 0 ? -1 : reason.IndexOf('\'', open + 1);
            if (close < 0)
            {
                return null;
            }

            var quoted = reason.Substring(open + 1, close - open - 1);

            if (isMember)
            {
                var member = quoted.LastIndexOf('.');
                quoted = member < 0 ? quoted : quoted.Substring(0, member);
            }

            var dot = quoted.LastIndexOf('.');
            return dot < 0 ? quoted : quoted.Substring(dot + 1);
        }

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Trim(string value) =>
            value.Length <= 160 ? value : value.Substring(0, 160) + "…";
    }
}
