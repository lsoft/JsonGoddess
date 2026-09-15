using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace JsonGoddess.Tests.Interop
{
    /// <summary>
    /// Дифференциальный харнесс против <c>System.Text.Json</c> - центральный
    /// механизм качества (§11 плана).
    ///
    /// Направлений четыре, и они не равносильны, что стоит сказать прямо:
    /// <list type="number">
    /// <item><b>оба пишут, документы сравниваются</b> - самое сильное
    /// утверждение, и пока оно зелёное, направление 2 из него следует;</item>
    /// <item><b>наш документ читает эталон</b> - оно держится отдельно не ради
    /// счёта: если мы когда-нибудь сознательно начнём писать другой, но
    /// равносильный документ, направление 1 покраснеет по замыслу, а это
    /// обязано остаться зелёным;</item>
    /// <item><b>документ эталона читаем мы</b> - обратная сторона, и она уже не
    /// следует ни из чего;</item>
    /// <item><b>документ эталона в режиме по умолчанию читаем мы</b> -
    /// направление, в котором живёт настоящее расхождение: его энкодер
    /// разворачивает в <c>\uXXXX</c> весь не-ASCII, включая имена свойств и
    /// ключи словарей, и не уметь это читать значило бы иметь одностороннюю
    /// совместимость.</item>
    /// </list>
    ///
    /// Ожидание не записано литералом нигде: во всех четырёх направлениях
    /// эталоном служит документ, который <c>System.Text.Json</c> напишет прямо
    /// в тесте.
    /// </summary>
    public class InteropFixture
    {
        public static IEnumerable<object[]> Forms =>
            Interop.Forms.All.Select(f => new object[] { f, });

        /// <summary>
        /// Форма, у которой расхождение не объявлено, обязана совпасть байт в
        /// байт. Форма, у которой объявлено, обязана <b>разойтись</b>: отмена
        /// осознанного решения не должна проходить молча.
        /// </summary>
        [Theory]
        [MemberData(nameof(Forms))]
        public void Both_write_the_same_document_unless_a_divergence_is_declared(InteropForm form)
        {
            if (form.Divergence is null)
            {
                Assert.Equal(form.WriteTheirs(), form.WriteOurs());
                return;
            }

            Assert.False(
                string.Equals(form.WriteTheirs(), form.WriteOurs(), StringComparison.Ordinal),
                "form '" + form.Name + "' declares a divergence (" + form.Divergence
                + ") but the documents are identical; remove the declaration"
                );
        }

        [Theory]
        [MemberData(nameof(Forms))]
        public void System_text_json_understands_our_document(InteropForm form)
        {
            Assert.Equal(form.WriteTheirs(), form.TheirsReadsThenWrites(form.WriteOurs()));
        }

        [Theory]
        [MemberData(nameof(Forms))]
        public void We_understand_the_document_of_system_text_json(InteropForm form)
        {
            Assert.Equal(form.WriteTheirs(), form.OursReadsThenTheirsWrites(form.WriteTheirs()));
        }

        [Theory]
        [MemberData(nameof(Forms))]
        public void We_understand_it_with_their_default_encoder_too(InteropForm form)
        {
            Assert.Equal(form.WriteTheirs(), form.OursReadsThenTheirsWrites(form.WriteTheirsEscaped()));
        }

        /// <summary>
        /// Приёмка фазы 4 - число, а не ощущение. Цель фазы 5 - сорок форм, и
        /// нижняя граница здесь стои́т затем, чтобы харнесс нельзя было
        /// незаметно обесточить, удалив половину каталога.
        /// </summary>
        [Fact]
        public void Harness_covers_at_least_twenty_five_forms()
        {
            Assert.True(
                Interop.Forms.All.Count >= 25,
                "the differential harness must cover at least 25 forms, it covers " + Interop.Forms.All.Count
                );

            //имена форм - ключи в отчёте, и совпадать они не должны
            Assert.Equal(
                Interop.Forms.All.Count,
                Interop.Forms.All.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count()
                );
        }

        /// <summary>
        /// Таблица совместимости рядом со сборкой тестов (§11). Она не заменяет
        /// проверок - те выше, - а отвечает на вопрос, на который набор
        /// зелёных галочек не отвечает: <b>что именно</b> покрыто и в каком
        /// направлении.
        /// </summary>
        [Fact]
        public void Compatibility_report_is_written_next_to_the_assembly()
        {
            var report = new StringBuilder();

            report.Append("# Совместимость с System.Text.Json\n\n");
            report.Append("Отчёт порождается тестом `InteropFixture`; править руками нечего.\n");
            report.Append("Ожидание во всех направлениях берётся прогоном `System.Text.Json` внутри теста,\n");
            report.Append("а не записывается литералом.\n\n");

            report.Append("Форм: ").Append(Interop.Forms.All.Count.ToString(CultureInfo.InvariantCulture));
            report.Append(". Форма - это пара «тип + образец»: пустая коллекция и заполненная дают\n");
            report.Append("документы разного строения при одном и том же POCO.\n\n");

            report.Append("Направления:\n\n");
            report.Append("1. `=` — оба пишут, документы сравниваются байт в байт;\n");
            report.Append("2. `→` — наш документ читает эталон;\n");
            report.Append("3. `←` — документ эталона читаем мы;\n");
            report.Append("4. `←\\u` — то же, но эталон писал энкодером по умолчанию (весь не-ASCII в `\\uXXXX`).\n\n");

            report.Append("В столбце `=` значение `иначе` означает <b>осознанное</b> расхождение:\n");
            report.Append("документы различаются по решению, и харнесс требует, чтобы они различались.\n");
            report.Append("Причина названа под таблицей.\n\n");

            report.Append("| Форма | Что проверяет | `=` | `→` | `←` | `←\\u` |\n");
            report.Append("|---|---|:-:|:-:|:-:|:-:|\n");

            var failures = new List<string>();
            var divergences = new List<string>();

            foreach (var form in Interop.Forms.All)
            {
                var theirs = Run(() => form.WriteTheirs());

                report.Append("| `").Append(form.Name).Append("` | ").Append(form.What).Append(" | ");

                if (form.Divergence is null)
                {
                    report.Append(Cell(form.Name, "=", failures, theirs, () => form.WriteOurs()));
                }
                else
                {
                    report.Append(Diverged(form, failures));
                    divergences.Add("`" + form.Name + "` — " + form.Divergence);
                }

                report.Append(" | ");
                report.Append(Cell(form.Name, "→", failures, theirs, () => form.TheirsReadsThenWrites(form.WriteOurs()))).Append(" | ");
                report.Append(Cell(form.Name, "←", failures, theirs, () => form.OursReadsThenTheirsWrites(form.WriteTheirs()))).Append(" | ");
                report.Append(Cell(form.Name, "←\\u", failures, theirs, () => form.OursReadsThenTheirsWrites(form.WriteTheirsEscaped())));
                report.Append(" |\n");
            }

            report.Append('\n');

            if (divergences.Count > 0)
            {
                report.Append("## Осознанные расхождения\n\n");
                foreach (var divergence in divergences)
                {
                    report.Append("- ").Append(divergence).Append('\n');
                }

                report.Append('\n');
            }

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

            var path = Path.Combine(AppContext.BaseDirectory, "interop-report.md");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));

            //отчёт, который пишется зелёным при красных ячейках, был бы хуже,
            //чем отсутствие отчёта
            Assert.Empty(failures);
        }

        private static string Diverged(InteropForm form, List<string> failures)
        {
            var theirs = Run(form.WriteTheirs);
            var ours = Run(form.WriteOurs);

            if (!string.Equals(theirs, ours, StringComparison.Ordinal))
            {
                return "иначе";
            }

            failures.Add("`" + form.Name + "`: расхождение объявлено (" + form.Divergence
                + "), но документы совпали - объявление пора снять");
            return "**совпало**";
        }

        private static string Cell(
            string form,
            string direction,
            List<string> failures,
            string expected,
            Func<string> actual
            )
        {
            var produced = Run(actual);

            if (string.Equals(expected, produced, StringComparison.Ordinal))
            {
                return "да";
            }

            failures.Add("`" + form + "`, направление `" + direction + "`: ожидалось `"
                + Trim(expected) + "`, получено `" + Trim(produced) + "`");
            return "**нет**";
        }

        private static string Run(Func<string> action)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
        }

        private static string Trim(string value)
        {
            return value.Length <= 200 ? value : value.Substring(0, 200) + "…";
        }
    }
}
