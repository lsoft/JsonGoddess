using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Путь до места отказа в нотации STJ - <c>$.orders[3].id</c> (§6.4).
    ///
    /// <para>
    /// Путь собирается <b>холодным проходом по документу от нулевого байта</b>,
    /// а не накапливается стеком по ходу разбора. Это расхождение с исходным
    /// замыслом плана («сегменты собираются из литералов, известных генератору,
    /// при раскрутке»), и причина у него одна: раскрутка потребовала бы
    /// <c>try/catch</c> в каждом порождённом <c>Read_</c> и
    /// <c>ReadCollection_</c>, то есть EH-область вокруг тела цикла - ровно в
    /// тех методах, ради скорости которых всё и затевалось. Проход по документу
    /// не стоит на счастливом пути <b>ничего</b>: порождённый читатель о путях
    /// не знает вовсе, а единственный <c>catch</c> стоит в точке входа, где
    /// <c>try/finally</c> и так уже есть.
    /// </para>
    ///
    /// <para>
    /// Смещение отказа приезжает бесплатно по той же причине: <c>position</c> -
    /// локальная переменная точки входа, которую все читатели правят по
    /// <c>ref</c>, так что к моменту, когда исключение долетело до
    /// <c>catch</c>, она уже показывает, где разбор встал.
    /// </para>
    ///
    /// <para>
    /// Расхождение с эталоном одно и оно осознанное: путь у нас отражает
    /// <b>структуру документа</b>, а у STJ - стек конвертеров. Внутрь
    /// незнакомого свойства STJ не заходит (<c>$.unknown</c> и точка), а мы
    /// назовём место поточнее (<c>$.unknown.a[2]</c>). Записано в
    /// docs/stj-divergences.md.
    /// </para>
    /// </summary>
    public static class JsonPath
    {
        /// <summary>
        /// Символы, из-за которых STJ берёт сегмент пути в скобки:
        /// <c>$['a.b']</c> вместо <c>$.a.b</c>. Набор установлен пробой по всем
        /// печатным ASCII и подозрительным неASCII, а не выведен из общих
        /// соображений: <c>\0</c>, <c>\a</c>, U+001F, NBSP и BOM в него,
        /// вопреки ожиданию, <b>не</b> входят.
        ///
        /// Содержимое скобок эталон не экранирует вовсе - имя с апострофом
        /// даёт неоднозначный <c>$['a'b']</c>. Повторяем как есть: совместимость
        /// важнее аккуратности, а разбирать этот путь обратно всё равно некому.
        /// </summary>
        private static bool IsSpecial(char c)
        {
            //U+0085 (NEL), U+2028 и U+2029 - последние три из набора; записаны
            //числами, а не литералами, потому что два последних сам компилятор
            //C# считает переводом строки и внутри литерала не терпит
            if (c == (char)0x0085 || c == (char)0x2028 || c == (char)0x2029)
            {
                return true;
            }

            switch (c)
            {
                case '.':
                case ' ':
                case '\'':
                case '/':
                case '"':
                case '[':
                case ']':
                case '(':
                case ')':
                case '\\':
                case '\t':
                case '\n':
                case '\r':
                case '\f':
                case '\b':
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Путь до места отказа. Не бросает никогда: его зовут уже на пути
        /// отказа, и исключение отсюда подменило бы настоящую причину.
        /// </summary>
        /// <param name="document">Документ целиком.</param>
        /// <param name="position">Смещение отказа.</param>
        /// <param name="anchor">Чем это смещение является - см. <see cref="JsonPathAnchor"/>.</param>
        public static string Build(scoped ReadOnlySpan<byte> document, int position, JsonPathAnchor anchor)
        {
            try
            {
                return Walk(document, position, anchor);
            }
            catch (Exception)
            {
                //холодный путь поверх заведомо битого документа: если проход
                //всё-таки споткнулся, корень - честный ответ, а подмена
                //настоящего отказа своим - нет
                return "$";
            }
        }

        /// <summary>
        /// Номер строки (с нуля) и смещение внутри неё (в байтах, с нуля) - в
        /// той же разбивке, которой отвечает <c>JsonException</c> эталона.
        /// </summary>
        public static void Locate(
            scoped ReadOnlySpan<byte> document, int position, out int lineNumber, out int bytePositionInLine
            )
        {
            var limit = Clamp(position, document.Length);
            var line = 0;
            var lineStart = 0;

            for (var i = 0; i < limit; i++)
            {
                if (document[i] == (byte)'\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            lineNumber = line;
            bytePositionInLine = limit - lineStart;
        }

        private sealed class Frame
        {
            public bool IsArray;

            /// <summary>Сколько элементов массива дочитано целиком.</summary>
            public int Index;

            /// <summary>Имя свойства объекта, значение которого читается сейчас.</summary>
            public string? Name;

            /// <summary>
            /// Имя прочитано, но двоеточия за ним ещё не было. В путь такое имя
            /// не попадает: эталон на обрезанном <c>{"orders":[{"id"</c>
            /// называет <c>$.orders[0]</c>, а не <c>$.orders[0].id</c> -
            /// свойство считается начатым только с двоеточия (пробой).
            /// </summary>
            public string? Pending;

            /// <summary>Следующая строка в этом объекте - имя, а не значение.</summary>
            public bool ExpectingName;
        }

        private enum Step
        {
            OpenObject,
            OpenArray,
            CloseObject,
            CloseArray,
            Comma,
            Colon,
            Name,
            Value,
            Stop,
        }

        private static string Walk(scoped ReadOnlySpan<byte> document, int position, JsonPathAnchor anchor)
        {
            var limit = Clamp(position, document.Length);
            var frames = new List<Frame>();
            var p = 0;

            while (true)
            {
                SkipTrivia(document, ref p, limit);
                if (p >= limit)
                {
                    break;
                }

                var step = Classify(document, p, frames, out var end, out var name);
                if (step == Step.Stop || end > limit)
                {
                    //лексема накрыла собой место отказа: отказ внутри неё, и
                    //дочитанной она не считается
                    break;
                }

                if (end == limit)
                {
                    if (anchor == JsonPathAnchor.AfterToken)
                    {
                        break;
                    }

                    if (anchor == JsonPathAnchor.EnclosingObject && step == Step.CloseObject)
                    {
                        ClearName(frames);
                        break;
                    }
                }

                Apply(frames, step, name);
                p = end;
            }

            return Render(frames);
        }

        /// <summary>
        /// Что за лексема стоит на <paramref name="p"/>, где она кончается и -
        /// для строки в позиции имени - как это имя выглядит.
        /// </summary>
        private static Step Classify(
            scoped ReadOnlySpan<byte> document, int p, List<Frame> frames, out int end, out string? name
            )
        {
            name = null;
            end = p + 1;

            switch (document[p])
            {
                case (byte)'{':
                    return Step.OpenObject;

                case (byte)'[':
                    return Step.OpenArray;

                case (byte)'}':
                    return Step.CloseObject;

                case (byte)']':
                    return Step.CloseArray;

                case (byte)',':
                    return Step.Comma;

                case (byte)':':
                    return Step.Colon;

                case (byte)'"':
                {
                    if (!ScanString(document, p, out end, out var content))
                    {
                        return Step.Stop;
                    }

                    var top = Top(frames);
                    if (top is not null && !top.IsArray && top.ExpectingName)
                    {
                        name = Decode(document, content);
                        return Step.Name;
                    }

                    return Step.Value;
                }

                default:
                    end = ScanPrimitive(document, p);
                    return Step.Value;
            }
        }

        private static void Apply(List<Frame> frames, Step step, string? name)
        {
            switch (step)
            {
                case Step.OpenObject:
                    frames.Add(new Frame { IsArray = false, ExpectingName = true, });
                    break;

                case Step.OpenArray:
                    frames.Add(new Frame { IsArray = true, });
                    break;

                case Step.CloseObject:
                case Step.CloseArray:
                    if (frames.Count > 0)
                    {
                        frames.RemoveAt(frames.Count - 1);
                    }

                    //закрытая скобка - это дочитанное значение для того, кто
                    //снаружи: элемент массива кончился
                    ValueCompleted(frames);
                    break;

                case Step.Comma:
                {
                    //в массиве запятая не двигает индекс: он уже сдвинулся,
                    //когда элемент дочитался. Иначе `[{"a":1}@]` назвал бы
                    //нулевой элемент, а эталон называет первый
                    var top = Top(frames);
                    if (top is not null && !top.IsArray)
                    {
                        top.Name = null;
                        top.Pending = null;
                        top.ExpectingName = true;
                    }

                    break;
                }

                case Step.Colon:
                {
                    var top = Top(frames);
                    if (top is not null && !top.IsArray)
                    {
                        top.Name = top.Pending;
                        top.Pending = null;
                        top.ExpectingName = false;
                    }

                    break;
                }

                case Step.Name:
                {
                    var top = Top(frames);
                    if (top is not null)
                    {
                        top.Pending = name;
                    }

                    break;
                }

                case Step.Value:
                    ValueCompleted(frames);
                    break;
            }
        }

        /// <summary>
        /// Значение дочитано. Имя свойства при этом <b>не</b> сбрасывается - его
        /// сбрасывает запятая: эталон на обрезанном <c>{"orders":[{"id":1</c>
        /// называет <c>$.orders[0].id</c>, а не <c>$.orders[0]</c>.
        /// </summary>
        private static void ValueCompleted(List<Frame> frames)
        {
            var top = Top(frames);
            if (top is not null && top.IsArray)
            {
                top.Index++;
            }
        }

        private static void ClearName(List<Frame> frames)
        {
            var top = Top(frames);
            if (top is not null)
            {
                top.Name = null;
                top.Pending = null;
            }
        }

        private static Frame? Top(List<Frame> frames)
        {
            return frames.Count > 0 ? frames[frames.Count - 1] : null;
        }

        private static string Render(List<Frame> frames)
        {
            var builder = new StringBuilder("$");

            foreach (var frame in frames)
            {
                if (frame.IsArray)
                {
                    builder.Append('[');
                    builder.Append(frame.Index.ToString(CultureInfo.InvariantCulture));
                    builder.Append(']');
                    continue;
                }

                if (frame.Name is null)
                {
                    continue;
                }

                if (NeedsBrackets(frame.Name))
                {
                    builder.Append("['").Append(frame.Name).Append("']");
                }
                else
                {
                    builder.Append('.').Append(frame.Name);
                }
            }

            return builder.ToString();
        }

        private static bool NeedsBrackets(string name)
        {
            for (var i = 0; i < name.Length; i++)
            {
                if (IsSpecial(name[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Пробелы и - на случай хоста с <c>JsonFeature.Comments</c> -
        /// комментарии. Проход обязан уметь всё, что умеет читатель: иначе на
        /// документе с комментарием путь развалится именно тогда, когда он
        /// нужен.
        /// </summary>
        private static void SkipTrivia(scoped ReadOnlySpan<byte> document, ref int p, int limit)
        {
            while (p < limit)
            {
                var b = document[p];
                if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                {
                    p++;
                    continue;
                }

                if (b != (byte)'/' || p + 1 >= limit)
                {
                    return;
                }

                var next = document[p + 1];
                if (next == (byte)'/')
                {
                    p += 2;
                    while (p < limit && document[p] != (byte)'\n')
                    {
                        p++;
                    }

                    continue;
                }

                if (next == (byte)'*')
                {
                    p += 2;
                    while (p + 1 < limit && !(document[p] == (byte)'*' && document[p + 1] == (byte)'/'))
                    {
                        p++;
                    }

                    p = p + 1 < limit ? p + 2 : limit;
                    continue;
                }

                return;
            }
        }

        /// <summary>
        /// Строка от открывающей кавычки. <c>false</c>, если закрывающей нет:
        /// значит отказ случился внутри неё.
        /// </summary>
        private static bool ScanString(
            scoped ReadOnlySpan<byte> document, int p, out int end, out (int Start, int Length) content
            )
        {
            var i = p + 1;
            while (i < document.Length)
            {
                var b = document[i];
                if (b == (byte)'\\')
                {
                    i += 2;
                    continue;
                }

                if (b == (byte)'"')
                {
                    content = (p + 1, i - p - 1);
                    end = i + 1;
                    return true;
                }

                i++;
            }

            content = default;
            end = document.Length + 1;
            return false;
        }

        /// <summary>
        /// Число, <c>true</c>/<c>false</c>/<c>null</c> или любой мусор на их
        /// месте - до ближайшего разделителя. Разбирать здесь нечего: путь
        /// интересует только, где эта лексема кончилась.
        /// </summary>
        private static int ScanPrimitive(scoped ReadOnlySpan<byte> document, int p)
        {
            var i = p;
            while (i < document.Length)
            {
                var b = document[i];
                if (b == (byte)',' || b == (byte)'}' || b == (byte)']'
                    || b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                {
                    break;
                }

                i++;
            }

            return i;
        }

        /// <summary>
        /// Имя свойства в том виде, в котором его показывает эталон, -
        /// разэкранированным (<c>Orders</c> в пути выглядит как
        /// <c>Orders</c>, пробой подтверждено).
        ///
        /// Аллокация здесь нарочно не экономится: это путь отказа, он бывает
        /// один раз на документ и только когда документ уже не прочитан.
        /// </summary>
        private static string Decode(scoped ReadOnlySpan<byte> document, (int Start, int Length) content)
        {
            var raw = document.Slice(content.Start, content.Length);

            var escaped = false;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] == (byte)'\\')
                {
                    escaped = true;
                    break;
                }
            }

            if (!escaped)
            {
                return Utf8ToString(raw);
            }

            try
            {
                var buffer = new byte[raw.Length];
                var written = JsonNameUnescape.Decode(raw, buffer);
                return Utf8ToString(new ReadOnlySpan<byte>(buffer, 0, written));
            }
            catch (Exception)
            {
                //экранирование в имени битое - тогда и разэкранировать нечего;
                //сырое имя честнее выдуманного
                return Utf8ToString(raw);
            }
        }

        private static string Utf8ToString(scoped ReadOnlySpan<byte> raw)
        {
            return Encoding.UTF8.GetString(raw.ToArray());
        }

        private static int Clamp(int position, int length)
        {
            if (position < 0)
            {
                return 0;
            }

            return position > length ? length : position;
        }

        /// <summary>
        /// Отказ сканера - с путём. Смещение берётся у самого исключения, а не
        /// у <c>position</c> точки входа: бросавший знал, на каком байте
        /// споткнулся, а <c>position</c> к тому моменту мог уйти вперёд.
        /// </summary>
        public static JsonDocumentException Decorate(JsonDocumentException failure, scoped ReadOnlySpan<byte> document)
        {
            var position = failure.BytePosition >= 0 ? failure.BytePosition : 0;
            Locate(document, position, out var line, out var column);

            return new JsonDocumentException(
                failure.Reason, failure.BytePosition, Build(document, position, failure.Anchor), line, column, failure
                );
        }

        /// <summary>
        /// Отказ инжектора - с путём. Здесь смещение приходит извне: лексема
        /// прочитана сканером, <c>position</c> уже за ней, а
        /// <see cref="FormatException"/> о документе не знает ничего.
        /// </summary>
        public static JsonDocumentException Decorate(
            FormatException failure, scoped ReadOnlySpan<byte> document, int position
            )
        {
            Locate(document, position, out var line, out var column);

            return new JsonDocumentException(
                failure.Message, position, Build(document, position, JsonPathAnchor.AfterToken), line, column, failure
                );
        }
    }
}
