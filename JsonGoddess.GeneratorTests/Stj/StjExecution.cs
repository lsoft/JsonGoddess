using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Один тип корпуса, доведённый до исполнения: не «генератор не отказал», а
    /// «их объект прошёл через наш код».
    ///
    /// Между этими утверждениями лежит почти всё, что может сломаться, поэтому
    /// таблица совместимости, построенная на одних отказах, была бы обещанием,
    /// а не проверкой.
    /// </summary>
    public sealed class StjSpecimen
    {
        public required string Name { get; init; }

        public required Type SubjectType { get; init; }

        /// <summary>Их образец: <c>ITestClass.Initialize()</c>, если он у типа есть.</summary>
        public required Func<object> Create { get; init; }

        public required Func<object, byte[]> Write { get; init; }

        public required Func<byte[], object?> Read { get; init; }

        /// <summary>
        /// Их документ - <c>s_json</c>/<c>s_data</c>, накопленный
        /// багрепортами, - или <c>null</c>, если у типа его нет.
        /// </summary>
        public required byte[]? Document { get; init; }

        /// <summary>
        /// Их собственная проверка прочитанного. Самая сильная ячейка во всём
        /// переносе: утверждение об объекте пишет не наш тест, а их.
        /// </summary>
        public required Action<object>? Verify { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Сборка корпуса вместе с порождённым кодом - и её исполнение.
    ///
    /// Корпус компилируется <b>в память и загружается</b>, а не подключается
    /// исходниками к проекту тестов: половина его типов существует ровно затем,
    /// чтобы генератор от них отказался, а отказ генератора - это ошибка
    /// компиляции, которая снесла бы сборку тестов целиком.
    ///
    /// Вызывать порождённые методы рефлексией напрямую нельзя: у читателя
    /// параметром стоит <c>ReadOnlySpan&lt;byte&gt;</c>, а ByRefLike через
    /// <c>MethodInfo.Invoke</c> не передаётся. Поэтому к каждому принятому типу
    /// печатается адаптер с сигнатурами на <c>object</c> и <c>byte[]</c> -
    /// одна лишняя строка кода на тип вместо отказа от направления чтения.
    /// </summary>
    public static class StjExecution
    {
        private static readonly Lazy<IReadOnlyList<StjSpecimen>> _specimens =
            new Lazy<IReadOnlyList<StjSpecimen>>(Build);

        private static readonly Lazy<ImmutableCompileResult> _compiled =
            new Lazy<ImmutableCompileResult>(Compile);

        public static IReadOnlyList<StjSpecimen> Specimens => _specimens.Value;

        /// <summary>
        /// Ошибки компиляции всего, что породил генератор на принятых типах.
        ///
        /// Пустота здесь - утверждение сильнее любого сравнения текста: код,
        /// напечатанный по ста двадцати восьми чужим типам, действительно
        /// собирается вместе с ними.
        /// </summary>
        public static IReadOnlyList<Diagnostic> CompilationErrors => _compiled.Value.Errors;

        public static IReadOnlyList<string> GeneratedFileNames => _compiled.Value.GeneratedFileNames;

        private sealed class ImmutableCompileResult
        {
            public required IReadOnlyList<Diagnostic> Errors { get; init; }

            public required IReadOnlyList<string> GeneratedFileNames { get; init; }

            public required Assembly? Assembly { get; init; }
        }

        private static ImmutableCompileResult Compile()
        {
            var accepted = StjCatalogue.Verdicts.Where(v => v.Accepted).ToList();

            var extra = new List<SourceFile>();

            foreach (var verdict in accepted)
            {
                var type = StjCatalogue.Resolve(verdict);

                extra.Add(
                    new SourceFile(
                        StjCatalogue.HostName(type) + ".cs",
                        StjCatalogue.HostSource(type, StjCatalogue.ClosureOf(type))
                        )
                    );

                extra.Add(new SourceFile("Adapter_" + type.Name + ".cs", AdapterSource(type)));
            }

            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);

            var compilation = StjCatalogue.Compilation
                .WithAssemblyName("JsonGoddess.GeneratorTests.StjCorpus")
                .AddSyntaxTrees(
                    extra.Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, path: f.Path))
                    );

            var run = GeneratorHarness.Continue(GeneratorHarness.CreateDriver(), compilation);

            var errors = run.GeneratorDiagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Concat(run.CompilationErrors)
                .ToList();

            if (errors.Count > 0)
            {
                return new ImmutableCompileResult
                {
                    Errors = errors,
                    GeneratedFileNames = run.GeneratedFiles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                    Assembly = null,
                };
            }

            using var stream = new MemoryStream();
            var emitted = run.OutputCompilation.Emit(stream);

            if (!emitted.Success)
            {
                return new ImmutableCompileResult
                {
                    Errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList(),
                    GeneratedFileNames = run.GeneratedFiles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                    Assembly = null,
                };
            }

            return new ImmutableCompileResult
            {
                Errors = Array.Empty<Diagnostic>(),
                GeneratedFileNames = run.GeneratedFiles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                Assembly = Assembly.Load(stream.ToArray()),
            };
        }

        private static string AdapterSource(INamedTypeSymbol type)
        {
            var subject = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var host = "global::" + StjCatalogue.HostNamespace + "." + StjCatalogue.HostName(type);

            var builder = new StringBuilder();

            builder.AppendLine("namespace " + StjCatalogue.HostNamespace);
            builder.AppendLine("{");
            builder.AppendLine("    public static class Adapter_" + type.Name);
            builder.AppendLine("    {");

            builder.AppendLine("        public static global::System.Type Subject => typeof(" + subject + ");");

            builder.AppendLine("        public static object Create()");
            builder.AppendLine("        {");
            builder.AppendLine("            var value = new " + subject + "();");
            if (StjCatalogue.IsTestClass(type))
            {
                builder.AppendLine("            value.Initialize();");
            }

            builder.AppendLine("            return value;");
            builder.AppendLine("        }");

            builder.AppendLine("        public static byte[] Write(object value)");
            builder.AppendLine("        {");
            builder.AppendLine("            using var exhauster = new global::JsonGoddess.PooledUtf8Exhauster();");
            builder.AppendLine("            " + host + ".Serialize(exhauster, (" + subject + ")value);");
            builder.AppendLine("            return exhauster.ToArray();");
            builder.AppendLine("        }");

            builder.AppendLine("        public static object? Read(byte[] json)");
            builder.AppendLine("        {");
            builder.AppendLine(
                "            " + host + ".Deserialize(global::JsonGoddess.DefaultInjector.Instance, json, out "
                + subject + "? value);"
                );
            builder.AppendLine("            return value;");
            builder.AppendLine("        }");

            builder.AppendLine(
                "        public static byte[]? Document => "
                + (StjCatalogue.DocumentExpression(type, subject) ?? "null")
                + ";"
                );

            builder.AppendLine("        public static void Verify(object value)");
            builder.AppendLine("        {");
            if (StjCatalogue.IsTestClass(type))
            {
                builder.AppendLine("            ((" + subject + ")value).Verify();");
            }

            builder.AppendLine("        }");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static IReadOnlyList<StjSpecimen> Build()
        {
            var assembly = _compiled.Value.Assembly;

            if (assembly is null)
            {
                //пустой список тут был бы худшим исходом: тесты по образцам
                //молча прошли бы, не проверив ничего
                throw new InvalidOperationException(
                    "Корпус с порождённым кодом не собрался, поэтому образцов нет. Первая ошибка: "
                    + (CompilationErrors.Count > 0 ? CompilationErrors[0].ToString() : "<нет>")
                    );
            }

            var result = new List<StjSpecimen>();

            foreach (var verdict in StjCatalogue.Verdicts.Where(v => v.Accepted))
            {
                var type = StjCatalogue.Resolve(verdict);
                var adapter = assembly.GetType(StjCatalogue.HostNamespace + ".Adapter_" + type.Name, true)!;

                var create = adapter.GetMethod("Create")!;
                var write = adapter.GetMethod("Write")!;
                var read = adapter.GetMethod("Read")!;
                var verify = adapter.GetMethod("Verify")!;
                var document = (byte[]?)adapter.GetProperty("Document")!.GetValue(null);
                var subject = (Type)adapter.GetProperty("Subject")!.GetValue(null)!;

                result.Add(
                    new StjSpecimen
                    {
                        Name = type.Name,
                        SubjectType = subject,
                        Create = () => create.Invoke(null, null)!,
                        Write = value => (byte[])write.Invoke(null, new[] { value, })!,
                        Read = json => read.Invoke(null, new object[] { json, }),
                        Document = document,
                        Verify = StjCatalogue.IsTestClass(type)
                            ? value => verify.Invoke(null, new[] { value, })
                            : null,
                    }
                    );
            }

            return result;
        }
    }
}
