using System;
using System.Runtime.CompilerServices;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// <c>Try</c>-вариант сканера: то же, что <see cref="JsonScan"/>, но с одним
    /// новым исходом - «байты кончились, дайте добавки».
    ///
    /// <para>
    /// Нужен гибридному пути (PLAN.md §12.9): тело запроса приезжает окном, и
    /// лексема может кончиться на краю окна, не кончившись в документе.
    /// Обычный сканер такого исхода не знает - для него конец буфера это конец
    /// ввода, - и именно поэтому семейство заведено рядом, а не вместо:
    /// буферный путь не должен платить ни одной проверки за случай, которого у
    /// него не бывает.
    /// </para>
    ///
    /// <para>
    /// <b>Правило, на котором держится вся схема: ложь возвращается только на
    /// нехватку данных, кривизна по-прежнему бросает.</b> Различать эти два
    /// исхода обязан не автор кода, а флаг <c>final</c>: пока труба не закрыта,
    /// «кончилось» значит «подожди»; закрыта - то же самое место отказывает
    /// ровно тем же <see cref="JsonDocumentException"/>, что и сегодня. Поэтому
    /// ошибка в классификации не может дать молча принятый обрезанный документ:
    /// она стои́т лишнего круга по трубе, и только.
    /// </para>
    ///
    /// <para>
    /// Позиция при возврате лжи не восстанавливается - вызывающий откатывается
    /// к началу элемента, а не к началу лексемы. Единственное исключение -
    /// <see cref="SkipValueGuarded"/>: счётчик глубины общий с документом, и
    /// брошенный недосчитанным он испортил бы следующую попытку.
    /// </para>
    ///
    /// <para>
    /// Имена методов намеренно совпадают с <see cref="JsonScan"/>: эмиттер
    /// выбирает <b>класс</b>, а не имя метода, и печать обоих путей отличается
    /// одной подстановкой плюс аргументом <c>final</c>.
    /// </para>
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonTryScan
    {
        //значения, а не копии: структурные байты JSON измениться не могут, но
        //источник у них обязан быть один - иначе два семейства однажды разойдутся
        public const byte Quote = JsonScan.Quote;
        public const byte Backslash = JsonScan.Backslash;
        public const byte OpenBrace = JsonScan.OpenBrace;
        public const byte CloseBrace = JsonScan.CloseBrace;
        public const byte OpenBracket = JsonScan.OpenBracket;
        public const byte CloseBracket = JsonScan.CloseBracket;
        public const byte Colon = JsonScan.Colon;
        public const byte Comma = JsonScan.Comma;

        private static readonly byte[] BlockCommentEnd = { (byte)'*', (byte)'/' };

        private static ReadOnlySpan<byte> TrueWord => "true"u8;

        private static ReadOnlySpan<byte> FalseWord => "false"u8;

        private static ReadOnlySpan<byte> NullWord => "null"u8;

        /// <summary>
        /// Пробелы RFC 8259 §2. Упереться в край окна здесь - это ещё не конец
        /// документа: следующий байт может оказаться каким угодно, в том числе
        /// снова пробелом.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SkipWhitespace(ReadOnlySpan<byte> json, scoped ref int position, bool final)
        {
            var i = position;
            while (i < json.Length)
            {
                var b = json[i];
                if (b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                {
                    i++;
                    continue;
                }

                position = i;
                return true;
            }

            position = i;
            return final;
        }

        /// <summary>
        /// То же плюс <c>JsonFeature.Comments</c> - см.
        /// <see cref="JsonScan.SkipWhitespaceAndComments"/>, поведение
        /// совпадает байт в байт, включая конец ввода как законный конец
        /// строчного комментария.
        /// </summary>
        public static bool SkipWhitespaceAndComments(ReadOnlySpan<byte> json, scoped ref int position, bool final)
        {
            while (true)
            {
                if (!SkipWhitespace(json, ref position, final))
                {
                    return false;
                }

                if (position >= json.Length)
                {
                    //сюда попадаем только при final: иначе пробелы вернули ложь
                    return true;
                }

                if (json[position] != (byte)'/')
                {
                    return true;
                }

                if (position + 1 >= json.Length)
                {
                    //одинокий '/' на краю: под окном это неизвестность (за ним
                    //может приехать '/' или '*'), а на закрытой трубе - не
                    //комментарий, и отказ печатает тот, кто нас позвал
                    return final;
                }

                if (json[position + 1] == (byte)'/')
                {
                    var i = position + 2;
                    while (i < json.Length && json[i] != (byte)'\n' && json[i] != (byte)'\r')
                    {
                        i++;
                    }

                    if (i >= json.Length && !final)
                    {
                        //комментарий может продолжаться за краем окна
                        return false;
                    }

                    position = i;
                    continue;
                }

                if (json[position + 1] == (byte)'*')
                {
                    var closing = json.Slice(position + 2).IndexOf(BlockCommentEnd);
                    if (closing < 0)
                    {
                        if (final)
                        {
                            throw new JsonDocumentException(
                                "Expected end of comment, but instead reached end of data.",
                                position + 2
                                );
                        }

                        return false;
                    }

                    position += closing + 4;
                    continue;
                }

                //одиночный '/' перед чем-то иным - не комментарий
                return true;
            }
        }

        /// <summary>
        /// Что стои́т дальше, не потребляя токен. <c>EndOfInput</c> выдаётся
        /// только на закрытой трубе: под окном пустота это «ещё не приехало», и
        /// такой исход выражается ложью.
        /// </summary>
        public static bool Peek(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out JsonTokenKind kind
            )
        {
            kind = JsonTokenKind.None;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                kind = JsonTokenKind.EndOfInput;
                return true;
            }

            switch (json[position])
            {
                case OpenBrace:
                    kind = JsonTokenKind.StartObject;
                    break;
                case CloseBrace:
                    kind = JsonTokenKind.EndObject;
                    break;
                case OpenBracket:
                    kind = JsonTokenKind.StartArray;
                    break;
                case CloseBracket:
                    kind = JsonTokenKind.EndArray;
                    break;
                case Quote:
                    kind = JsonTokenKind.String;
                    break;
                case (byte)'t':
                    kind = JsonTokenKind.True;
                    break;
                case (byte)'f':
                    kind = JsonTokenKind.False;
                    break;
                case (byte)'n':
                    kind = JsonTokenKind.Null;
                    break;
                default:
                    kind = JsonTokenKind.Number;
                    break;
            }

            return true;
        }

        /// <summary>Требует конкретный структурный байт.</summary>
        public static bool Expect(ReadOnlySpan<byte> json, scoped ref int position, byte expected, bool final)
        {
            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                if (final)
                {
                    throw new JsonDocumentException(
                        "Expected '" + (char)expected + "' but the input ended.",
                        position
                        );
                }

                return false;
            }

            if (json[position] != expected)
            {
                throw new JsonDocumentException(
                    "Expected '" + (char)expected + "' but found '" + (char)json[position] + "'.",
                    position
                    );
            }

            position++;
            return true;
        }

        /// <summary>
        /// Съесть байт, если он тот самый. Исход здесь тройной, поэтому «съели»
        /// уезжает в <paramref name="consumed"/>, а возвращаемое значение
        /// означает только «хватило ли данных, чтобы это решить».
        /// </summary>
        public static bool TryConsume(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            byte expected,
            bool final,
            out bool consumed
            )
        {
            consumed = false;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                //на закрытой трубе пустота - законный ответ «байта нет»
                return final;
            }

            if (json[position] != expected)
            {
                return true;
            }

            position++;
            consumed = true;
            return true;
        }

        /// <summary>
        /// Содержимое строки без кавычек - см.
        /// <see cref="JsonScan.ReadStringContent"/>.
        /// </summary>
        public static bool ReadStringContent(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out ReadOnlySpan<byte> content,
            out bool hasEscape
            )
        {
            content = default;
            hasEscape = false;

            if (!OpeningQuote(json, ref position, final))
            {
                return false;
            }

            var start = position + 1;
            var i = start;

            while (true)
            {
                if (i >= json.Length)
                {
                    return Unterminated(start, final);
                }

                var hit = json.Slice(i).IndexOfAny(Quote, Backslash);
                if (hit < 0)
                {
                    return Unterminated(start, final);
                }

                i += hit;

                if (json[i] == Quote)
                {
                    content = json.Slice(start, i - start);
                    position = i + 1;
                    return true;
                }

                //обратный слэш: следующий байт экранирован, каким бы он ни был.
                //Что он ещё не приехал - обнаружится проверкой границы на
                //следующем витке
                hasEscape = true;
                i += 2;
            }
        }

        /// <summary>
        /// То же с проверкой <c>JsonGuard.ControlCharsInStrings</c> - см.
        /// <see cref="JsonScan.ReadStringContentStrict"/>.
        ///
        /// <para>
        /// Управляющий байт, уже лежащий в окне, - это кривизна, а не нехватка:
        /// отказ здесь безусловный, и <c>final</c> на него не влияет.
        /// </para>
        /// </summary>
        public static bool ReadStringContentStrict(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out ReadOnlySpan<byte> content,
            out bool hasEscape
            )
        {
            content = default;
            hasEscape = false;

            if (!OpeningQuote(json, ref position, final))
            {
                return false;
            }

            var start = position + 1;
            var i = start;

            while (true)
            {
                if (i >= json.Length)
                {
                    return Unterminated(start, final);
                }

                var rest = json.Slice(i);
                var hit = rest.IndexOfAny(Quote, Backslash);
                var scanned = hit < 0 ? rest.Length : hit;

                for (var k = 0; k < scanned; k++)
                {
                    if (rest[k] < 0x20)
                    {
                        throw new JsonDocumentException(
                            "Control character 0x" + rest[k].ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
                            + " must be escaped inside a JSON string.",
                            i + k
                            );
                    }
                }

                if (hit < 0)
                {
                    return Unterminated(start, final);
                }

                i += hit;

                if (json[i] == Quote)
                {
                    content = json.Slice(start, i - start);
                    position = i + 1;
                    return true;
                }

                hasEscape = true;
                i += 2;
            }
        }

        private static bool OpeningQuote(ReadOnlySpan<byte> json, scoped ref int position, bool final)
        {
            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            if (position >= json.Length)
            {
                if (final)
                {
                    throw new JsonDocumentException("Expected '\"' but the input ended.", position);
                }

                return false;
            }

            if (json[position] != Quote)
            {
                throw new JsonDocumentException(
                    "Expected '\"' but found '" + (char)json[position] + "'.",
                    position
                    );
            }

            return true;
        }

        private static bool Unterminated(int start, bool final)
        {
            if (final)
            {
                throw new JsonDocumentException("Unterminated string.", start);
            }

            return false;
        }

        /// <summary>
        /// Лексема числа целиком, без проверки грамматики - см.
        /// <see cref="JsonScan.ReadNumberRaw"/>.
        ///
        /// <para>
        /// Число, дотянувшееся до края окна, здесь <b>всегда</b> нехватка: без
        /// грамматики решить, кончилось оно или продолжается, нечем - <c>123</c>
        /// на краю может оказаться началом <c>1234</c>, <c>1.5</c> или
        /// <c>1e9</c>.
        /// </para>
        /// </summary>
        public static bool ReadNumberRaw(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = default;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            var start = position;
            var i = start;

            while (i < json.Length)
            {
                var b = json[i];
                if ((b >= (byte)'0' && b <= (byte)'9')
                    || b == (byte)'-' || b == (byte)'+'
                    || b == (byte)'.' || b == (byte)'e' || b == (byte)'E')
                {
                    i++;
                    continue;
                }

                break;
            }

            if (i == start)
            {
                //чужой байт, уже лежащий в окне, - кривизна, а не нехватка:
                //ждать тут нечего, следующий байт ничего не исправит
                return Need(final || i < json.Length, "Expected a number.", start);
            }

            if (i >= json.Length && !final)
            {
                return false;
            }

            raw = json.Slice(start, i - start);
            position = i;
            return true;
        }

        /// <summary>
        /// Число по грамматике RFC 8259 §6 - <c>JsonGuard.StrictNumbers</c>, см.
        /// <see cref="JsonScan.ReadNumberRawStrict"/>.
        ///
        /// <para>
        /// Единственное место семейства, где край окна проверяется <b>после
        /// каждой части</b> грамматики, а не один раз в конце: у грамматики
        /// здесь больше состояний, чем у остальных примитивов, и «упёрлись в
        /// край» в каждом из них значит своё. Первая версия прототипа была
        /// ленивой ровно тут - и принимала <c>01</c>, где эталон отказывает;
        /// ни падения, ни предупреждения такая лень не даёт.
        /// </para>
        /// </summary>
        public static bool ReadNumberRawStrict(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = default;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            var start = position;
            var i = start;

            if (i < json.Length && json[i] == (byte)'-')
            {
                i++;
            }

            if (i >= json.Length)
            {
                return Need(final, "Invalid number: expected a digit.", i);
            }

            if (json[i] == (byte)'0')
            {
                i++;
            }
            else if (json[i] >= (byte)'1' && json[i] <= (byte)'9')
            {
                i++;
                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }
            }
            else
            {
                throw new JsonDocumentException("Invalid number: expected a digit.", i);
            }

            if (i >= json.Length)
            {
                //дальше могли бы стоять цифра, точка или экспонента
                return final && Finish(json, start, i, ref position, out raw);
            }

            if (json[i] == (byte)'.')
            {
                i++;
                var fracStart = i;

                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }

                if (i == fracStart)
                {
                    //как и выше: видно чужой байт - отказ немедленный, край
                    //окна - ожидание. Без этого различения '1.x' под окном
                    //уезжал бы на переигрывание вместо отказа
                    return Need(final || i < json.Length, "Invalid number: expected a digit after '.'.", i);
                }

                if (i >= json.Length)
                {
                    return final && Finish(json, start, i, ref position, out raw);
                }
            }

            if (json[i] == (byte)'e' || json[i] == (byte)'E')
            {
                i++;

                if (i < json.Length && (json[i] == (byte)'+' || json[i] == (byte)'-'))
                {
                    i++;
                }

                var expStart = i;

                while (i < json.Length && Digit(json[i]))
                {
                    i++;
                }

                if (i == expStart)
                {
                    return Need(final || i < json.Length, "Invalid number: expected a digit in the exponent.", i);
                }

                if (i >= json.Length)
                {
                    return final && Finish(json, start, i, ref position, out raw);
                }
            }

            //висящая цифра сразу после того, как грамматика решила, что число
            //кончилось, - это и есть '01'
            if (Digit(json[i]))
            {
                throw new JsonDocumentException("Invalid number: unexpected extra digit.", i);
            }

            return Finish(json, start, i, ref position, out raw);
        }

        /// <summary>
        /// Лексема литерала целиком - см. <see cref="JsonScan.ReadLiteralRaw"/>.
        /// Слово, дотянувшееся до края окна, - нехватка: <c>tru</c> станет
        /// <c>true</c>, когда доедет четвёртый байт.
        /// </summary>
        public static bool ReadLiteralRaw(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            bool final,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = default;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            var start = position;
            var i = start;

            while (i < json.Length)
            {
                var b = json[i];
                if (b >= (byte)'a' && b <= (byte)'z')
                {
                    i++;
                    continue;
                }

                break;
            }

            if (i == start)
            {
                return Need(final || i < json.Length, "Expected a literal.", start);
            }

            if (i >= json.Length && !final)
            {
                return false;
            }

            raw = json.Slice(start, i - start);
            position = i;
            return true;
        }

        /// <summary>Литерал <c>true</c>/<c>false</c>.</summary>
        public static bool ReadBoolean(ReadOnlySpan<byte> json, scoped ref int position, bool final, out bool value)
        {
            value = false;

            if (!Keyword(json, ref position, TrueWord, final, out var matched))
            {
                return false;
            }

            if (matched)
            {
                value = true;
                return true;
            }

            if (!Keyword(json, ref position, FalseWord, final, out matched))
            {
                return false;
            }

            if (!matched)
            {
                throw new JsonDocumentException("Expected 'true' or 'false'.", position);
            }

            return true;
        }

        /// <summary>
        /// Потребляет <c>null</c>, если он там стои́т. Исход тройной, поэтому
        /// «съели» уезжает в <paramref name="matched"/>.
        /// </summary>
        public static bool TryReadNull(ReadOnlySpan<byte> json, scoped ref int position, bool final, out bool matched)
        {
            return Keyword(json, ref position, NullWord, final, out matched);
        }

        /// <summary>
        /// Слово целиком. Если в окне лежит его начало и больше ничего - это
        /// нехватка, а не несовпадение.
        /// </summary>
        private static bool Keyword(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            ReadOnlySpan<byte> word,
            bool final,
            out bool matched
            )
        {
            matched = false;

            if (!SkipWhitespace(json, ref position, final))
            {
                return false;
            }

            var rest = json.Slice(position);

            if (rest.Length >= word.Length)
            {
                if (rest.StartsWith(word))
                {
                    position += word.Length;
                    matched = true;
                }

                return true;
            }

            if (!final && word.StartsWith(rest))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Пропускает одно значение любой формы - см.
        /// <see cref="JsonScan.SkipValue"/>. Скобки считаются, рекурсии нет.
        /// </summary>
        public static bool SkipValue(ReadOnlySpan<byte> json, scoped ref int position, bool final)
        {
            var depth = 0;

            do
            {
                if (!SkipWhitespace(json, ref position, final))
                {
                    return false;
                }

                if (position >= json.Length)
                {
                    throw new JsonDocumentException("Unexpected end of input while skipping a value.", position);
                }

                var b = json[position];
                switch (b)
                {
                    case OpenBrace:
                    case OpenBracket:
                        depth++;
                        position++;
                        break;

                    case CloseBrace:
                    case CloseBracket:
                        if (depth == 0)
                        {
                            throw new JsonDocumentException("Unexpected '" + (char)b + "'.", position);
                        }

                        depth--;
                        position++;
                        break;

                    case Quote:
                        if (!ReadStringContent(json, ref position, final, out _, out _))
                        {
                            return false;
                        }

                        break;

                    case Colon:
                    case Comma:
                        position++;
                        break;

                    case (byte)'t':
                    case (byte)'f':
                    case (byte)'n':
                        if (!SkipLiteral(json, ref position, final))
                        {
                            return false;
                        }

                        break;

                    default:
                        if (!ReadNumberRaw(json, ref position, final, out _))
                        {
                            return false;
                        }

                        break;
                }
            }
            while (depth > 0);

            return true;
        }

        /// <summary>
        /// То же с накоплением глубины в общий счётчик документа -
        /// <c>JsonGuard.MaxDepth</c>, см. <see cref="JsonScan.SkipValueGuarded"/>.
        ///
        /// <para>
        /// Счётчик <paramref name="depth"/> при возврате лжи возвращается к
        /// тому, чем был на входе: он общий с документом и переживает попытку,
        /// а сама попытка будет переиграна с начала элемента. Недосчитанный
        /// счётчик отказал бы на глубине, до которой документ не доходил.
        /// </para>
        /// </summary>
        public static bool SkipValueGuarded(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref int depth,
            int maxDepth,
            bool final
            )
        {
            var entryDepth = depth;
            var localDepth = 0;

            do
            {
                if (!SkipWhitespace(json, ref position, final))
                {
                    depth = entryDepth;
                    return false;
                }

                if (position >= json.Length)
                {
                    throw new JsonDocumentException("Unexpected end of input while skipping a value.", position);
                }

                var b = json[position];
                switch (b)
                {
                    case OpenBrace:
                    case OpenBracket:
                        localDepth++;
                        depth++;
                        if (depth > maxDepth)
                        {
                            throw new JsonDocumentException(
                                "The maximum configured depth of "
                                + maxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                + " has been exceeded.",
                                position
                                );
                        }

                        position++;
                        break;

                    case CloseBrace:
                    case CloseBracket:
                        if (localDepth == 0)
                        {
                            throw new JsonDocumentException("Unexpected '" + (char)b + "'.", position);
                        }

                        localDepth--;
                        depth--;
                        position++;
                        break;

                    case Quote:
                        if (!ReadStringContent(json, ref position, final, out _, out _))
                        {
                            depth = entryDepth;
                            return false;
                        }

                        break;

                    case Colon:
                    case Comma:
                        position++;
                        break;

                    case (byte)'t':
                    case (byte)'f':
                    case (byte)'n':
                        if (!SkipLiteral(json, ref position, final))
                        {
                            depth = entryDepth;
                            return false;
                        }

                        break;

                    default:
                        if (!ReadNumberRaw(json, ref position, final, out _))
                        {
                            depth = entryDepth;
                            return false;
                        }

                        break;
                }
            }
            while (localDepth > 0);

            return true;
        }

        private static bool SkipLiteral(ReadOnlySpan<byte> json, scoped ref int position, bool final)
        {
            if (!Keyword(json, ref position, TrueWord, final, out var matched))
            {
                return false;
            }

            if (matched)
            {
                return true;
            }

            if (!Keyword(json, ref position, FalseWord, final, out matched))
            {
                return false;
            }

            if (matched)
            {
                return true;
            }

            if (!Keyword(json, ref position, NullWord, final, out matched))
            {
                return false;
            }

            if (!matched)
            {
                throw new JsonDocumentException("Unexpected literal.", position);
            }

            return true;
        }

        private static bool Digit(byte b) => b >= (byte)'0' && b <= (byte)'9';

        private static bool Finish(
            ReadOnlySpan<byte> json,
            int start,
            int end,
            scoped ref int position,
            out ReadOnlySpan<byte> raw
            )
        {
            raw = json.Slice(start, end - start);
            position = end;
            return true;
        }

        /// <summary>
        /// Нехватка или кривизна - решает <paramref name="final"/>, и только он.
        /// </summary>
        private static bool Need(bool final, string message, int at)
        {
            if (final)
            {
                throw new JsonDocumentException(message, at);
            }

            return false;
        }
    }
}
