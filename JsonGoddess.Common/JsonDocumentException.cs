using System;

namespace JsonGoddess
{
    /// <summary>
    /// Документ не разобран. Наследник <see cref="InvalidOperationException"/>
    /// намеренно: так его ловит код, написанный под
    /// <c>System.Text.Json.JsonException</c>, который тоже наследник
    /// <see cref="InvalidOperationException"/>. Разбор чужого документа обязан
    /// кончаться исключением этого семейства либо
    /// <see cref="FormatException"/> - но никогда не выходом за границы буфера.
    /// </summary>
    public class JsonDocumentException : InvalidOperationException
    {
        /// <summary>
        /// Смещение в байтах от начала документа, где разбор встал.
        /// -1, если позиция неизвестна.
        /// </summary>
        public int BytePosition
        {
            get;
        }

        public JsonDocumentException(string message)
            : this(message, -1)
        {
        }

        public JsonDocumentException(string message, int bytePosition)
            : base(bytePosition >= 0 ? message + " (byte " + bytePosition.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : message)
        {
            BytePosition = bytePosition;
        }
    }
}
