using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Массив со сравнением по значению.
    ///
    /// Нужен потому, что <see cref="ImmutableArray{T}"/> сравнивается по
    /// ссылке на внутренний массив: два результата генерации с одинаковым
    /// содержимым оказались бы неравны, выходной шаг конвейера переисполнялся
    /// бы на каждую правку, и вся схема §9 плана - "кэш берётся из равенства
    /// выходов" - не работала бы, никак себя при этом не проявляя.
    /// </summary>
    public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
        where T : IEquatable<T>
    {
        public static readonly EquatableArray<T> Empty = new EquatableArray<T>(ImmutableArray<T>.Empty);

        private readonly ImmutableArray<T> _items;

        public EquatableArray(ImmutableArray<T> items)
        {
            _items = items;
        }

        public EquatableArray(IEnumerable<T> items)
        {
            _items = ImmutableArray.CreateRange(items);
        }

        public int Count => _items.IsDefault ? 0 : _items.Length;

        public T this[int index] => _items[index];

        public bool Equals(EquatableArray<T> other)
        {
            if (_items.IsDefault || other._items.IsDefault)
            {
                return _items.IsDefault && other._items.IsDefault;
            }

            if (_items.Length != other._items.Length)
            {
                return false;
            }

            for (var i = 0; i < _items.Length; i++)
            {
                if (!_items[i].Equals(other._items[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is EquatableArray<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            if (_items.IsDefault)
            {
                return 0;
            }

            var hash = 17;
            for (var i = 0; i < _items.Length; i++)
            {
                hash = unchecked((hash * 31) + _items[i].GetHashCode());
            }

            return hash;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_items.IsDefault)
            {
                yield break;
            }

            for (var i = 0; i < _items.Length; i++)
            {
                yield return _items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
