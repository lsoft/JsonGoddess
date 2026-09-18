using System;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Найденный вызов фасада <c>JsonGoddess.Compat.JsonSerializer</c> - в виде
    /// строки, а не символа.
    ///
    /// <para>
    /// Символа здесь нет по той же причине, что и в <see cref="HostReference"/>:
    /// триггерный шаг переисполняется только для изменившихся деревьев, его
    /// закэшированные значения приезжают из <b>предыдущей</b> компиляции, а
    /// связывать символы разных компиляций нельзя.
    /// </para>
    ///
    /// <para>
    /// Имя типа записано <b>ссылочным идентификатором документационного
    /// комментария</b> (<c>T:System.Collections.Generic.List{My.Order}</c>), а
    /// не метаданным именем: аргументом обобщённого метода бывает и построенный
    /// generic, и массив, а метаданное имя такого типа назвать не умеет. Тот же
    /// идентификатор умеет и разобраться обратно в символ - в той компиляции, с
    /// которой работает связыватель.
    /// </para>
    /// </summary>
    public readonly struct CompatCallSite : IEquatable<CompatCallSite>
    {
        public readonly string ReferenceId;

        public readonly LocationInfo? Location;

        public CompatCallSite(string referenceId, LocationInfo? location)
        {
            ReferenceId = referenceId;
            Location = location;
        }

        public bool Equals(CompatCallSite other)
        {
            return ReferenceId == other.ReferenceId && Nullable.Equals(Location, other.Location);
        }

        public override bool Equals(object? obj) => obj is CompatCallSite other && Equals(other);

        public override int GetHashCode()
        {
            return unchecked(((ReferenceId?.GetHashCode() ?? 0) * 31) + (Location?.GetHashCode() ?? 0));
        }
    }
}
