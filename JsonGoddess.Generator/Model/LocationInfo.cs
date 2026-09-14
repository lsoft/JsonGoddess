using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Позиция в исходнике, из которой выброшен <see cref="Location"/>.
    ///
    /// Сам <see cref="Location"/> тащит за собой синтаксическое дерево, то есть
    /// всю компиляцию: положив его в результат генерации, мы удерживали бы в
    /// кэше Roslyn'а предыдущую компиляцию целиком и сравнивали бы результаты
    /// по ссылке. Здесь остаются только строки и числа.
    /// </summary>
    public readonly struct LocationInfo : IEquatable<LocationInfo>
    {
        public readonly string FilePath;
        public readonly TextSpan TextSpan;
        public readonly LinePositionSpan LineSpan;

        public LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
        {
            FilePath = filePath;
            TextSpan = textSpan;
            LineSpan = lineSpan;
        }

        public static LocationInfo? From(Location? location)
        {
            if (location is null || location.SourceTree is null)
            {
                return null;
            }

            return new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span
                );
        }

        public static LocationInfo? From(ISymbol symbol)
        {
            foreach (var reference in symbol.Locations)
            {
                var info = From(reference);
                if (info.HasValue)
                {
                    return info;
                }
            }

            return null;
        }

        public Location ToLocation()
        {
            return Location.Create(FilePath, TextSpan, LineSpan);
        }

        public bool Equals(LocationInfo other)
        {
            return FilePath == other.FilePath
                && TextSpan == other.TextSpan
                && LineSpan.Equals(other.LineSpan);
        }

        public override bool Equals(object? obj) => obj is LocationInfo other && Equals(other);

        public override int GetHashCode()
        {
            var hash = FilePath?.GetHashCode() ?? 0;
            hash = unchecked((hash * 31) + TextSpan.GetHashCode());
            return unchecked((hash * 31) + LineSpan.GetHashCode());
        }
    }
}
