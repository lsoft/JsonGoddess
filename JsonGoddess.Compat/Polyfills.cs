#if NETSTANDARD2_0

using System;

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Атрибуты обрезки появились в .NET 5; в netstandard2.0 их нет.
    ///
    /// <para>
    /// Печатаем свои - <c>internal</c>, поэтому наружу они не видны и ничей
    /// чужой полифилл не задевают. Нужны они здесь только затем, чтобы текст
    /// фасада был один на все таргеты: 42 метода из 103 помечены у эталона
    /// обоими атрибутами, и обкладывать каждый из них <c>#if</c>'ом значило бы
    /// вчетверо увеличить файл ради таргета, где обрезки не бывает вовсе.
    /// </para>
    ///
    /// <para>
    /// Анализатор обрезки на netstandard2.0 их не читает, и это не потеря:
    /// netstandard2.0-сборку исполняет net472, где ни trimming'а, ни AOT нет.
    /// </para>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
        Inherited = false
        )]
    internal sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message)
        {
            Message = message;
        }

        public string Message { get; }

        public string? Url { get; set; }
    }

    /// <inheritdoc cref="RequiresUnreferencedCodeAttribute" />
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
        Inherited = false
        )]
    internal sealed class RequiresDynamicCodeAttribute : Attribute
    {
        public RequiresDynamicCodeAttribute(string message)
        {
            Message = message;
        }

        public string Message { get; }

        public string? Url { get; set; }
    }
}

#endif
