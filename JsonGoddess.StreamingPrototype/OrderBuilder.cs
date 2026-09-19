using System;
using System.Buffers;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Накопитель прочитанных элементов.
    ///
    /// <para>
    /// Заведён вместо <c>List&lt;Order&gt;</c> по замеру: список растёт
    /// удвоением, и каждое удвоение - мусор, а потом <c>ToArray</c> копирует
    /// всё ещё раз. На тысяче элементов это ~24 КБ, на десяти тысячах ~211 КБ,
    /// и ровно этим мы были <b>выше</b> эталона по аллокациям при том, что
    /// быстрее его в полтора раза.
    /// </para>
    ///
    /// <para>
    /// Здесь рост идёт по арендованным массивам (<see cref="ArrayPool{T}"/>),
    /// то есть после прогрева не стои́т ни байта, а наружу отдаётся ровно один
    /// массив нужного размера - тот самый, который всё равно обязан быть
    /// результатом.
    /// </para>
    ///
    /// <para>
    /// <c>clearArray: true</c> при возврате обязателен: массив ссылочный, и
    /// невычищенный он держал бы прочитанные объекты живыми до следующей
    /// аренды.
    /// </para>
    /// </summary>
    internal struct OrderBuilder
    {
        private Order[]? _items;

        private int _count;

        internal readonly int Count => _count;

        internal void Add(Order item)
        {
            var items = _items;

            if (items is null)
            {
                items = ArrayPool<Order>.Shared.Rent(64);
                _items = items;
            }
            else if (_count == items.Length)
            {
                var bigger = ArrayPool<Order>.Shared.Rent(items.Length * 2);
                Array.Copy(items, bigger, _count);
                ArrayPool<Order>.Shared.Return(items, clearArray: true);
                _items = bigger;
                items = bigger;
            }

            items[_count++] = item;
        }

        /// <summary>
        /// Отдать результат и вернуть аренду. Зовётся и на успехе, и на отказе -
        /// иначе арендованный массив утекает из пула.
        /// </summary>
        internal Order[] Finish()
        {
            var items = _items;

            if (items is null)
            {
                return Array.Empty<Order>();
            }

            var result = new Order[_count];
            Array.Copy(items, result, _count);

            ArrayPool<Order>.Shared.Return(items, clearArray: true);
            _items = null;
            _count = 0;

            return result;
        }

        internal void Release()
        {
            if (_items is null)
            {
                return;
            }

            ArrayPool<Order>.Shared.Return(_items, clearArray: true);
            _items = null;
            _count = 0;
        }
    }
}
