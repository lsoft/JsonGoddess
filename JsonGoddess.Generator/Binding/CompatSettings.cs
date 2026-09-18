using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// MSBuild-свойства Compat-слоя, прочитанные один раз (§10 плана).
    ///
    /// <para>
    /// Тип-значение и <c>IEquatable</c> не для красоты: настройки едут по
    /// инкрементальному конвейеру, а он кэширует по равенству. Класс без
    /// <c>Equals</c> заставлял бы пересвязывать всю сборку на каждой правке
    /// любого файла.
    /// </para>
    /// </summary>
    public readonly struct CompatSettings : IEquatable<CompatSettings>
    {
        public const string EnabledProperty = "build_property.JsonGoddessCompat";
        public const string StrictProperty = "build_property.JsonGoddessCompatStrict";

        public static readonly CompatSettings Default = new CompatSettings(true, null, null);

        /// <summary>Перехват вызовов фасада включён.</summary>
        public readonly bool Enabled;

        /// <summary>
        /// Нижняя граница строгости для <c>JGD001</c>/<c>JGD002</c> - или
        /// <c>null</c>, если свойство не задано.
        ///
        /// <para>
        /// Именно <b>граница</b>, а не точное значение, и это важно:
        /// <c>JGD002</c> объявлен <c>Warning</c>, потому что это дыра в
        /// генераторе, и <c>JsonGoddessCompatStrict=warning</c> не должен
        /// делать его тише. Свойство поднимает громкость и только.
        /// </para>
        /// </summary>
        public readonly DiagnosticSeverity? Floor;

        /// <summary>
        /// Значение <c>JsonGoddessCompatStrict</c>, которого мы не понимаем, -
        /// или <c>null</c>, если понимаем.
        ///
        /// <para>
        /// Хранится, чтобы о нём <b>сказать</b>. Опечатка в имени уровня иначе
        /// означала бы, что человек попросил строгости, не получил её и не
        /// узнал об этом, - то есть тот самый молчаливый исход, против которого
        /// весь §1.
        /// </para>
        /// </summary>
        public readonly string? UnrecognizedStrictValue;

        private CompatSettings(bool enabled, DiagnosticSeverity? floor, string? unrecognized)
        {
            Enabled = enabled;
            Floor = floor;
            UnrecognizedStrictValue = unrecognized;
        }

        public static CompatSettings Read(AnalyzerConfigOptions options)
        {
            var enabled = !options.TryGetValue(EnabledProperty, out var switchValue)
                || !string.Equals(switchValue, "disable", StringComparison.OrdinalIgnoreCase);

            if (!options.TryGetValue(StrictProperty, out var strict) || string.IsNullOrWhiteSpace(strict))
            {
                return new CompatSettings(enabled, null, null);
            }

            strict = strict.Trim();

            //«info» - это умолчание, названное вслух. Принимается не из
            //снисходительности: без него свойство нельзя было бы выставить в
            //Directory.Build.props и переопределить в одном проекте обратно
            if (string.Equals(strict, "info", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Info, null);
            }

            if (string.Equals(strict, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Warning, null);
            }

            if (string.Equals(strict, "error", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Error, null);
            }

            return new CompatSettings(enabled, null, strict);
        }

        /// <summary>
        /// Severity для диагностики с объявленной <paramref name="declared"/>:
        /// громче объявленного - можно, тише - нет.
        /// </summary>
        public DiagnosticSeverity? Raise(DiagnosticSeverity declared)
        {
            return Floor is null || Floor.Value <= declared ? null : Floor.Value;
        }

        public bool Equals(CompatSettings other)
        {
            return Enabled == other.Enabled
                && Floor == other.Floor
                && string.Equals(UnrecognizedStrictValue, other.UnrecognizedStrictValue, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is CompatSettings other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Enabled ? 1 : 0;
            hash = unchecked((hash * 31) + (Floor?.GetHashCode() ?? 0));
            return unchecked((hash * 31) + (UnrecognizedStrictValue?.GetHashCode() ?? 0));
        }
    }
}
