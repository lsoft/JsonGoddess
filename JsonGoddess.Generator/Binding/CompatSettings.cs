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
        public const string WebProperty = "build_property.JsonGoddessCompatWeb";

        public static readonly CompatSettings Default = new CompatSettings(true, null, null, null);

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

        /// <summary>
        /// Печатать ли второй вариант кода - под <c>JsonSerializerDefaults.Web</c>,
        /// то есть под то, что строят ASP.NET Core MVC и minimal API.
        ///
        /// <para>
        /// <c>null</c> - «не просили ни того ни другого», и тогда решает не
        /// умолчание, а <b>факт</b>: ссылается ли сборка на ASP.NET Core
        /// (<c>CompatBinder</c> спрашивает об этом компиляцию). Веб-вариант -
        /// это второй писатель и второй читатель на каждый обслуженный тип,
        /// то есть примерно удвоение порождаемого кода (§16.1); консольному
        /// приложению он не нужен ни разу, а веб-проекту нужен всегда, и
        /// отличить одно от другого можно, не спрашивая человека.
        /// </para>
        ///
        /// <para>
        /// Раньше здесь стояло «по умолчанию нет», и человек узнавал о
        /// свойстве из сообщения, которое мост печатает, отступая. Это
        /// работало, но требовало сперва заметить, что не ускорилось, - то
        /// есть цена ошибки платилась молчанием. Явное значение свойства
        /// по-прежнему сильнее факта: и <c>enable</c>, и <c>disable</c>
        /// делают ровно то, что написано.
        /// </para>
        /// </summary>
        public readonly bool? Web;

        private CompatSettings(bool enabled, DiagnosticSeverity? floor, string? unrecognized, bool? web)
        {
            Enabled = enabled;
            Floor = floor;
            UnrecognizedStrictValue = unrecognized;
            Web = web;
        }

        public static CompatSettings Read(AnalyzerConfigOptions options)
        {
            var enabled = !options.TryGetValue(EnabledProperty, out var switchValue)
                || !string.Equals(switchValue, "disable", StringComparison.OrdinalIgnoreCase);

            //Трёхзначно: «включить», «выключить» и «не просили». Последнее -
            //не то же самое, что «выключить»: за него отвечает факт ссылки на
            //ASP.NET Core, и решает его CompatBinder, у которого есть
            //компиляция
            bool? web = null;
            if (options.TryGetValue(WebProperty, out var webValue))
            {
                var trimmed = webValue?.Trim();

                if (string.Equals(trimmed, "enable", StringComparison.OrdinalIgnoreCase))
                {
                    web = true;
                }
                else if (string.Equals(trimmed, "disable", StringComparison.OrdinalIgnoreCase))
                {
                    web = false;
                }
            }

            if (!options.TryGetValue(StrictProperty, out var strict) || string.IsNullOrWhiteSpace(strict))
            {
                return new CompatSettings(enabled, null, null, web);
            }

            strict = strict.Trim();

            //«info» - это умолчание, названное вслух. Принимается не из
            //снисходительности: без него свойство нельзя было бы выставить в
            //Directory.Build.props и переопределить в одном проекте обратно
            if (string.Equals(strict, "info", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Info, null, web);
            }

            if (string.Equals(strict, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Warning, null, web);
            }

            if (string.Equals(strict, "error", StringComparison.OrdinalIgnoreCase))
            {
                return new CompatSettings(enabled, DiagnosticSeverity.Error, null, web);
            }

            return new CompatSettings(enabled, null, strict, web);
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
                && Web == other.Web
                && string.Equals(UnrecognizedStrictValue, other.UnrecognizedStrictValue, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => obj is CompatSettings other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Enabled ? 1 : 0;
            hash = unchecked((hash * 31) + (Web is null ? 2 : Web.Value ? 1 : 0));
            hash = unchecked((hash * 31) + (Floor?.GetHashCode() ?? 0));
            return unchecked((hash * 31) + (UnrecognizedStrictValue?.GetHashCode() ?? 0));
        }
    }
}
