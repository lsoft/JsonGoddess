using System;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Путь до места отказа при потоковом чтении - в нотации эталона
    /// (<c>$[3].id</c>).
    ///
    /// <para>
    /// Собирается из двух половин, и иначе нельзя (PLAN.md §12.9). Индекс
    /// элемента знает только драйвер; место внутри элемента - только холодный
    /// проход по документу (§6.4), а документа у нас нет: есть окно, которое
    /// кончается где попало и начинается не с начала тела. Поэтому проход идёт
    /// по <b>элементу</b>, а его результат приставляется к приставке, которую
    /// знает драйвер.
    /// </para>
    ///
    /// <para>
    /// Правила приставки сняты пробой у эталона, а не выведены: до открывающей
    /// скобки - корень, между элементами - индекс <b>следующего</b>.
    /// </para>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonStreamPath
    {
        /// <summary>
        /// Отказ <b>внутри</b> значения, начавшегося в окне со смещения
        /// <paramref name="start"/>: холодный проход по нему плюс приставка.
        /// </summary>
        public static JsonDocumentException Inside(
            JsonDocumentException failure,
            ReadOnlySpan<byte> json,
            int start,
            string prefix
            )
        {
            var inside = JsonPath.Decorate(
                new JsonDocumentException(
                    failure.Reason,
                    Math.Max(0, failure.BytePosition - start),
                    failure.Anchor
                    ),
                json.Slice(start)
                );

            var tail = inside.Path is null || inside.Path.Length <= 1
                ? string.Empty
                : inside.Path.Substring(1);

            return new JsonDocumentException(
                failure.Reason,
                failure.BytePosition,
                prefix + tail,
                inside.LineNumber,
                inside.BytePositionInLine,
                failure
                );
        }

        /// <summary>
        /// Отказ <b>вне</b> значения - до него, между значениями, на закрытии.
        /// Путь тут целиком принадлежит драйверу: внутрь мы не заходили.
        /// </summary>
        public static JsonDocumentException Outside(
            JsonDocumentException failure,
            ReadOnlySpan<byte> json,
            string path
            )
        {
            JsonPath.Locate(json, Math.Max(0, failure.BytePosition), out var line, out var column);

            return new JsonDocumentException(
                failure.Reason,
                failure.BytePosition,
                path,
                line,
                column,
                failure
                );
        }

        /// <summary>
        /// Отказ внутри свойства корневого объекта - <c>$.customer</c>.
        ///
        /// <para>
        /// Пройти надо было бы по объекту, а не по свойству: иначе имя свойства
        /// в путь не попадёт. Начала объекта в окне может уже не быть - оно
        /// съедено и отброшено, - поэтому честный ответ собирается иначе: корень
        /// плюс имя, прочитанное с начала самого́ свойства.
        /// </para>
        /// </summary>
        public static JsonDocumentException Property(
            JsonDocumentException failure,
            ReadOnlySpan<byte> json,
            int propertyStart
            )
        {
            JsonPath.Locate(json, Math.Max(0, failure.BytePosition), out var line, out var column);

            var name = NameAt(json, propertyStart);

            return new JsonDocumentException(
                failure.Reason,
                failure.BytePosition,
                name is null ? "$" : "$." + name,
                line,
                column,
                failure
                );
        }

        private static string? NameAt(ReadOnlySpan<byte> json, int start)
        {
            var position = start;

            try
            {
                return JsonTryScan.ReadStringContent(json, ref position, true, out var raw, out var escaped)
                    ? JsonStringDecoder.Decode(raw, escaped)
                    : null;
            }
            catch (JsonDocumentException)
            {
                return null;
            }
        }

        /// <summary>Приставка элемента массива - <c>$[3]</c>.</summary>
        public static string Index(int index)
        {
            return "$[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
        }
    }
}
