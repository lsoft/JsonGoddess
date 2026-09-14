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
    }
}
