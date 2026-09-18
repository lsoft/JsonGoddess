using System;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Описание диагностики в виде значений. Причина та же, что у
    /// <see cref="LocationInfo"/>: сам <see cref="Diagnostic"/> непригоден для
    /// сравнения результатов генерации.
    /// </summary>
    public readonly struct DiagnosticInfo : IEquatable<DiagnosticInfo>
    {
        public readonly string Id;
        public readonly LocationInfo? Location;
        public readonly EquatableArray<string> Arguments;

        /// <summary>
        /// Severity вместо той, что объявлена дескриптором, - или <c>null</c>,
        /// если менять нечего.
        ///
        /// <para>
        /// Существует ради <c>JsonGoddessCompatStrict</c> (§10 плана):
        /// отступление к эталону - объявленное поведение, и по умолчанию это
        /// <c>Info</c>, но проект, которому важно, чтобы ускорилось <b>всё</b>,
        /// вправе потребовать предупреждения или ошибки. Решение при этом не
        /// меняется ни на йоту - меняется только громкость.
        /// </para>
        ///
        /// <para>
        /// Поле входит в <see cref="Equals(DiagnosticInfo)"/> не для порядка:
        /// результат генерации кэшируется по равенству, и без этого правка
        /// свойства в csproj не заставила бы конвейер переиздать диагностику.
        /// </para>
        /// </summary>
        public readonly DiagnosticSeverity? Severity;

        public DiagnosticInfo(string id, LocationInfo? location, params string[] arguments)
            : this(id, location, null, arguments)
        {
        }

        public DiagnosticInfo(
            string id, LocationInfo? location, DiagnosticSeverity? severity, params string[] arguments
            )
        {
            Id = id;
            Location = location;
            Severity = severity;
            Arguments = new EquatableArray<string>(arguments);
        }

        /// <summary>Та же диагностика, но громче (или тише) на одну ступень строгости.</summary>
        public DiagnosticInfo With(DiagnosticSeverity? severity)
        {
            var arguments = new string[Arguments.Count];
            for (var i = 0; i < Arguments.Count; i++)
            {
                arguments[i] = Arguments[i];
            }

            return new DiagnosticInfo(Id, Location, severity, arguments);
        }

        public Diagnostic ToDiagnostic()
        {
            var descriptor = Diagnostics.JsonGoddessDiagnostics.Get(Id);

            var arguments = new object[Arguments.Count];
            for (var i = 0; i < Arguments.Count; i++)
            {
                arguments[i] = Arguments[i];
            }

            var location = Location?.ToLocation();

            return Severity is null
                ? Diagnostic.Create(descriptor, location, arguments)
                : Diagnostic.Create(
                    descriptor,
                    location,
                    Severity.Value,
                    additionalLocations: null,
                    properties: null,
                    messageArgs: arguments
                    );
        }

        public bool Equals(DiagnosticInfo other)
        {
            return Id == other.Id
                && Nullable.Equals(Location, other.Location)
                && Severity == other.Severity
                && Arguments.Equals(other.Arguments);
        }

        public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Id?.GetHashCode() ?? 0;
            hash = unchecked((hash * 31) + (Location?.GetHashCode() ?? 0));
            hash = unchecked((hash * 31) + (Severity?.GetHashCode() ?? 0));
            return unchecked((hash * 31) + Arguments.GetHashCode());
        }
    }
}
