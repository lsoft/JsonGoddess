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
        /// Лексема числа целиком. Грамматику RFC 8259 §6 здесь намеренно не
        /// проверяем: дефолтный путь отдаёт лексему парсеру и принимает то, что
        /// тот съел. Отказ от <c>01</c>, <c>+1</c>, <c>.5</c> - это
        /// <c>JsonGuard.StrictNumbers</c>, и у него своя цена.
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
