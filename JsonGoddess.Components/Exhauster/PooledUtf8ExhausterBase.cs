using System;
using System.Buffers;

namespace JsonGoddess
{
    /// <summary>
    /// Буфер из пула - общая часть sink'ов, пишущих документ целиком в память.
    ///
    /// <para>
    /// Выделена не ради стройности, а потому что листьев стало два:
    /// <see cref="PooledUtf8Exhauster"/> и <see cref="CompatUtf8Exhauster"/>.
    /// Различаются они <b>только</b> набором экранируемого; аренда, рост и
    /// возврат буфера у них одни и те же, и дублировать их значило бы
    /// разъехаться в первый же месяц - ровно тем же доводом, каким заведён
    /// <see cref="Utf8ExhausterBase"/>.
    /// </para>
    ///
    /// <para>
    /// Оба листа <c>sealed</c>, и это существенно: генератор печатает вызовы по
    /// конкретному типу sink'а, и запечатанный лист девиртуализуется в обычный
    /// вызов. Абстрактная середина этому не мешает.
    /// </para>
    ///
    /// <para>
    /// Не потокобезопасен.
    /// </para>
    /// </summary>
    public abstract class PooledUtf8ExhausterBase : Utf8ExhausterBase, IDisposable
    {
        public const int DefaultCapacity = 1024;

        private byte[]? _buffer;
        private int _written;

        protected PooledUtf8ExhausterBase(int capacity)
        {
            if (capacity <= 0)
            {
                capacity = DefaultCapacity;
            }

            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
            _written = 0;
        }

        /// <summary>
        /// Записанное, без копии.
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan
        {
            get
            {
                var buffer = _buffer ?? throw new ObjectDisposedException(GetType().Name);
                return new ReadOnlySpan<byte>(buffer, 0, _written);
            }
        }

        public int WrittenCount
        {
            get => _written;
        }

        public byte[] ToArray()
        {
            var result = new byte[_written];
            Array.Copy(_buffer ?? throw new ObjectDisposedException(GetType().Name), 0, result, 0, _written);
            return result;
        }

        /// <summary>
        /// Документ как <see cref="string"/>. Нужен тестам и человеку, а не
        /// горячему пути: JSON и рождается, и уезжает байтами.
        /// </summary>
        public override string ToString()
        {
            return Internal.JsonStringDecoder.Decode(WrittenSpan, false);
        }

        /// <summary>
        /// Сбрасывает записанное, сохраняя арендованный буфер. Для
        /// переиспользования sink'а в цикле.
        /// </summary>
        public void Reset()
        {
            _written = 0;
        }

        public void Dispose()
        {
            var buffer = _buffer;
            if (buffer is not null)
            {
                _buffer = null;
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected sealed override Span<byte> GetSpan(int sizeHint)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(GetType().Name);
            if (buffer.Length - _written < sizeHint)
            {
                buffer = Grow(sizeHint);
            }

            return new Span<byte>(buffer, _written, buffer.Length - _written);
        }

        protected sealed override void Advance(int count)
        {
            _written += count;
        }

        private byte[] Grow(int sizeHint)
        {
            var current = _buffer!;
            var required = _written + sizeHint;
            var capacity = current.Length;
            while (capacity < required)
            {
                capacity *= 2;
            }

            var next = ArrayPool<byte>.Shared.Rent(capacity);
            Array.Copy(current, 0, next, 0, _written);
            _buffer = next;
            ArrayPool<byte>.Shared.Return(current);
            return next;
        }
    }
}
