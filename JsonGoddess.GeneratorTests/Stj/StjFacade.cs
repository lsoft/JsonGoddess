using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using JsonGoddess.GeneratorTests.Harness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonGoddess.GeneratorTests.Stj
{
    /// <summary>
    /// Один тип корпуса глазами <b>потребителя фасада</b>: не «генератор его
    /// принял», а «код, который человек написал под <c>System.Text.Json</c>,
    /// после подмены типа делает то же самое».
    /// </summary>
    public sealed class StjFacadeSpecimen
    {
        public required string Name { get; init; }

        public required Type SubjectType { get; init; }

        public required Func<object> Create { get; init; }

        /// <summary><c>JsonSerializer.Serialize&lt;T&gt;(value)</c>.</summary>
        public required Func<object, string> Write { get; init; }

        /// <summary><c>JsonSerializer.SerializeToUtf8Bytes&lt;T&gt;(value)</c>.</summary>
        public required Func<object, byte[]> WriteUtf8 { get; init; }

        /// <summary><c>JsonSerializer.Deserialize&lt;T&gt;(string)</c>.</summary>
        public required Func<string, object?> ReadText { get; init; }

        /// <summary><c>JsonSerializer.Deserialize&lt;T&gt;(ReadOnlySpan&lt;byte&gt;)</c>.</summary>
        public required Func<byte[], object?> ReadUtf8 { get; init; }

        public required byte[]? Document { get; init; }

        public required Action<object>? Verify { get; init; }

        /// <summary>
        /// Обслужил ли вызов наш быстрый путь - или он ушёл эталону.
        ///
        /// <para>
        /// Это не украшение отчёта, а условие его осмысленности. Все ячейки
        /// ниже сравнивают фасад с эталоном; у типа, ушедшего эталону, фасад
        /// <b>и есть</b> эталон, и ячейка зелена тождественно. Таблица без
        /// этого столбца могла бы быть зелёной целиком и не проверять ничего.
        /// </para>
        /// </summary>
        public required bool IsBound { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Маршрут B из §11.1: их корпус, пропущенный через <b>фасад</b>.
    ///
    /// <para>
    /// Отличие от <see cref="StjExecution"/> принципиальное, и оно же делает
    /// этот прогон приёмкой фазы 8. Там мы звали порождённый хост по имени -
    /// то есть проверяли генератор. Здесь печатается код, который пишет
    /// <b>человек</b>: <c>JsonSerializer.Serialize(value)</c>,
    /// <c>JsonSerializer.Deserialize&lt;T&gt;(json)</c>, ни одного нашего
    /// имени в строке. Подмена типа делается так, как её сделает потребитель, -
    /// псевдонимом, - и дальше всё решает связка «перехват вызова + фасад».
    /// </para>
    ///
    /// <para>
    /// Опции - <b>по умолчанию</b>, и это тоже не деталь. Прогон маршрута A
    /// сверяется с эталоном на <c>UnsafeRelaxedJsonEscaping</c>, потому что
    /// набор экранируемого у нас свой (§8.4) и сравнивать имеет смысл
    /// равнозначные настройки. Потребителю фасада такой выбор недоступен: он
    /// не звал ни энкодер, ни опции, он просто собрал проект с другим пакетом.
    /// Поэтому здесь эталон берётся ровно тот, который получил бы он, - и
    /// расхождение, если оно есть, обязано быть видно, а не спрятано за
    /// удобной настройкой.
    /// </para>
    ///
    /// <para>
    /// Кандидаты - <b>все</b> типы корпуса, которые вообще можно создать, а не
    /// только принятые генератором. В этом весь смысл проверки фасада: тип, от
    /// которого генератор отказался, обязан продолжать работать через эталон,
    /// и «обязан» здесь проверяется, а не обещается.
    /// </para>
    /// </summary>
    public static class StjFacade
    {
        public const string ConsumerNamespace = "JsonGoddess.GeneratorTests.Stj.Consumers";

        private static readonly Lazy<CompiledFacade> _compiled = new Lazy<CompiledFacade>(Compile);

        private static readonly Lazy<IReadOnlyList<StjFacadeSpecimen>> _specimens =
            new Lazy<IReadOnlyList<StjFacadeSpecimen>>(Build);

        /// <summary>
        /// Типы, которым печатается потребитель: все кандидаты корпуса, кроме
        /// тех, которые нельзя даже создать. Абстрактный тип и тип без
        /// доступного конструктора без параметров отсеиваются не по
        /// снисходительности, а потому что <c>new T()</c> по ним не
        /// компилируется - написать такой вызов не смог бы и потребитель.
        /// </summary>
        public static IReadOnlyList<INamedTypeSymbol> Consumable =>
            StjCatalogue.Candidates.Where(IsConstructible).ToList();

        public static IReadOnlyList<StjFacadeSpecimen> Specimens => _specimens.Value;

        public static IReadOnlyList<Diagnostic> CompilationErrors => _compiled.Value.Errors;

        /// <summary>
        /// Диагностики генератора на этой компиляции. Здесь живёт
        /// <c>JGD001</c>: отступление обязано быть <b>сказано</b>, иначе
        /// потребитель не узнает, что половина его вызовов идёт прежним
        /// путём.
        /// </summary>
        public static IReadOnlyList<Diagnostic> Diagnostics => _compiled.Value.Diagnostics;

        private sealed class CompiledFacade
        {
            public required IReadOnlyList<Diagnostic> Errors { get; init; }

            public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }

            public required Assembly? Assembly { get; init; }
        }

        private static bool IsConstructible(INamedTypeSymbol type)
        {
            if (type.IsAbstract)
            {
                return false;
            }

            //required-член сделал бы «new T()» ошибкой компиляции; в
            //зафиксированном корпусе таких нет, но проверка стоит дёшево, а
            //её отсутствие стоило бы всей сборки после первого же обновления
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (current.GetMembers().Any(m => m.IsRequired()))
                {
                    return false;
                }
            }

            return type.IsValueType
                || type.InstanceConstructors.Any(
                    c => c.Parameters.Length == 0 && c.DeclaredAccessibility != Accessibility.Private
                    );
        }

        private static CompiledFacade Compile()
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);

            var trees = Consumable
                .Select(type => CSharpSyntaxTree.ParseText(
                    ConsumerSource(type),
                    parseOptions,
                    path: "Consumer_" + type.Name + ".cs"
                    ))
                .ToList();

            var compilation = StjCatalogue.Compilation
                .WithAssemblyName("JsonGoddess.GeneratorTests.StjFacade")
                .AddSyntaxTrees(trees);

            var run = GeneratorHarness.Continue(GeneratorHarness.CreateDriver(), compilation);

            var errors = run.GeneratorDiagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Concat(run.CompilationErrors)
                .ToList();

            if (errors.Count > 0)
            {
                return new CompiledFacade
                {
                    Errors = errors,
                    Diagnostics = run.GeneratorDiagnostics,
                    Assembly = null,
                };
            }

            using var stream = new MemoryStream();
            var emitted = run.OutputCompilation.Emit(stream);

            if (!emitted.Success)
            {
                return new CompiledFacade
                {
                    Errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList(),
                    Diagnostics = run.GeneratorDiagnostics,
                    Assembly = null,
                };
            }

            return new CompiledFacade
            {
                Errors = Array.Empty<Diagnostic>(),
                Diagnostics = run.GeneratorDiagnostics,
                Assembly = Assembly.Load(stream.ToArray()),
            };
        }

        /// <summary>
        /// Код потребителя. Ни одного нашего имени в вызовах - только
        /// <c>JsonSerializer</c>, подменённый псевдонимом ровно так, как его
        /// подменяет <c>global using</c> из §10.
        ///
        /// <para>
        /// Перегрузок взято четыре, а не одна: быстрый путь есть у восьми
        /// методов фасада, и у чтения их два входа (строка и UTF-8), а у
        /// записи два выхода (строка и байты). Проверять один вход значило бы
        /// оставить три непроверенными - а расходятся они независимо.
        /// </para>
        /// </summary>
        private static string ConsumerSource(INamedTypeSymbol type)
        {
            var subject = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isTestClass = StjCatalogue.IsTestClass(type);

            var builder = new StringBuilder();

            builder.AppendLine("using JsonSerializer = global::JsonGoddess.Compat.JsonSerializer;");
            builder.AppendLine();
            builder.AppendLine("namespace " + ConsumerNamespace);
            builder.AppendLine("{");
            builder.AppendLine("    public static class Consumer_" + type.Name);
            builder.AppendLine("    {");

            builder.AppendLine("        public static global::System.Type Subject => typeof(" + subject + ");");

            builder.AppendLine("        public static object Create()");
            builder.AppendLine("        {");
            builder.AppendLine("            var value = new " + subject + "();");
            if (isTestClass)
            {
                builder.AppendLine("            value.Initialize();");
            }

            builder.AppendLine("            return value;");
            builder.AppendLine("        }");

            builder.AppendLine("        public static string Write(object value) =>");
            builder.AppendLine("            JsonSerializer.Serialize<" + subject + ">((" + subject + ")value);");

            builder.AppendLine("        public static byte[] WriteUtf8(object value) =>");
            builder.AppendLine("            JsonSerializer.SerializeToUtf8Bytes<" + subject + ">((" + subject + ")value);");

            builder.AppendLine("        public static object? ReadText(string json) =>");
            builder.AppendLine("            JsonSerializer.Deserialize<" + subject + ">(json);");

            //ReadOnlySpan<byte> - отдельная перегрузка фасада, и быстрый путь
            //у неё свой; неявное преобразование из byte[] выбрало бы её же,
            //но писать это явно честнее
            builder.AppendLine("        public static object? ReadUtf8(byte[] utf8) =>");
            builder.AppendLine(
                "            JsonSerializer.Deserialize<" + subject
                + ">(new global::System.ReadOnlySpan<byte>(utf8));"
                );

            builder.AppendLine(
                "        public static byte[]? Document => "
                + (StjCatalogue.DocumentExpression(type, subject) ?? "null")
                + ";"
                );

            builder.AppendLine("        public static void Verify(object value)");
            builder.AppendLine("        {");
            if (isTestClass)
            {
                builder.AppendLine("            ((" + subject + ")value).Verify();");
            }

            builder.AppendLine("        }");

            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static IReadOnlyList<StjFacadeSpecimen> Build()
        {
            var assembly = _compiled.Value.Assembly;

            if (assembly is null)
            {
                //пустой список тут был бы худшим исходом: тесты по образцам
                //молча прошли бы, не проверив ничего
                throw new InvalidOperationException(
                    "Корпус поверх фасада не собрался, поэтому образцов нет. Первая ошибка: "
                    + (CompilationErrors.Count > 0 ? CompilationErrors[0].ToString() : "<нет>")
                    );
            }

            //регистрацию быстрых путей делает [ModuleInitializer] собранной
            //сборки. Зовём его сами, а не полагаемся на первое обращение:
            //IsBound читается до вызовов, и «ещё не инициализировано» выглядело
            //бы как «генератор не справился»
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

            var result = new List<StjFacadeSpecimen>();

            foreach (var type in Consumable)
            {
                var consumer = assembly.GetType(ConsumerNamespace + ".Consumer_" + type.Name, true)!;

                var create = consumer.GetMethod("Create")!;
                var write = consumer.GetMethod("Write")!;
                var writeUtf8 = consumer.GetMethod("WriteUtf8")!;
                var readText = consumer.GetMethod("ReadText")!;
                var readUtf8 = consumer.GetMethod("ReadUtf8")!;
                var verify = consumer.GetMethod("Verify")!;
                var document = (byte[]?)consumer.GetProperty("Document")!.GetValue(null);
                var subject = (Type)consumer.GetProperty("Subject")!.GetValue(null)!;

                result.Add(
                    new StjFacadeSpecimen
                    {
                        Name = type.Name,
                        SubjectType = subject,
                        Create = () => create.Invoke(null, null)!,
                        Write = value => (string)write.Invoke(null, new[] { value, })!,
                        WriteUtf8 = value => (byte[])writeUtf8.Invoke(null, new[] { value, })!,
                        ReadText = json => readText.Invoke(null, new object[] { json, }),
                        ReadUtf8 = utf8 => readUtf8.Invoke(null, new object[] { utf8, }),
                        Document = document,
                        Verify = StjCatalogue.IsTestClass(type)
                            ? value => verify.Invoke(null, new[] { value, })
                            : null,
                        IsBound = Bound(subject),
                    }
                    );
            }

            return result;
        }

        /// <summary>
        /// <c>CompatBinding&lt;T&gt;.IsBound</c> для типа, известного только в
        /// рантайме. Закрыть генерик приходится рефлексией: <c>T</c> живёт в
        /// сборке, которой на момент компиляции теста не существовало.
        /// </summary>
        private static bool Bound(Type subject)
        {
            var binding = typeof(global::JsonGoddess.Compat.CompatBinding<>).MakeGenericType(subject);
            return (bool)binding.GetProperty("IsBound")!.GetValue(null)!;
        }
    }

    internal static class SymbolRequirements
    {
        /// <summary>
        /// <c>required</c> на члене. Свойство <c>ISymbol.IsRequired</c>
        /// появилось в Roslyn 4.4; читаем его так, чтобы сборка не зависела от
        /// версии пакета, которая окажется в проекте завтра.
        /// </summary>
        public static bool IsRequired(this ISymbol symbol) =>
            symbol switch
            {
                IPropertySymbol property => property.IsRequired,
                IFieldSymbol field => field.IsRequired,
                _ => false,
            };
    }
}
