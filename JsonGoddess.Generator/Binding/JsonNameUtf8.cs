using System.Text;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Имя свойства в виде UTF-8-байтов - той самой формы, в которой оно
    /// приезжает из документа и сравнивается с литералом.
    /// </summary>
    public static class JsonNameUtf8
    {
        /// <summary>
        /// Отказывает именам, которые в JSON обязаны быть экранированы.
        ///
        /// Причина не в сложности, а в неоднозначности: сгенерированный код
        /// сравнивает имя с <b>сырыми</b> байтами документа, до разэкранирования,
        /// а имя с кавычкой или управляющим символом приезжает в двух разных
        /// видах - <c>\"</c> и <c>"</c> - и оба законны. Совпадение стало бы
        /// зависеть от того, как его написал отправитель.
        ///
        /// Не-ASCII имена при этом полностью законны: в JSON они не требуют
        /// экранирования, и их UTF-8-байты сравниваются напрямую.
        /// </summary>
        public static bool TryEncode(string name, out byte[] utf8)
        {
            utf8 = Encoding.UTF8.GetBytes(name);

            foreach (var b in utf8)
            {
                if (b < 0x20 || b == (byte)'"' || b == (byte)'\\')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Ключ имени - в точности то же, что считает
        /// <c>JsonGoddess.Internal.JsonNameKey.Compute</c> в рантайме: старший
        /// байт - длина, младшие семь - первые семь байт имени, порядок
        /// little-endian.
        ///
        /// Порядок байтов задан здесь арифметикой, а не
        /// <c>BitConverter</c>, и это не украшение: константу печатает
        /// <b>сборочная</b> машина, а сравнивает <b>целевая</b>. У
        /// System.Text.Json такой заботы нет - они считают ключ в рантайме, и
        /// обе стороны сравнения всегда на одной машине.
        /// </summary>
        public static ulong ComputeKey(byte[] utf8)
        {
            var key = (ulong)(byte)utf8.Length << 56;

            var count = utf8.Length < 7 ? utf8.Length : 7;
            for (var i = 0; i < count; i++)
            {
                key |= (ulong)utf8[i] << (i * 8);
            }

            return key;
        }

        /// <summary>
        /// Полон ли ключ, то есть доказывает ли его совпадение совпадение
        /// имени. До семи байт - да, и тогда <c>SequenceEqual</c> не нужен вовсе.
        /// </summary>
        public static bool IsKeyExact(byte[] utf8) => utf8.Length <= 7;

        /// <summary>
        /// Накрывается ли имя двумя перекрывающимися восьмибайтовыми словами -
        /// байтами <c>0..7</c> и <c>L-8..L-1</c>. Их объединение полно, когда
        /// <c>L-8 &lt;= 8</c>, то есть до шестнадцати байт включительно; с
        /// семнадцати между словами появляется дыра, и сравнение перестаёт
        /// доказывать имя.
        ///
        /// Нижняя граница - восемь: короче слова просто не прочитать, спан
        /// имени кончится раньше.
        /// </summary>
        public static bool FitsInTwoWords(byte[] utf8) => utf8.Length >= 8 && utf8.Length <= 16;

        /// <summary>
        /// Восемь байт имени начиная со смещения как little-endian число - то
        /// же самое, что в рантайме прочитает
        /// <c>JsonGoddess.Internal.JsonNameKey.Word</c>.
        ///
        /// Порядок байтов задан арифметикой по той же причине, что и в
        /// <see cref="ComputeKey"/>: константу печатает сборочная машина, а
        /// сравнивает целевая.
        /// </summary>
        public static ulong Word(byte[] utf8, int offset)
        {
            ulong word = 0;

            for (var i = 0; i < 8; i++)
            {
                word |= (ulong)utf8[offset + i] << (i * 8);
            }

            return word;
        }
    }
}
