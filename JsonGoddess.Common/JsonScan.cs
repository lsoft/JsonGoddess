using System;
using System.Runtime.CompilerServices;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Что стоит в позиции курсора. Значения RFC 8259 §3-§6 плюс два
    /// служебных.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public enum JsonTokenKind : byte
    {
        None = 0,
        StartObject = 1,
        EndObject = 2,
        StartArray = 3,
        EndArray = 4,
        String = 5,
        Number = 6,
        True = 7,
        False = 8,
        Null = 9,
        EndOfInput = 10,
    }

    /// <summary>
    /// Однопроходный сканер по UTF-8. Не читатель общего назначения: здесь
    /// только то, что нужно сгенерированному коду, который на каждом шаге уже
    /// знает, чего ждёт.
    ///
    /// Инвариант, за который отвечает каждый метод: ни на каком вводе -
    /// обрезанном, битом, произвольном - не возникает выхода за границы буфера.
    /// Любой отказ это <see cref="JsonDocumentException"/>. Держится fuzz-корпусом.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class JsonScan
    {
        public const byte Quote = (byte)'"';
        public const byte Backslash = (byte)'\\';
        public const byte OpenBrace = (byte)'{';
        public const byte CloseBrace = (byte)'}';
        public const byte OpenBracket = (byte)'[';
        public const byte CloseBracket = (byte)']';
        public const byte Colon = (byte)':';
        public const byte Comma = (byte)',';

        /// <summary>
        /// Пробельные символы RFC 8259 §2: space, tab, LF, CR. И только они -
        /// остальной Unicode-whitespace в JSON не пробельный.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SkipWhitespace(ReadOnlySpan<byte> json, scoped ref int position)
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

                break;
            }

            position = i;
        }

        /// <summary>
        /// То же, что <see cref="SkipWhitespace"/>, плюс <c>JsonFeature.Comments</c>
        /// (JSONC): <c>//</c> до конца строки (или конца ввода) и <c>/* */</c>.
        /// Печатается вместо <see cref="SkipWhitespace"/> только там, где хост
        /// включил фичу, и только как <b>первое</b> действие метода/участка
        /// кода, читающего значение или структурный токен, - выключенная
        /// фича не платит ни одной проверки на счастливом пути (её здесь
        /// попросту не вызывают).
        ///
        /// Единственное место, где этот метод не печатается, даже когда фича
        /// включена, - между именем свойства и двоеточием: пробой
        /// (<c>System.Text.Json</c> 10.0, <c>ReadCommentHandling.Skip</c>)
        /// показал, что сам эталон отказывает на <c>{"Id" /* c */ : 1}</c>,
        /// хотя комментарий легален буквально везде вокруг этого места -
        /// до имени, после двоеточия, перед запятой, перед закрывающей
        /// скобкой. Решение об этом принимает эмиттер (см.
        /// <c>ClassSourceProducer</c>), а не этот метод: он всего лишь не
        /// печатается в этой одной точке.
        /// </summary>
        public static void SkipWhitespaceAndComments(ReadOnlySpan<byte> json, scoped ref int position)
        {
            while (true)
            {
                SkipWhitespace(json, ref position);

                //меньше двух байт впереди - комментарию просто неоткуда
                //начаться; одиночный '/' без второго символа комментарием не
                //является нигде дальше по методу, поэтому оба случая уходят
                //одной проверкой
                if (position + 1 >= json.Length || json[position] != (byte)'/')
                {
                    return;
                }

                if (json[position + 1] == (byte)'/')
                {
                    //строчный комментарий: до конца строки или до конца
                    //ввода, что раньше - оба варианта пробоем подтверждены
                    position += 2;
                    while (position < json.Length && json[position] != (byte)'\n' && json[position] != (byte)'\r')
                    {
                        position++;
                    }

                    continue;
                }

                if (json[position + 1] == (byte)'*')
                {
                    position += 2;
                    var closing = json.Slice(position).IndexOf(BlockCommentEnd);
                    if (closing < 0)
                    {
                        throw new JsonDocumentException("Expected end of comment, but instead reached end of data.", position);
                    }

                    position += closing + 2;
                    continue;
                }

                //одиночный '/' - не комментарий; позиция не трогается, и
                //дальнейший отказ печатает свою собственную, более точную
                //диагностику тот, кто позвал этот метод (Expect/ReadNumberRaw
                //и так далее), а не он сам
                return;
            }
        }

        private static readonly byte[] BlockCommentEnd = { (byte)'*', (byte)'/' };

        /// <summary>
        /// Пропускает пробелы и сообщает, что стоит дальше, не потребляя токен.
        /// </summary>
        public static JsonTokenKind Peek(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length)
            {
                return JsonTokenKind.EndOfInput;
            }

            switch (json[position])
            {
                case OpenBrace:
                    return JsonTokenKind.StartObject;
                case CloseBrace:
                    return JsonTokenKind.EndObject;
                case OpenBracket:
                    return JsonTokenKind.StartArray;
                case CloseBracket:
                    return JsonTokenKind.EndArray;
                case Quote:
                    return JsonTokenKind.String;
                case (byte)'t':
                    return JsonTokenKind.True;
                case (byte)'f':
                    return JsonTokenKind.False;
                case (byte)'n':
                    return JsonTokenKind.Null;
                default:
                    return JsonTokenKind.Number;
            }
        }

        /// <summary>
        /// Пропускает пробелы и требует конкретный структурный байт.
        /// </summary>
        public static void Expect(ReadOnlySpan<byte> json, scoped ref int position, byte expected)
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != expected)
            {
                throw Unexpected(json, position, expected);
            }

            position++;
        }

        /// <summary>
        /// Пропускает пробелы и потребляет байт, если он там есть.
        /// </summary>
        public static bool TryConsume(ReadOnlySpan<byte> json, scoped ref int position, byte expected)
        {
            SkipWhitespace(json, ref position);
            if (position < json.Length && json[position] == expected)
            {
                position++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Содержимое строки без кавычек. На входе курсор стоит на открывающей
        /// кавычке (пробелы перед ней пропускаются), на выходе - сразу за
        /// закрывающей.
        ///
        /// <paramref name="hasEscape"/> - в содержимом есть обратный слэш.
        /// Быстрый путь декодера на это смотрит первым: строка без escape
        /// декодируется одним транскодированием, с escape - посимвольно.
        /// </summary>
        public static ReadOnlySpan<byte> ReadStringContent(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            out bool hasEscape
            )
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != Quote)
            {
                throw Unexpected(json, position, Quote);
            }

            var start = position + 1;
            var i = start;
            hasEscape = false;

            while (true)
            {
                if (i >= json.Length)
                {
                    throw new JsonDocumentException("Unterminated string.", start);
                }

                var rest = json.Slice(i);
                var hit = rest.IndexOfAny(Quote, Backslash);
                if (hit < 0)
                {
                    throw new JsonDocumentException("Unterminated string.", start);
                }

                i += hit;
                if (json[i] == Quote)
                {
                    position = i + 1;
                    return json.Slice(start, i - start);
                }

                //обратный слэш: следующий байт экранирован, каким бы он ни был.
                //Проверка корректности самой последовательности - дело декодера,
                //здесь важно только не принять \" за конец строки
                hasEscape = true;
                i += 2;
            }
        }

        /// <summary>
        /// То же, что <see cref="ReadStringContent"/>, но с проверкой
        /// <c>JsonGuard.ControlCharsInStrings</c>: неэкранированный байт
        /// &lt; U+0020 внутри строки - отказ (RFC 8259 §7). Печатается вместо
        /// обычного чтения только на хосте, включившем страж, - выключенный
        /// страж не платит даже за более широкий поиск.
        ///
        /// Пробой подтверждено (System.Text.Json 9.0.0): эталон отказывает на
        /// сыром управляющем байте безусловно - в <see cref="JsonReaderOptions"/>
        /// нет свойства, которое бы это разрешало, то есть это единственное
        /// поведение эталона, а не более строгий режим сверх него.
        /// </summary>
        public static ReadOnlySpan<byte> ReadStringContentStrict(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            out bool hasEscape
            )
        {
            SkipWhitespace(json, ref position);
            if (position >= json.Length || json[position] != Quote)
            {
                throw Unexpected(json, position, Quote);
            }

            var start = position + 1;
            var i = start;
            hasEscape = false;

            while (true)
            {
                if (i >= json.Length)
                {
                    throw new JsonDocumentException("Unterminated string.", start);
                }

                var rest = json.Slice(i);
                var hit = rest.IndexOfAny(Quote, Backslash);
                var scanned = hit < 0 ? rest.Length : hit;

                //Управляющие байты ищутся отдельным проходом по уже найденному куску,
                //а не одним расширенным IndexOfAny: набор из 34 значений
                //(кавычка, слэш, 32 управляющих байта) - это то самое место,
                //где план (§6.3) прямо просит замер перед тем, как сделать
                //поиск безусловным; здесь он платится только под флагом
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
                    throw new JsonDocumentException("Unterminated string.", start);
                }

                i += hit;
                if (json[i] == Quote)
                {
                    position = i + 1;
                    return json.Slice(start, i - start);
                }

                hasEscape = true;
                i += 2;
            }
        }

        /// <summary>
        /// Лексема числа целиком. Грамматику RFC 8259 §6 здесь намеренно не
        /// проверяем: дефолтный путь отдаёт лексему парсеру и принимает то, что
        /// тот съел. Отказ от <c>01</c>, <c>+1</c>, <c>.5</c> - это
        /// <c>JsonGuard.StrictNumbers</c> и <see cref="ReadNumberRawStrict"/>.
        /// </summary>
        public static ReadOnlySpan<byte> ReadNumberRaw(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
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
                throw new JsonDocumentException("Expected a number.", start);
            }

            position = i;
            return json.Slice(start, i - start);
        }

        /// <summary>
        /// То же, но с проверкой грамматики RFC 8259 §6 -
        /// <c>JsonGuard.StrictNumbers</c>. Печатается вместо
        /// <see cref="ReadNumberRaw"/> только на хосте, включившем страж:
        /// выключенный страж не должен стоить ни одной ветки, и лексика
        /// эталона (<c>Utf8Parser</c>-совместимая) остаётся дефолтом.
        ///
        /// Пробой подтверждено (System.Text.Json 9.0.0): <c>01</c>, <c>+1</c>,
        /// <c>.5</c>, <c>1.</c>, <c>1e</c> эталон отвергает безусловно, без
        /// какой-либо опции это разрешить, - то есть это не «более строгий
        /// режим», а единственное поведение самого эталона.
        ///
        /// Проверка стоит границу лексемы саму по себе: для валидного числа
        /// она даёт ту же лексему, что и <see cref="ReadNumberRaw"/> (все
        /// байты валидного числа входят в тот же набор символов), поэтому
        /// документ, уже пройденный стражем, парсер разбирает как раньше.
        /// </summary>
        public static ReadOnlySpan<byte> ReadNumberRawStrict(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
            var start = position;
            var i = start;

            if (i < json.Length && json[i] == (byte)'-')
            {
                i++;
            }

            if (i < json.Length && json[i] == (byte)'0')
            {
                //ведущий ноль допустим только в одиночку - '01' грамматике не
                //соответствует, и это ловится ниже финальной проверкой на
                //висящую цифру
                i++;
            }
            else if (i < json.Length && json[i] >= (byte)'1' && json[i] <= (byte)'9')
            {
                i++;
                while (i < json.Length && json[i] >= (byte)'0' && json[i] <= (byte)'9')
                {
                    i++;
                }
            }
            else
            {
                throw new JsonDocumentException("Invalid number: expected a digit.", i);
            }

            if (i < json.Length && json[i] == (byte)'.')
            {
                i++;
                var fracStart = i;
                while (i < json.Length && json[i] >= (byte)'0' && json[i] <= (byte)'9')
                {
                    i++;
                }

                if (i == fracStart)
                {
                    throw new JsonDocumentException("Invalid number: expected a digit after '.'.", i);
                }
            }

            if (i < json.Length && (json[i] == (byte)'e' || json[i] == (byte)'E'))
            {
                i++;
                if (i < json.Length && (json[i] == (byte)'+' || json[i] == (byte)'-'))
                {
                    i++;
                }

                var expStart = i;
                while (i < json.Length && json[i] >= (byte)'0' && json[i] <= (byte)'9')
                {
                    i++;
                }

                if (i == expStart)
                {
                    throw new JsonDocumentException("Invalid number: expected a digit in the exponent.", i);
                }
            }

            //висящая цифра сразу после того, как грамматика решила, что число
            //кончилось, - это и есть '01' (после одиночного '0') или похожий
            //случай: без этой проверки остаток тихо ушёл бы в следующую
            //лексему документа с куда менее внятной ошибкой
            if (i < json.Length && json[i] >= (byte)'0' && json[i] <= (byte)'9')
            {
                throw new JsonDocumentException("Invalid number: unexpected extra digit.", i);
            }

            position = i;
            return json.Slice(start, i - start);
        }

        /// <summary>
        /// Лексема литерала целиком (<c>true</c>, <c>false</c>, <c>null</c>) -
        /// байты как есть, для передачи в инжектор.
        ///
        /// Инжектор здесь не лишняя прослойка: булев член читается им же, чем и
        /// числовой, и это единственное место, где пользовательский инжектор
        /// сможет принять чужую форму (скажем, <c>"true"</c> строкой), не
        /// перекраивая сканер.
        /// </summary>
        public static ReadOnlySpan<byte> ReadLiteralRaw(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
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
                throw new JsonDocumentException("Expected a literal.", start);
            }

            position = i;
            return json.Slice(start, i - start);
        }

        /// <summary>
        /// Литерал <c>true</c>/<c>false</c>.
        /// </summary>
        public static bool ReadBoolean(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
            if (TryReadLiteral(json, ref position, "true"))
            {
                return true;
            }

            if (TryReadLiteral(json, ref position, "false"))
            {
                return false;
            }

            throw new JsonDocumentException("Expected 'true' or 'false'.", position);
        }

        /// <summary>
        /// Потребляет <c>null</c>, если он там стоит.
        /// </summary>
        public static bool TryReadNull(ReadOnlySpan<byte> json, scoped ref int position)
        {
            SkipWhitespace(json, ref position);
            return TryReadLiteral(json, ref position, "null");
        }

        private static bool TryReadLiteral(ReadOnlySpan<byte> json, scoped ref int position, string literal)
        {
            if (position + literal.Length > json.Length)
            {
                return false;
            }

            for (var k = 0; k < literal.Length; k++)
            {
                if (json[position + k] != (byte)literal[k])
                {
                    return false;
                }
            }

            position += literal.Length;
            return true;
        }

        /// <summary>
        /// Пропускает одно значение любой формы, включая вложенные объекты и
        /// массивы. Считает скобки, не рекурсируя: чужое поддерево может быть
        /// сколь угодно глубоким, и стека на него нет.
        /// </summary>
        public static void SkipValue(ReadOnlySpan<byte> json, scoped ref int position)
        {
            var depth = 0;

            do
            {
                SkipWhitespace(json, ref position);
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
                        ReadStringContent(json, ref position, out _);
                        break;

                    case Colon:
                    case Comma:
                        position++;
                        break;

                    case (byte)'t':
                    case (byte)'f':
                    case (byte)'n':
                        if (!TryReadLiteral(json, ref position, "true")
                            && !TryReadLiteral(json, ref position, "false")
                            && !TryReadLiteral(json, ref position, "null"))
                        {
                            throw new JsonDocumentException("Unexpected literal.", position);
                        }

                        break;

                    default:
                        ReadNumberRaw(json, ref position);
                        break;
                }
            }
            while (depth > 0);
        }

        /// <summary>
        /// То же, что <see cref="SkipValue"/>, но с накоплением глубины в
        /// общий счётчик документа - <c>JsonGuard.MaxDepth</c>.
        ///
        /// Печатается вместо обычного пропуска только тогда, когда хост
        /// включил <c>MaxDepth</c>, но не включил <c>UnknownProperties</c>
        /// (иначе неизвестное свойство - отказ ещё до того, как его значение
        /// понадобилось бы пропускать). Счётчик - общий с известными типами,
        /// которые тоже увеличивают его при входе в свой <c>{</c>/<c>[</c>
        /// (см. <c>ClassSourceProducer</c>): пробой подтверждено, что у
        /// эталона предел глубины считается сквозным образом что для
        /// известных членов, что для пропускаемых поддеревьев незнакомых
        /// свойств - вложенный неизвестный массив на глубине, где известный
        /// граф уже израсходовал бюджет, тоже получает отказ.
        /// </summary>
        public static void SkipValueGuarded(
            ReadOnlySpan<byte> json,
            scoped ref int position,
            scoped ref int depth,
            int maxDepth
            )
        {
            var localDepth = 0;

            do
            {
                SkipWhitespace(json, ref position);
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
                        ReadStringContent(json, ref position, out _);
                        break;

                    case Colon:
                    case Comma:
                        position++;
                        break;

                    case (byte)'t':
                    case (byte)'f':
                    case (byte)'n':
                        if (!TryReadLiteral(json, ref position, "true")
                            && !TryReadLiteral(json, ref position, "false")
                            && !TryReadLiteral(json, ref position, "null"))
                        {
                            throw new JsonDocumentException("Unexpected literal.", position);
                        }

                        break;

                    default:
                        ReadNumberRaw(json, ref position);
                        break;
                }
            }
            while (localDepth > 0);
        }

        private static JsonDocumentException Unexpected(ReadOnlySpan<byte> json, int position, byte expected)
        {
            if (position >= json.Length)
            {
                return new JsonDocumentException("Expected '" + (char)expected + "' but the input ended.", position);
            }

            return new JsonDocumentException(
                "Expected '" + (char)expected + "' but found '" + (char)json[position] + "'.",
                position
                );
        }
    }
}
