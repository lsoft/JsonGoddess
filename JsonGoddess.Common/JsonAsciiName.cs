using System;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Сравнение имени без учёта регистра - в байтах и только для ASCII.
    ///
    /// Нужно оно ровно в одном месте: <c>System.Text.Json</c> читает имя члена
    /// enum'а без учёта регистра (<c>"draft"</c> означает <c>Draft</c>), и не
    /// уметь этого значило бы не прочитать законный документ. Для значений и
    /// имён свойств такого сравнения нет и не будет - там регистр значим.
    ///
    /// Ограничение ASCII не спрятано, а вынесено в отказ генератора: имя члена
    /// enum'а вне ASCII отвергается (JGD022), потому что эталон свернул бы
    /// регистр по Unicode, а мы - нет, и совпадение зависело бы от алфавита.
    /// Отказ закреплён тестом
    /// <c>GeneratorDiagnosticsFixture.String_enum_refuses_what_cannot_be_matched_exactly</c>.
    /// </summary>
    public static class JsonAsciiName
    {
        /// <summary>
        /// <paramref name="literal"/> печатается генератором и заведомо ASCII;
        /// <paramref name="value"/> приезжает из документа и может быть любым.
        /// Байт вне ASCII совпадением не станет: сворачивается только то, что
        /// лежит в <c>A..Z</c>/<c>a..z</c>, а UTF-8-продолжения туда не попадают
        /// ни при каком значении бита 0x20.
        /// </summary>
        public static bool EqualsIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> literal)
        {
            if (value.Length != literal.Length)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var a = value[i];
                var b = literal[i];

                if (a == b)
                {
                    continue;
                }

                var folded = (byte)(a | 0x20);
                if (folded >= (byte)'a' && folded <= (byte)'z' && folded == (byte)(b | 0x20))
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }
}
