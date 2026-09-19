using System;
using System.IO;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// Поток, отдающий по куску за раз.
    ///
    /// <para>
    /// Существует затем, чтобы читатель эталона действительно оказался в
    /// многосегментном режиме. С <c>MemoryStream</c> он вычитывает документ
    /// целиком и работает по непрерывной памяти - то есть форма, ради которой
    /// мост и писался, не замерялась бы вовсе, а медленный путь имён не
    /// исполнялся бы ни разу.
    /// </para>
    /// </summary>
    internal sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        public ChunkedStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var left = _data.Length - _position;
            var take = Math.Min(Math.Min(count, _chunk), left);
            Array.Copy(_data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
