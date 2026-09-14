using System;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Найденный хост - в виде имени, а не символа.
    ///
    /// Символ отсюда убран сознательно. Триггерный шаг переисполняется только
    /// для изменившихся деревьев, поэтому его закэшированные значения приезжают
    /// из <b>предыдущей</b> компиляции, а связывать символы одной компиляции с
    /// другой нельзя. Имя же и позиция от компиляции не зависят, а символ
    /// связыватель достанет заново из той компиляции, с которой работает.
    /// </summary>
    public readonly struct HostReference : IEquatable<HostReference>
    {
        /// <summary>
        /// Метаданное имя хоста: <c>Namespace.Host</c>. Вложенных и
        /// обобщённых хостов фаза 2 не принимает (JGD025), поэтому ни <c>+</c>,
        /// ни арности здесь не бывает.
        /// </summary>
        public readonly string MetadataName;

        public readonly LocationInfo? Location;

        public HostReference(string metadataName, LocationInfo? location)
        {
            MetadataName = metadataName;
            Location = location;
        }

        public bool Equals(HostReference other)
        {
            return MetadataName == other.MetadataName && Nullable.Equals(Location, other.Location);
        }

        public override bool Equals(object? obj) => obj is HostReference other && Equals(other);

        public override int GetHashCode()
        {
            return unchecked(((MetadataName?.GetHashCode() ?? 0) * 31) + (Location?.GetHashCode() ?? 0));
        }
    }
}
