using System.Collections.Generic;
using JsonGoddess.Generator.Diagnostics;
using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Настройки хоста, прочитанные из <c>[JsonGuard]</c> (§6.3 плана).
    ///
    /// Устроено тем же способом, что и <see cref="SerializationOptions"/>:
    /// атрибут читается по имени и числом, без ссылки на рантайм-сборку из
    /// компилятора. Разница в одном - атрибут здесь <b>наш</b>
    /// (<c>JsonGoddess.JsonGuardAttribute</c>), а не чужой, поэтому разбирать
    /// приходится не двадцать семь свойств произвольного стороннего типа, а
    /// собственный контракт из одного обязательного аргумента и одного
    /// именованного.
    /// </summary>
    public sealed class GuardOptions
    {
        public static readonly GuardOptions Default = new GuardOptions(JsonGuard.None, 64);

        public JsonGuard Guards { get; }

        /// <summary>
        /// Предел вложенности. Значим только при установленном
        /// <see cref="JsonGuard.MaxDepth"/> - без него хост может назвать
        /// любое число, оно ни на что не повлияет.
        /// </summary>
        public int MaxDepth { get; }

        private GuardOptions(JsonGuard guards, int maxDepth)
        {
            Guards = guards;
            MaxDepth = maxDepth;
        }

        /// <summary>
        /// Набор, назначенный не автором хоста, а нами. Нужен Compat-слою
        /// (§10): там <c>[JsonGuard]</c> писать некому, а строгость обязана
        /// совпасть с эталонной.
        /// </summary>
        public static GuardOptions For(JsonGuard guards, int maxDepth) => new GuardOptions(guards, maxDepth);

        public static GuardOptions Read(
            INamedTypeSymbol host,
            KnownSymbols known,
            List<DiagnosticInfo> diagnostics,
            ref bool failed
            )
        {
            if (known.Guard is null)
            {
                return Default;
            }

            foreach (var attribute in host.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Guard))
                {
                    continue;
                }

                var guards = attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is int raw
                        ? (JsonGuard)raw
                        : JsonGuard.None;

                var maxDepth = 64;
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "MaxDepth" && named.Value.Value is int value)
                    {
                        maxDepth = value;
                    }
                }

                //MaxDepth считает так же, как эталон: корень уже глубина 1
                //(проверено пробой на System.Text.Json), поэтому предел меньше
                //единицы не пропустил бы ни одного документа - это не
                //«очень строгий страж», а сломанная настройка
                if ((guards & JsonGuard.MaxDepth) != 0 && maxDepth < 1)
                {
                    diagnostics.Add(
                        new DiagnosticInfo(
                            JsonGoddessDiagnostics.InvalidMaxDepthId,
                            LocationInfo.From(host),
                            maxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            host.ToDisplayString()
                            )
                        );
                    failed = true;
                }

                return new GuardOptions(guards, maxDepth);
            }

            return Default;
        }
    }
}
