using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonGoddess.GeneratorTests.Harness
{
    public sealed class SourceFile
    {
        public string Path { get; }

        public string Text { get; }

        public SourceFile(string path, string text)
        {
            Path = path;
            Text = text;
        }
    }

    public sealed class GeneratorRun
    {
        public required GeneratorDriver Driver { get; init; }

        public required Compilation OutputCompilation { get; init; }

        /// <summary>Диагностики самого генератора.</summary>
        public required ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

        /// <summary>Файлы, которые генератор добавил: имя - текст.</summary>
        public required IReadOnlyDictionary<string, string> GeneratedFiles { get; init; }

        public required GeneratorDriverRunResult RunResult { get; init; }

        /// <summary>
        /// Ошибки итоговой компиляции. Пустота здесь - более сильное
        /// утверждение, чем любое сравнение текста: она означает, что
        /// порождённый код действительно компилируется вместе с исходным.
        /// </summary>
        public ImmutableArray<Diagnostic> CompilationErrors =>
            OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();

        public string SingleGeneratedFile => GeneratedFiles.Values.Single();

        public IEnumerable<string> DiagnosticIds => GeneratorDiagnostics.Select(d => d.Id);
    }

    /// <summary>
    /// Прогон генератора через <see cref="CSharpGeneratorDriver"/>.
    ///
    /// Ссылки берутся из списка доверенных сборок текущего процесса: тесты
    /// работают на том же рантайме, что и проверяемый код, поэтому и
    /// <c>System.Text.Json</c>, и сама JsonGoddess там уже есть, и городить
    /// ручной список ссылок не нужно.
    /// </summary>
    public static class GeneratorHarness
    {
        private static readonly Lazy<ImmutableArray<MetadataReference>> _references =
            new Lazy<ImmutableArray<MetadataReference>>(BuildReferences);

        public static GeneratorRun Run(
            string source,
            LanguageVersion languageVersion = LanguageVersion.CSharp11
            )
        {
            return Run(new[] { new SourceFile("Subject.cs", source), }, languageVersion);
        }

        public static GeneratorRun Run(
            IReadOnlyList<SourceFile> files,
            LanguageVersion languageVersion = LanguageVersion.CSharp11
            )
        {
            var compilation = CreateCompilation(files, languageVersion);
            var driver = CreateDriver(languageVersion);

            return Continue(driver, compilation);
        }

        public static GeneratorRun Continue(GeneratorDriver driver, Compilation compilation)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var diagnostics
                );

            var runResult = driver.GetRunResult();

            var generated = runResult.Results
                .SelectMany(r => r.GeneratedSources)
                .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

            return new GeneratorRun
            {
                Driver = driver,
                OutputCompilation = outputCompilation,
                GeneratorDiagnostics = diagnostics,
                GeneratedFiles = generated,
                RunResult = runResult,
            };
        }

        public static GeneratorDriver CreateDriver(LanguageVersion languageVersion = LanguageVersion.CSharp11)
        {
            return CSharpGeneratorDriver.Create(
                new[] { new global::JsonGoddess.Generator.JsonGoddessGenerator().AsSourceGenerator(), },
                parseOptions: new CSharpParseOptions(languageVersion),
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true
                    )
                );
        }

        public static CSharpCompilation CreateCompilation(
            IReadOnlyList<SourceFile> files,
            LanguageVersion languageVersion = LanguageVersion.CSharp11
            )
        {
            var parseOptions = new CSharpParseOptions(languageVersion);

            var trees = files
                .Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, path: f.Path))
                .ToArray();

            return CSharpCompilation.Create(
                "JsonGoddess.GeneratorTests.Subject",
                trees,
                _references.Value,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                    )
                );
        }

        private static ImmutableArray<MetadataReference> BuildReferences()
        {
            var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

            var result = ImmutableArray.CreateBuilder<MetadataReference>();
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length > 0 && Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(MetadataReference.CreateFromFile(path));
                }
            }

            return result.ToImmutable();
        }
    }
}
