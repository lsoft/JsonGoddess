using System;
using System.Buffers;

namespace JsonGoddess
{
    /// <summary>
    /// Пишет в арендованный <c>byte[]</c>. Основной sink для случая "нужен
    /// документ целиком в памяти": результат отдаётся спаном без копии, а
    /// <see cref="ToArray"/> копирует ровно записанный префикс, а не весь
    /// арендованный буфер.
    ///
    /// Не потокобезопасен. <c>sealed</c> не для красоты: генератор зовёт sink по
    /// конкретному типу, и запечатанный девиртуализуется в обычный вызов.
    /// </summary>
    public sealed class PooledUtf8Exhauster : Utf8ExhausterBase, IDisposable
    {
        public const int DefaultCapacity = 1024;

        private byte[]? _buffer;
        private int _written;

        public PooledUtf8Exhauster()
            : this(DefaultCapacity)
        {
        }

        public PooledUtf8Exhauster(int capacity)
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
                var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledUtf8Exhauster));
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
            Array.Copy(_buffer ?? throw new ObjectDisposedException(nameof(PooledUtf8Exhauster)), 0, result, 0, _written);
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

        protected override Span<byte> GetSpan(int sizeHint)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledUtf8Exhauster));
            if (buffer.Length - _written < sizeHint)
            {
                buffer = Grow(sizeHint);
            }

            return new Span<byte>(buffer, _written, buffer.Length - _written);
        }

        protected override void Advance(int count)
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
