using System.Globalization;
using System.Text;

//Этот файл компилируется в две сборки, и в генераторе он обязан лежать в другом
//пространстве имён. Иначе получается один и тот же публичный тип в рантайм-сборке
//и в анализаторе, и компиляция, которая видит обе, отказывается выбрать (CS0433).
//У потребителя такого не бывает - анализатор подключается без ссылки на сборку, -
//но харнесс генератора видит обе, и он прав, что видит: это настоящая коллизия.
#if JSONGODDESS_GENERATOR
namespace JsonGoddess.Generator.Shared
#else
namespace JsonGoddess.Internal
#endif
{
    /// <summary>
    /// Политика именования. Значения совпадают с
    /// <c>System.Text.Json.Serialization.JsonKnownNamingPolicy</c> числом, а не
    /// только по смыслу: генератор читает константу атрибута из метаданных
    /// числом, и отображение получается тождественным.
    /// </summary>
    public enum JsonNamingStyle
    {
        None = 0,
        CamelCase = 1,
        SnakeCaseLower = 2,
        SnakeCaseUpper = 3,
        KebabCaseLower = 4,
        KebabCaseUpper = 5,
    }

    /// <summary>
    /// Преобразование имени по политике.
    ///
    /// Этот файл компилируется <b>дважды</b>: в рантайм-сборку и в генератор
    /// (ссылкой на исходник). Иначе алгоритм пришлось бы держать в двух местах,
    /// а он ровно тот случай, где две копии разъедутся незаметно: имена членов
    /// преобразуются на компиляции, ключи словарей - в рантайме, и разойтись
    /// они не имеют права.
    ///
    /// Алгоритмы повторяют <c>System.Text.Json</c> и <b>сверяются с ним</b>
    /// прогоном на корпусе имён (<c>JsonNamingFixture</c>): ожидание называет
    /// их <c>ConvertName</c>, а не я. Это не формальность - здесь легко
    /// ошибиться и не заметить: <c>ABCd</c> даёт <c>abCd</c>, а не
    /// <c>abcd</c> и не <c>aBCd</c>, а <c>X509Certificate</c> -
    /// <c>x509Certificate</c>, потому что цифра обрывает пробег заглавных.
    ///
    /// Регистр сворачивается по Unicode, а не по ASCII: <c>Имя</c> у эталона
    /// даёт <c>имя</c>. Здесь это можно себе позволить - преобразование идёт
    /// над строкой и вне горячего пути, - в отличие от сравнения имён enum'ов,
    /// где по той же причине пришлось ограничиться ASCII.
    /// </summary>
    public static class JsonNaming
    {
        public static string Convert(string name, JsonNamingStyle style)
        {
            switch (style)
            {
                case JsonNamingStyle.CamelCase:
                    return ToCamelCase(name);

                case JsonNamingStyle.SnakeCaseLower:
                    return ToSeparated(name, '_', false);

                case JsonNamingStyle.SnakeCaseUpper:
                    return ToSeparated(name, '_', true);

                case JsonNamingStyle.KebabCaseLower:
                    return ToSeparated(name, '-', false);

                case JsonNamingStyle.KebabCaseUpper:
                    return ToSeparated(name, '-', true);

                default:
                    return name;
            }
        }

        /// <summary>
        /// Пробег заглавных опускается целиком, но останавливается перед
        /// последней заглавной, за которой идёт строчная: <c>IOStream</c> даёт
        /// <c>ioStream</c>, потому что <c>S</c> начинает следующее слово.
        /// </summary>
        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            {
                return name;
            }

            var chars = name.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                //вторая буква не заглавная - значит слово обычное, и трогать
                //дальше нечего
                if (i == 1 && !char.IsUpper(chars[i]))
                {
                    break;
                }

                var hasNext = i + 1 < chars.Length;

                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    if (chars[i + 1] == ' ')
                    {
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    }

                    break;
                }

                chars[i] = char.ToLowerInvariant(chars[i]);
            }

            return new string(chars);
        }

        private enum SeparatorState
        {
            NotStarted,
            UppercaseLetter,
            LowercaseLetterOrDigit,
            SpaceSeparator,
        }

        /// <summary>
        /// Разделитель ставится на границе слов, а пробег заглавных словом не
        /// разрывается: <c>HTTPResponseCode</c> даёт <c>http_response_code</c>,
        /// а не <c>h_t_t_p_...</c>. Граница внутри пробега появляется только
        /// перед последней заглавной, за которой идёт строчная.
        /// </summary>
        private static string ToSeparated(string name, char separator, bool upper)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var builder = new StringBuilder(name.Length + 4);
            var state = SeparatorState.NotStarted;

            for (var i = 0; i < name.Length; i++)
            {
                var current = name[i];

                switch (CharUnicodeInfo.GetUnicodeCategory(current))
                {
                    case UnicodeCategory.UppercaseLetter:
                    {
                        switch (state)
                        {
                            case SeparatorState.NotStarted:
                                break;

                            case SeparatorState.LowercaseLetterOrDigit:
                            case SeparatorState.SpaceSeparator:
                                builder.Append(separator);
                                break;

                            case SeparatorState.UppercaseLetter:
                                if (i + 1 < name.Length && char.IsLower(name[i + 1]))
                                {
                                    builder.Append(separator);
                                }

                                break;
                        }

                        builder.Append(upper ? current : char.ToLowerInvariant(current));
                        state = SeparatorState.UppercaseLetter;
                        break;
                    }

                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.DecimalDigitNumber:
                    {
                        if (state == SeparatorState.SpaceSeparator)
                        {
                            builder.Append(separator);
                        }

                        builder.Append(upper ? char.ToUpperInvariant(current) : current);
                        state = SeparatorState.LowercaseLetterOrDigit;
                        break;
                    }

                    case UnicodeCategory.SpaceSeparator:
                    {
                        if (state != SeparatorState.NotStarted)
                        {
                            state = SeparatorState.SpaceSeparator;
                        }

                        break;
                    }

                    default:
                    {
                        //всё прочее - знаки препинания, подчёркивание, символы -
                        //переносится как есть и обрывает слово
                        builder.Append(current);
                        state = SeparatorState.NotStarted;
                        break;
                    }
                }
            }

            return builder.ToString();
        }
    }
}
