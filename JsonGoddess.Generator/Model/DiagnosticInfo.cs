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

        public DiagnosticInfo(string id, LocationInfo? location, params string[] arguments)
        {
            Id = id;
            Location = location;
            Arguments = new EquatableArray<string>(arguments);
        }

        public Diagnostic ToDiagnostic()
        {
            var descriptor = Diagnostics.JsonGoddessDiagnostics.Get(Id);

            var arguments = new object[Arguments.Count];
            for (var i = 0; i < Arguments.Count; i++)
            {
                arguments[i] = Arguments[i];
            }

            return Diagnostic.Create(descriptor, Location?.ToLocation(), arguments);
        }

        public bool Equals(DiagnosticInfo other)
        {
            return Id == other.Id
                && Nullable.Equals(Location, other.Location)
                && Arguments.Equals(other.Arguments);
        }

        public override bool Equals(object? obj) => obj is DiagnosticInfo other && Equals(other);

        public override int GetHashCode()
        {
            var hash = Id?.GetHashCode() ?? 0;
            hash = unchecked((hash * 31) + (Location?.GetHashCode() ?? 0));
            return unchecked((hash * 31) + Arguments.GetHashCode());
        }
    }
}
