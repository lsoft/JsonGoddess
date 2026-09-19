using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// MSBuild-свойство потокового пути (PLAN.md §12.9, фаза 10).
    ///
    /// <para>
    /// Тип-значение и <c>IEquatable</c> по той же причине, что у
    /// <see cref="CompatSettings"/>: настройка едет по инкрементальному
    /// конвейеру, а он кэширует по равенству.
    /// </para>
    /// </summary>
    public readonly struct StreamingSettings : IEquatable<StreamingSettings>
    {
        public const string EnabledProperty = "build_property.JsonGoddessStreaming";

        public static readonly StreamingSettings Default = new StreamingSettings(null);

        /// <summary>
        /// Печатать ли потоковый читатель.
        ///
        /// <para>
        /// <c>null</c> - «не просили ни того ни другого», и тогда решает
        /// <b>факт</b>: ссылается ли сборка на ASP.NET Core. Потоковый путь -
        /// это второй читатель на каждый обслуженный тип, то есть заметное
        /// удвоение порождаемого кода; консольному приложению он не нужен ни
        /// разу (тело там уже в памяти целиком, и обычный читатель на нём
        /// быстрее), а веб-проекту нужен всегда. Отличить одно от другого можно,
        /// не спрашивая человека, - ровно как с веб-профилем моста.
        /// </para>
        ///
        /// <para>
        /// Явное значение свойства сильнее факта: и <c>enable</c>, и
        /// <c>disable</c> делают ровно то, что написано.
        /// </para>
        /// </summary>
        public readonly bool? Enabled;

        private StreamingSettings(bool? enabled)
        {
            Enabled = enabled;
        }

        public static StreamingSettings Read(AnalyzerConfigOptions options)
        {
            if (!options.TryGetValue(EnabledProperty, out var value))
            {
                return Default;
            }

            var trimmed = value?.Trim();

            if (string.Equals(trimmed, "enable", StringComparison.OrdinalIgnoreCase))
            {
                return new StreamingSettings(true);
            }

            if (string.Equals(trimmed, "disable", StringComparison.OrdinalIgnoreCase))
            {
                return new StreamingSettings(false);
            }

            //значение, которого мы не понимаем, - это "не просили": решает факт
            return Default;
        }

        public bool Equals(StreamingSettings other) => Enabled == other.Enabled;

        public override bool Equals(object? obj) => obj is StreamingSettings other && Equals(other);

        public override int GetHashCode() => Enabled is null ? 0 : Enabled.Value ? 1 : 2;
    }
}
