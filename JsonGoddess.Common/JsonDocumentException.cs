using System;
using System.Globalization;

namespace JsonGoddess
{
    /// <summary>
    /// Документ не разобран. Наследник <see cref="InvalidOperationException"/>
    /// намеренно: так его ловит код, написанный под
    /// <c>System.Text.Json.JsonException</c>, который тоже наследник
    /// <see cref="InvalidOperationException"/>. Разбор чужого документа обязан
    /// кончаться исключением этого семейства либо
    /// <see cref="FormatException"/> - но никогда не выходом за границы буфера.
    ///
    /// <para>
    /// Сообщение бывает двух видов, и какой достанется - решает хост (§6.4).
    /// Хост без стражей получает короткое: причина и смещение. Хост с любым
    /// стражем - разложенное, как у эталона: причина, путь, строка и смещение
    /// внутри строки. Путь стоит холодного прохода по документу, и платит за
    /// него только тот, кто уже попросил отказывать строже.
    /// </para>
    /// </summary>
    public class JsonDocumentException : InvalidOperationException
    {
        /// <summary>
        /// Причина без приписок - то, что в сообщении стоит до <c>Path:</c>.
        /// Хранится отдельно, потому что украшение пути собирает сообщение
        /// заново, а разбирать своё же сообщение обратно - худший способ это
        /// сделать.
        /// </summary>
        public string Reason
        {
            get;
        }

        /// <summary>
        /// Смещение в байтах от начала документа, где разбор встал.
        /// -1, если позиция неизвестна.
        /// </summary>
        public int BytePosition
        {
            get;
        }

        /// <summary>
        /// Чем именно является <see cref="BytePosition"/> - см.
        /// <see cref="JsonPathAnchor"/>. Читается только при построении пути.
        /// </summary>
        public JsonPathAnchor Anchor
        {
            get;
        }

        /// <summary>
        /// Путь до места отказа в нотации STJ - <c>$.orders[3].id</c>.
        /// <c>null</c> у хоста без стражей: путь ему не строился.
        /// </summary>
        public string? Path
        {
            get;
        }

        /// <summary>
        /// Номер строки с нуля, как у <c>JsonException</c> эталона.
        /// -1, когда путь не строился.
        /// </summary>
        public int LineNumber
        {
            get;
        }

        /// <summary>
        /// Смещение в байтах внутри строки, с нуля, как у эталона.
        /// -1, когда путь не строился.
        /// </summary>
        public int BytePositionInLine
        {
            get;
        }

        public JsonDocumentException(string message)
            : this(message, -1)
        {
        }

        public JsonDocumentException(string message, int bytePosition)
            : this(message, bytePosition, JsonPathAnchor.AtByte)
        {
        }

        public JsonDocumentException(string message, int bytePosition, JsonPathAnchor anchor)
            : base(Short(message, bytePosition))
        {
            Reason = message;
            BytePosition = bytePosition;
            Anchor = anchor;
            Path = null;
            LineNumber = -1;
            BytePositionInLine = -1;
        }

        /// <summary>
        /// Тот же отказ, но с путём. Зовётся только из точки входа хоста со
        /// стражами и только на пути отказа.
        /// </summary>
        public JsonDocumentException(
            string reason, int bytePosition, string path, int lineNumber, int bytePositionInLine, Exception? inner
            )
            : base(Full(reason, path, lineNumber, bytePositionInLine), inner)
        {
            Reason = reason;
            BytePosition = bytePosition;
            Anchor = JsonPathAnchor.AtByte;
            Path = path;
            LineNumber = lineNumber;
            BytePositionInLine = bytePositionInLine;
        }

        private static string Short(string message, int bytePosition)
        {
            return bytePosition >= 0
                ? message + " (byte " + bytePosition.ToString(CultureInfo.InvariantCulture) + ")"
                : message;
        }

        private static string Full(string reason, string path, int lineNumber, int bytePositionInLine)
        {
            return reason
                + " Path: " + path
                + " | LineNumber: " + lineNumber.ToString(CultureInfo.InvariantCulture)
                + " | BytePositionInLine: " + bytePositionInLine.ToString(CultureInfo.InvariantCulture)
                + ".";
        }
    }
}
