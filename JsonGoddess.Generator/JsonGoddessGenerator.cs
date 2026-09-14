using System.Linq;
using JsonGoddess.Generator.Binding;
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

            var results = hosts
                .Combine(context.CompilationProvider)
                .Select(static (pair, token) => HostBinder.Bind(pair.Right, pair.Left, token));

            context.RegisterSourceOutput(
                results,
                static (productionContext, result) =>
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
                    }

                    foreach (var file in result.Files)
                    {
                        productionContext.AddSource(file.HintName, file.Text);
                    }
                });
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
