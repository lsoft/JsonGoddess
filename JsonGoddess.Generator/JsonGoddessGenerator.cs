using System.Linq;
using System.Threading;
using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JsonGoddess.Generator
{
    /// <summary>
    /// Инкрементальный генератор JsonGoddess.
    ///
    /// Схема конвейера нестандартная, и это осознанно (§9 плана). Обычный
    /// совет - "не таскайте Compilation по конвейеру" - писан для генераторов,
    /// чей выход зависит только от размеченного объявления. Здесь не так:
    /// <c>[JsonSubject(typeof(T))]</c> висит на хосте, а код порождается по
    /// транзитивному замыканию графа типов, лежащего в других файлах. Генератор,
    /// заведённый строго от узла хоста, был бы идеально инкрементален и выдавал
    /// бы устаревший код после переименования члена в соседнем файле.
    ///
    /// Поэтому <see cref="Compilation"/> остаётся входом, связывание идёт на
    /// каждую правку, а кэш берётся из <b>равенства выходов</b>: последний шаг
    /// отдаёт <see cref="GenerationResult"/> - голые строки, без символов и
    /// узлов. Совпал результат - выходной шаг помечается Cached, и Roslyn
    /// переиспользует уже разобранные деревья. Именно это, а не работа
    /// генератора, - доминирующая стоимость в IDE.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class JsonGoddessGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var hosts = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    HostBinder.SubjectAttribute,
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (syntaxContext, _) =>
                    {
                        //из триггера уезжает имя, а не символ: этот шаг
                        //переисполняется только для изменившихся деревьев, и его
                        //закэшированные значения приезжают из предыдущей
                        //компиляции, а связывать символы разных компиляций нельзя
                        var symbol = (INamedTypeSymbol)syntaxContext.TargetSymbol;

                        return new HostReference(
                            BuildMetadataName(symbol),
                            LocationInfo.From(symbol)
                            );
                    })
                .Collect();

            //потоковый читатель печатается не всегда, и решает это свойство
            //сборки плюс факт ссылки на ASP.NET Core - см. StreamingSettings
            var streaming = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) => StreamingSettings.Read(provider.GlobalOptions));

            var results = hosts
                .Combine(context.CompilationProvider)
                .Combine(streaming)
                .Select(static (pair, token) => HostBinder.Bind(pair.Left.Right, pair.Left.Left, pair.Right, token));

            context.RegisterSourceOutput(results, static (productionContext, result) => Emit(productionContext, result));

            InitializeCompat(context);
        }

        /// <summary>
        /// Маршрут A Compat-слоя (§10): перехват вызовов фасада.
        ///
        /// <para>
        /// Отдельный конвейер, а не ветка в основном, потому что вход у него
        /// другой: не размеченное объявление, а <b>вызов</b> в любом файле.
        /// Триггер дешёвый и синтаксический - имя метода, - а решение «это
        /// действительно наш фасад» принимается уже с семантикой.
        /// </para>
        ///
        /// <para>
        /// Выключатель <c>JsonGoddessCompat=disable</c> есть, потому что
        /// перехват - это подмена поведения на всю сборку, и должен быть способ
        /// от неё отказаться, не убирая ссылку. Пользуется им в первую очередь
        /// наш же тест фасада: он проверяет <b>отступление</b> к эталону, а
        /// генератор, обслуживший его типы, нечего было бы и проверять.
        /// </para>
        /// </summary>
        private static void InitializeCompat(IncrementalGeneratorInitializationContext context)
        {
            var settings = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) => CompatSettings.Read(provider.GlobalOptions));

            //про непонятое значение JsonGoddessCompatStrict говорится отдельно
            //от связывания и раньше него: оно не зависит ни от одного вызова
            //фасада, а сказать о нём надо и проекту, где таких вызовов пока нет
            context.RegisterSourceOutput(
                settings,
                static (productionContext, value) =>
                {
                    if (value.UnrecognizedStrictValue is null)
                    {
                        return;
                    }

                    productionContext.ReportDiagnostic(
                        new DiagnosticInfo(
                            JsonGoddessDiagnostics.CompatStrictValueIsNotRecognizedId,
                            null,
                            value.UnrecognizedStrictValue
                            ).ToDiagnostic()
                        );
                });

            var sites = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => IsFacadeCandidate(node),
                    static (syntaxContext, token) => ReadCallSite(syntaxContext, token))
                .Where(static site => site is not null)
                .Select(static (site, _) => site!.Value)
                .Collect();

            var streaming = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) => StreamingSettings.Read(provider.GlobalOptions));

            var compat = sites
                .Combine(settings)
                .Combine(streaming)
                .Combine(context.CompilationProvider)
                .Select(static (pair, token) => pair.Left.Left.Right.Enabled
                    ? CompatBinder.Bind(
                        pair.Right,
                        pair.Left.Left.Left,
                        pair.Left.Left.Right,
                        pair.Left.Right,
                        token
                        )
                    : GenerationResult.Empty);

            context.RegisterSourceOutput(compat, static (productionContext, result) => Emit(productionContext, result));
        }

        /// <summary>
        /// Дешёвый синтаксический отсев: имя вызванного метода. Предикат
        /// исполняется на каждом узле каждого изменившегося дерева, поэтому
        /// здесь нельзя ни семантики, ни аллокаций.
        /// </summary>
        private static bool IsFacadeCandidate(SyntaxNode node)
        {
            if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
            {
                return false;
            }

            var name = access.Name.Identifier.ValueText;
            return name == "Serialize" || name == "SerializeToUtf8Bytes" || name == "Deserialize";
        }

        private static CompatCallSite? ReadCallSite(GeneratorSyntaxContext context, CancellationToken token)
        {
            if (context.SemanticModel.GetSymbolInfo(context.Node, token).Symbol is not IMethodSymbol method)
            {
                return null;
            }

            if (method.ContainingType?.ToDisplayString() != CompatBinder.FacadeMetadataName)
            {
                return null;
            }

            //перехватывается не всякая перегрузка, а та, у которой быстрый путь
            //есть, - фасад помечает их сам (CompatFastPathAttribute). Из ста
            //трёх методов таких восемь; у остальных T известен ровно так же, но
            //работа уходит эталону безусловно, и порождать для них код значило
            //бы печатать заведомо мёртвый - а на несвязавшемся типе ещё и
            //жаловаться на вызов, которому быстрый путь всё равно не достался бы
            if (!HasFastPath(method))
            {
                return null;
            }

            //«T известен статически» - это буквально: у обобщённого метода в
            //пользовательском коде аргументом приедет его собственный параметр
            //типа, и обслуживать там нечего
            if (method.TypeArguments.Length != 1
                || method.TypeArguments[0] is not INamedTypeSymbol argument
                || argument.TypeKind == TypeKind.Error
                || argument.IsUnboundGenericType)
            {
                return null;
            }

            var referenceId = DocumentationCommentId.CreateReferenceId(argument);
            if (string.IsNullOrEmpty(referenceId))
            {
                return null;
            }

            return new CompatCallSite(referenceId, LocationInfo.From(context.Node.GetLocation()));
        }

        private static bool HasFastPath(IMethodSymbol method)
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == CompatBinder.FastPathAttributeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Emit(SourceProductionContext context, GenerationResult result)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            foreach (var file in result.Files)
            {
                context.AddSource(file.HintName, file.Text);
            }
        }

        private static string BuildMetadataName(INamedTypeSymbol symbol)
        {
            var name = symbol.MetadataName;

            for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
            {
                name = containing.MetadataName + "+" + name;
            }

            return symbol.ContainingNamespace.IsGlobalNamespace
                ? name
                : symbol.ContainingNamespace.ToDisplayString() + "." + name;
        }
    }
}
