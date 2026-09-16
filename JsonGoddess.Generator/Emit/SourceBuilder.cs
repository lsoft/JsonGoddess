using System.Text;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Построитель текста с отступами. Отступы здесь не косметика: текст
    /// сгенерированного кода - предмет тестов (§11.3 плана), и разъезжающиеся
    /// пробелы превратили бы их в проверку форматирования.
    /// </summary>
    public sealed class SourceBuilder
    {
        //Отступы готовыми строками, а не циклом по уровням. Строк в файле
        //десятки тысяч, уровней у каждой до девяти, и цикл давал по вызову
        //Append на уровень; здесь он один на строку. Таблица растёт по
        //требованию - глубже неё вложенности не бывает, но и запрещать её
        //незачем.
        private static readonly string[] _indents = BuildIndents(16);

        private readonly StringBuilder _builder = new StringBuilder(1 << 16);
        private int _indent;

        private static string[] BuildIndents(int levels)
        {
            var result = new string[levels];
            for (var i = 0; i < levels; i++)
            {
                result[i] = new string(' ', i * 4);
            }

            return result;
        }

        public void Indent() => _indent++;

        public void Unindent() => _indent--;

        public void Line()
        {
            _builder.Append('\n');
        }

        public void Line(string text)
        {
            _builder.Append(_indent < _indents.Length ? _indents[_indent] : new string(' ', _indent * 4));
            _builder.Append(text);
            _builder.Append('\n');
        }

        public void OpenBlock()
        {
            Line("{");
            Indent();
        }

        public void OpenBlock(string header)
        {
            Line(header);
            OpenBlock();
        }

        public void CloseBlock()
        {
            Unindent();
            Line("}");
        }

        public override string ToString() => _builder.ToString();

        /// <summary>
        /// Строковый литерал C#, гарантированно ASCII: всё за пределами
        /// печатной латиницы уходит в <c>\uXXXX</c>. Для <c>u8</c>-литерала это
        /// безопасно - компилятор кодирует строку в UTF-8 сам, и суррогатная
        /// пара превращается в четырёхбайтовую последовательность как надо.
        /// </summary>
        public static string Literal(string value)
        {
            var result = new StringBuilder(value.Length + 2);
            result.Append('"');

            foreach (var c in value)
            {
                if (c == '"')
                {
                    result.Append("\\\"");
                }
                else if (c == '\\')
                {
                    result.Append("\\\\");
                }
                else if (c >= ' ' && c < (char)0x7F)
                {
                    result.Append(c);
                }
                else
                {
                    result.Append("\\u").Append(((int)c).ToString("X4"));
                }
            }

            result.Append('"');
            return result.ToString();
        }

        /// <summary>UTF-8-литерал: то же самое с суффиксом <c>u8</c>.</summary>
        public static string Utf8Literal(string value) => Literal(value) + "u8";
    }
}
