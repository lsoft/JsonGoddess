#if !NET7_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Полифилл для ключевого слова <c>required</c> на net472.
    ///
    /// Нужен, как и <see cref="IsExternalInit"/>, <b>тестам</b>, а не
    /// библиотеке: компилятор на каждый <c>required</c>-член требует этот тип
    /// и <see cref="CompilerFeatureRequiredAttribute"/>, а в старом рантайме
    /// их нет.
    ///
    /// Знать о нём потребителю надо. На net472 у него два пути: вписать эти
    /// же два типа себе или писать <c>[JsonRequired]</c> вместо ключевого
    /// слова - атрибут никакого полифилла не требует и означает у эталона
    /// ровно то же самое (проверено пробой).
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false
        )]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    /// <summary>
    /// Второй из двух типов, которых компилятор требует под <c>required</c>.
    /// Разметка им - способ сказать старому компилятору «не притворяйся, что
    /// понял этот тип»; нам важно только, что тип существует.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public const string RefStructs = nameof(RefStructs);

        public const string RequiredMembers = nameof(RequiredMembers);

        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }

        public bool IsOptional { get; set; }
    }
}

#endif
