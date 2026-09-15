using System;
using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Выбор конструктора десериализации и связывание его параметров с членами.
    ///
    /// Все правила здесь сняты <b>прогоном эталона</b>, а не выведены из
    /// документации, и половина из них неочевидна:
    ///
    /// <list type="number">
    /// <item>конструктор без параметров <b>побеждает</b>, даже когда рядом есть
    /// публичный параметризованный: объект собирается им, а члены
    /// присваиваются setter'ами;</item>
    /// <item>единственный параметризованный берётся сам, без атрибута;</item>
    /// <item>два параметризованных без <c>[JsonConstructor]</c> - у эталона
    /// <c>NotSupportedException</c> <b>в рантайме</b>; у нас отказ на
    /// компиляции, что то же решение, только раньше;</item>
    /// <item>каждый параметр обязан связаться с членом - иначе у эталона
    /// <c>InvalidOperationException</c>, и не на «плохом» документе, а на
    /// любом;</item>
    /// <item>связывание идёт по <b>имени члена</b> без учёта регистра
    /// (<c>alpha</c> → <c>Alpha</c>), но в документе ищется <b>JSON-имя</b>
    /// этого члена: у переименованного <c>[JsonPropertyName("renamed")]</c>
    /// свойства параметр <c>alpha</c> читается из ключа <c>renamed</c>;</item>
    /// <item>умолчание параметра исполняется: <c>beta = 42</c> на документе без
    /// <c>beta</c> даёт 42, а не ноль.</item>
    /// </list>
    /// </summary>
    public static class ConstructorBinder
    {
        /// <summary>
        /// Возвращает параметры выбранного конструктора и помечает связанные
        /// члены. <c>null</c> - отказ, диагностика уже выдана.
        /// </summary>
        public static IReadOnlyList<ParameterModel>? Bind(
            INamedTypeSymbol subject,
            List<MemberModel> members,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            var constructor = Choose(subject, known, location, diagnostics, ref failed);

            if (constructor is null || constructor.Parameters.Length == 0)
            {
                //либо отказ уже выдан, либо конструктор без параметров
                return failed ? null : RefuseStandaloneInit(subject, members, location, diagnostics, ref failed);
            }

            var result = new List<ParameterModel>(constructor.Parameters.Length);

            foreach (var parameter in constructor.Parameters)
            {
                var index = members.FindIndex(
                    m => string.Equals(m.MemberName, parameter.Name, StringComparison.OrdinalIgnoreCase)
                    );

                if (index < 0)
                {
                    Refuse(subject, location, diagnostics, ref failed,
                        "constructor parameter '" + parameter.Name
                        + "' does not match any serializable member; System.Text.Json requires every parameter of "
                        + "the deserialization constructor to bind to a property or field, and throws on any document "
                        + "otherwise");
                    return null;
                }

                var member = members[index];

                if (!member.CanWrite)
                {
                    Refuse(subject, location, diagnostics, ref failed,
                        "constructor parameter '" + parameter.Name + "' binds to member '" + member.MemberName
                        + "', which is not serialized, so the value could never make it back into the document");
                    return null;
                }

                members[index] = WithConstructorParameter(member);

                result.Add(new ParameterModel(member.MemberName, Default(parameter, member)));
            }

            return RefuseStandaloneInit(subject, members, location, diagnostics, ref failed) is null
                ? null
                : result;
        }

        /// <summary>
        /// <c>init</c>-член, который конструктору не аргумент, - отказ.
        ///
        /// Причина не в том, что его трудно присвоить, а в том, что <b>нельзя
        /// не присвоить</b>: инициализатор объекта либо есть в тексте, либо
        /// нет, третьего он не знает. А присваивать надо не всегда: эталон
        /// оставляет члену значение его инициализатора, если имени в документе
        /// не было (проверено прогоном), и повторить это в инициализаторе
        /// объекта нечем.
        ///
        /// У позиционных <c>record</c> этой беды нет: их <c>init</c>-свойства
        /// все до одного - аргументы конструктора, и присваиваются им.
        /// </summary>
        private static IReadOnlyList<ParameterModel>? RefuseStandaloneInit(
            INamedTypeSymbol subject,
            List<MemberModel> members,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            foreach (var member in members)
            {
                if (!member.IsInitOnly || !member.CanRead || member.IsConstructorParameter)
                {
                    continue;
                }

                Refuse(subject, location, diagnostics, ref failed,
                    "member '" + member.MemberName + "' has an init-only setter and is not a constructor parameter; "
                    + "such a member can only be assigned in an object initializer, which cannot express "
                    + "'leave it alone when the document does not carry it' - and System.Text.Json does leave it alone, "
                    + "keeping the member's own initializer");
                return null;
            }

            return Array.Empty<ParameterModel>();
        }

        /// <summary>
        /// Умолчание параметра, каким его печатать в инициализацию локальной.
        ///
        /// Этим одним выражением заменяется отслеживание «было ли имя в
        /// документе»: локальная заводится с умолчанием, документ её
        /// перезаписывает, если имя встретилось. Ни флага, ни второй ветки.
        /// </summary>
        private static string Default(IParameterSymbol parameter, MemberModel member)
        {
            if (!parameter.HasExplicitDefaultValue)
            {
                return "default(" + member.Value.Declaration + ")";
            }

            var value = parameter.ExplicitDefaultValue;

            if (value is null)
            {
                return "default(" + member.Value.Declaration + ")";
            }

            //литерал печатает Roslyn, а не мы: у char, строки, bool и
            //вещественных правила записи разные, и ошибиться в них можно
            //молча - а молча выданный валидный код с другим значением и есть
            //худший из исходов
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(
                value, quoteStrings: true, useHexadecimalNumbers: false
                );

            //приведение к типу члена: у enum-параметра умолчание приезжает
            //подлежащим числом, у byte/short - int'ом
            return "(" + member.Value.Declaration + ")(" + literal + ")";
        }

        private static MemberModel WithConstructorParameter(MemberModel member)
        {
            return new MemberModel(
                member.MemberName,
                member.JsonName,
                member.JsonNameUtf8,
                member.Value,
                member.CanWrite,

                //член, связанный с параметром, читается всегда - даже если
                //setter'а у него нет вовсе: значение приезжает конструктором
                true,
                member.Condition,
                member.Order,
                isConstructorParameter: true,
                isInitOnly: member.IsInitOnly
                );
        }

        /// <summary>
        /// Конструктор десериализации. <c>null</c> без отказа означает
        /// «подходит конструктор без параметров».
        /// </summary>
        private static IMethodSymbol? Choose(
            INamedTypeSymbol subject,
            KnownSymbols known,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            var marked = subject.InstanceConstructors
                .Where(c => known.Has(c, known.JsonConstructor))
                .ToList();

            if (marked.Count > 1)
            {
                Refuse(subject, location, diagnostics, ref failed,
                    "more than one constructor carries [JsonConstructor]");
                return null;
            }

            if (marked.Count == 1)
            {
                if (!IsAccessible(marked[0]))
                {
                    //эталон вызывает и приватный - через рефлексию; у нас
                    //порождённый код лежит в чужом классе и такого хода не имеет
                    Refuse(subject, location, diagnostics, ref failed,
                        "the [JsonConstructor] constructor is not public or internal, and generated code cannot call it "
                        + "(System.Text.Json reaches it by reflection, which JsonGoddess does not use)");
                    return null;
                }

                return marked[0];
            }

            //конструктор без параметров побеждает даже при наличии публичного
            //параметризованного - проверено прогоном на паре, дающей разный
            //результат
            var parameterless = subject.InstanceConstructors
                .FirstOrDefault(c => c.Parameters.Length == 0 && IsAccessible(c));

            if (parameterless is not null)
            {
                return null;
            }

            var candidates = subject.InstanceConstructors
                .Where(c => c.Parameters.Length > 0 && IsAccessible(c))
                .ToList();

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            Refuse(subject, location, diagnostics, ref failed,
                candidates.Count == 0
                    ? "no accessible parameterless constructor and no accessible parameterized one either"
                    : "several parameterized constructors and no [JsonConstructor] to choose between them; "
                    + "System.Text.Json throws NotSupportedException on this type at run time, so mark one");
            return null;
        }

        private static bool IsAccessible(IMethodSymbol constructor) =>
            constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;

        private static void Refuse(
            INamedTypeSymbol subject,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            ref bool failed,
            string reason
            )
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    JsonGoddessDiagnostics.SubjectIsNotSupportedId,
                    location,
                    subject.ToDisplayString(),
                    reason
                    )
                );
            failed = true;
        }
    }
}
