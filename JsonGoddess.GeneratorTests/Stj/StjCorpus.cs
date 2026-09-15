using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JsonGoddess.GeneratorTests.Harness;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Вендоренный корпус тестов <c>System.Text.Json</c> - маршрут A из §11.1
    /// плана.
    ///
    /// Заявка «то же богатство функций, только быстрее» проверяется не нашими
    /// тестами, а <b>их</b>. Свой набор всегда описывает то, что автор подумал
    /// проверить; чужой, писанный командой, которая держит формат в продакшене
    /// десять лет, описывает то, что ломается на самом деле.
    ///
    /// Корпус лежит в <c>corpus/</c> дословными копиями и подаётся генератору
    /// <b>текстом</b>: в сборку тестов он не компилируется, потому что половина
    /// его типов существует ровно затем, чтобы генератор от них отказался.
    /// Коммит зафиксирован в <c>PINNED.md</c>, обновление - через
    /// <c>eng/sync-stj-tests.ps1</c>.
    /// </summary>
    public static class StjCorpus
    {
        /// <summary>
        /// Пространство имён корпуса. Наши хосты печатаются в него же: иначе
        /// пришлось бы либо писать <c>using</c>, либо квалифицировать каждое
        /// имя типа, а имена в корпусе встречаются вложенные.
        /// </summary>
        public const string Namespace = "System.Text.Json.Serialization.Tests";

        private static readonly Lazy<IReadOnlyList<SourceFile>> _files =
            new Lazy<IReadOnlyList<SourceFile>>(Load);

        public static IReadOnlyList<SourceFile> Files => _files.Value;

        public static string Directory =>
            Path.Combine(AppContext.BaseDirectory, "Stj", "corpus");

        private static IReadOnlyList<SourceFile> Load()
        {
            var directory = Directory;

            if (!System.IO.Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    "Корпус System.Text.Json не доехал до каталога сборки: '" + directory + "' не существует. "
                    + "Ожидались файлы, положенные туда как None+CopyToOutputDirectory из Stj/corpus."
                    );
            }

            var files = System.IO.Directory
                .GetFiles(directory, "*.cs")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new SourceFile(Path.GetFileName(p), File.ReadAllText(p)))
                .ToList();

            if (files.Count == 0)
            {
                throw new InvalidOperationException(
                    "Каталог корпуса '" + directory + "' пуст. Пустой корпус дал бы зелёную таблицу "
                    + "совместимости, не проверив ничего, - это хуже отсутствия таблицы."
                    );
            }

            return files;
        }
    }
}
