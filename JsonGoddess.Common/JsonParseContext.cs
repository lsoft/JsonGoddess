using System;
using System.Buffers;
using JsonGoddess.Internal;

namespace JsonGoddess
{
    /// <summary>
    /// Контекст одного разбора: всё, что коду разбора нужно знать помимо самой
    /// лексемы.
    ///
    /// Это ref struct и это один параметр, а не несколько, намеренно. Урок
    /// XmlSerDe: добавленный параметр ломает все перегрузки <c>Parse</c> разом,
    /// добавленное поле контекста - ни одной. Поэтому всё, что появится потом
    /// (путь для сообщения об ошибке, флаги фич, счётчик глубины), приедет
    /// сюда полем.
    /// </summary>
    public ref struct JsonParseContext
    {
        /// <summary>
        /// Документ целиком. Нужен для сообщения об ошибке: лексема сама по
        /// себе не знает, где она стояла.
        /// </summary>
        public readonly ReadOnlySpan<byte> Document;

        /// <summary>
        /// Позиция, с которой началась текущая лексема. Смысл имеет только на
        /// пути отказа; на счастливом пути её никто не читает.
        /// </summary>
        public int TokenStart;

        /// <summary>
        /// Буфер под разэкранированное имя свойства. Арендуется <b>лениво</b>:
        /// в документе, где экранированных имён нет, он так и остаётся
        /// <c>null</c>, и вся поддержка стоит одно поле ссылочного типа,
        /// обнулённое один раз на весь разбор.
        ///
        /// Он живёт в контексте, а не на стеке читателя, именно ради этого:
        /// <c>stackalloc</c> фиксированного размера JIT выносит в пролог метода
        /// и (без <c>SkipLocalsInit</c>) обнуляет на каждом вызове - то есть на
        /// каждый прочитанный объект, независимо от того, нужен он был или нет.
        /// </summary>
        private byte[]? _nameBuffer;

        /// <summary>
        /// Буфер под разэкранированное <b>значение</b>, которое сравнивается в
        /// байтах, - сейчас это имя члена enum'а.
        ///
        /// Он отдельный от <see cref="_nameBuffer"/> не из щедрости: имя
        /// свойства живёт до конца диспетчеризации, а значение читается внутри
        /// неё, и один буфер на двоих означал бы, что чтение значения затирает
        /// имя, по которому в него попали. Сегодня это было бы безвредно -
        /// после тела члена имя никто не читает, - и ровно поэтому опасно:
        /// такая связь не видна на месте и ломается перестановкой строк.
        /// </summary>
        private byte[]? _valueBuffer;

        public JsonParseContext(ReadOnlySpan<byte> document)
        {
            Document = document;
            TokenStart = 0;
            _nameBuffer = null;
            _valueBuffer = null;
        }

        /// <summary>
        /// Разэкранированное имя свойства в UTF-8 - в той же форме, в которой с
        /// ним сравнивает диспетчер.
        ///
        /// Вызывается только тогда, когда имя приехало экранированным <b>и</b>
        /// не совпало ни с одним литералом, то есть на пути, которого в обычном
        /// документе не бывает вовсе.
        /// </summary>
        public ReadOnlySpan<byte> UnescapeName(scoped ReadOnlySpan<byte> raw)
        {
            _nameBuffer = Ensure(_nameBuffer, raw.Length);

            var written = JsonNameUnescape.Decode(raw, _nameBuffer);
            return new ReadOnlySpan<byte>(_nameBuffer, 0, written);
        }

        /// <summary>
        /// Разэкранированное значение в UTF-8 - в той же форме, в которой с ним
        /// сравнивает порождённый код. Как и с именем, вызывается только тогда,
        /// когда значение приехало экранированным.
        /// </summary>
        public ReadOnlySpan<byte> UnescapeValue(scoped ReadOnlySpan<byte> raw)
        {
            _valueBuffer = Ensure(_valueBuffer, raw.Length);

            var written = JsonNameUnescape.Decode(raw, _valueBuffer);
            return new ReadOnlySpan<byte>(_valueBuffer, 0, written);
        }

        /// <summary>
        /// Буфер не меньше нужного. Возвращает массив, а не принимает его по
        /// <c>ref</c>: поле ref-структуры, переданное по ссылке, закрывает
        /// компилятору возможность доказать, что спан не переживёт контекст, -
        /// и он отказывается, справедливо.
        /// </summary>
        private static byte[] Ensure(byte[]? buffer, int length)
        {
            //разэкранирование никогда не удлиняет: \uXXXX - шесть байт на входе
            //и максимум три на выходе
            if (buffer is not null && buffer.Length >= length)
            {
                return buffer;
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return ArrayPool<byte>.Shared.Rent(length);
        }

        /// <summary>
        /// Возвращает арендованное в пул. Вызывается точкой входа один раз на
        /// документ; не вызвать её - не ошибка корректности, но пул от этого
        /// беднеет.
        /// </summary>
        public void Release()
        {
            if (_nameBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_nameBuffer);
                _nameBuffer = null;
            }

            if (_valueBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_valueBuffer);
                _valueBuffer = null;
            }
        }
    }
}
