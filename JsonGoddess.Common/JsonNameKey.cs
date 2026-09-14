using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Имя свойства, упакованное в одно 64-битное число: старший байт - длина,
    /// младшие семь - первые семь байт имени.
    ///
    /// Приём взят у <c>System.Text.Json</c> (<c>PropertyRef.GetKey</c>), но
    /// используется иначе. Им ключ нужен, чтобы линейно пройти рантайм-кэш
    /// найденных свойств и при промахе уйти в словарь, раскодировав имя в
    /// UTF-16; у нас по ключу стоит <c>switch</c> с константами, известными
    /// генератору, то есть таблица переходов - ни кэша, ни промахов, ни словаря.
    ///
    /// Главное следствие раскладки: <b>для имени длиной до семи байт ключ
    /// полон</b>. Длина внутри ключа, все байты внутри ключа - значит совпадение
    /// ключа доказывает совпадение имени, и сравнения байтов не нужно вовсе.
    /// Для имени от восьми байт ключ - только префильтр, и
    /// <c>SequenceEqual</c> обязателен.
    ///
    /// Две вещи, о которых обязан помнить генератор:
    /// <list type="number">
    /// <item><b>Порядок байтов задан явно.</b> У STJ этой заботы нет - они
    /// считают ключ в рантайме, обе стороны сравнения всегда на одной машине.
    /// У нас константу печатает генератор на сборочной машине, а сравнивает
    /// целевая, поэтому здесь <see cref="BinaryPrimitives"/> с явным
    /// little-endian, а не <c>BitConverter</c>.</item>
    /// <item><b>Ключи двух имён могут совпасть.</b> Два имени от восьми байт с
    /// одинаковой длиной и одинаковыми первыми семью байтами дают один ключ, а
    /// двух <c>case</c> с одной константой в C# не бывает. Такие имена
    /// генератор обязан собрать в одну ветку и разделить внутри неё
    /// сравнением. Для имён до семи байт столкновений не бывает по
    /// построению.</item>
    /// </list>
    /// Длина кладётся в байт, поэтому имена длиной 256 и 0 неотличимы по этому
    /// полю - безвредно: у любого имени от восьми байт ключ и так лишь
    /// префильтр.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonNameKey
    {
        /// <summary>
        /// До этой длины включительно ключ содержит имя целиком, и сравнение
        /// байтов не требуется.
        /// </summary>
        public const int MaxExactLength = 7;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Compute(scoped ReadOnlySpan<byte> name)
        {
            var length = name.Length;
            var key = (ulong)(byte)length << 56;

            switch (length)
            {
                case 0:
                    return key;
                case 1:
                    return key | name[0];
                case 2:
                    return key | BinaryPrimitives.ReadUInt16LittleEndian(name);
                case 3:
                    return key | BinaryPrimitives.ReadUInt16LittleEndian(name) | ((ulong)name[2] << 16);
                case 4:
                    return key | BinaryPrimitives.ReadUInt32LittleEndian(name);
                case 5:
                    return key | BinaryPrimitives.ReadUInt32LittleEndian(name) | ((ulong)name[4] << 32);
                case 6:
                    return key
                        | BinaryPrimitives.ReadUInt32LittleEndian(name)
                        | ((ulong)BinaryPrimitives.ReadUInt16LittleEndian(name.Slice(4, 2)) << 32);
                case 7:
                    return key
                        | BinaryPrimitives.ReadUInt32LittleEndian(name)
                        | ((ulong)BinaryPrimitives.ReadUInt16LittleEndian(name.Slice(4, 2)) << 32)
                        | ((ulong)name[6] << 48);
                default:
                    return key | (BinaryPrimitives.ReadUInt64LittleEndian(name) & 0x00FF_FFFF_FFFF_FFFFUL);
            }
        }
    }
}
