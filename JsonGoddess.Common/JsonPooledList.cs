using System;
using System.Buffers;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Накопитель элементов корневой коллекции при потоковом чтении.
    ///
    /// <para>
    /// Заведён вместо <see cref="System.Collections.Generic.List{T}"/> по
    /// замеру (PLAN.md §12.9): список растёт удвоением, и каждое удвоение -
    /// мусор, а потом <c>ToArray</c> копирует всё ещё раз. На тысяче элементов
    /// это ~24 КБ, на десяти тысячах ~211 КБ - ровно этим потоковый путь был
    /// <b>выше</b> эталона по аллокациям при том, что быстрее его в полтора
    /// раза.
    /// </para>
    ///
    /// <para>
    /// Здесь рост идёт по арендованным массивам, то есть после прогрева не
    /// стои́т ни байта, а наружу отдаётся ровно один массив нужного размера -
    /// тот самый, который всё равно обязан быть результатом.
    /// </para>
    ///
    /// <para>
    /// <c>clearArray: true</c> при возврате обязателен, когда <typeparamref name="T"/>
    /// ссылочный: невычищенный массив держал бы прочитанные объекты живыми до
    /// следующей аренды. Проверять это на каждом возврате незачем - JIT свернёт
    /// условие на компиляции типа.
    /// </para>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public struct JsonPooledList<T>
    {
        private const int FirstRent = 64;

        private T[]? _items;

        private int _count;

        public readonly int Count => _count;

        public void Add(T item)
        {
            var items = _items;

            if (items is null)
            {
                items = ArrayPool<T>.Shared.Rent(FirstRent);
                _items = items;
            }
            else if (_count == items.Length)
            {
                var bigger = ArrayPool<T>.Shared.Rent(items.Length * 2);
                Array.Copy(items, bigger, _count);
                ArrayPool<T>.Shared.Return(items, clearArray: Clears);
                _items = bigger;
                items = bigger;
            }

            items[_count++] = item;
        }

        /// <summary>Отдать результат и вернуть аренду.</summary>
        public T[] Finish()
        {
            var items = _items;

            if (items is null)
            {
                return Array.Empty<T>();
            }

            var result = new T[_count];
            Array.Copy(items, result, _count);

            ArrayPool<T>.Shared.Return(items, clearArray: Clears);
            _items = null;
            _count = 0;

            return result;
        }

        /// <summary>
        /// То же, но в <see cref="System.Collections.Generic.List{T}"/> -
        /// когда корнем запроса объявлен именно он. Ёмкость известна точно,
        /// поэтому список не растёт ни разу.
        /// </summary>
        public System.Collections.Generic.List<T> FinishAsList()
        {
            var items = _items;
            var result = new System.Collections.Generic.List<T>(_count);

            if (items is null)
            {
                return result;
            }

            for (var i = 0; i < _count; i++)
            {
                result.Add(items[i]);
            }

            ArrayPool<T>.Shared.Return(items, clearArray: Clears);
            _items = null;
            _count = 0;

            return result;
        }

        /// <summary>Вернуть аренду, ничего не отдавая, - путь отказа.</summary>
        public void Release()
        {
            if (_items is null)
            {
                return;
            }

            ArrayPool<T>.Shared.Return(_items, clearArray: Clears);
            _items = null;
            _count = 0;
        }

        private static bool Clears =>
#if NET6_0_OR_GREATER
            System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#else
            !typeof(T).IsPrimitive;
#endif
    }
}
