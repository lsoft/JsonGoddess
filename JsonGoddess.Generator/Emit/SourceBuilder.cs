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
        private readonly StringBuilder _builder = new StringBuilder();
        private int _indent;

        public void Indent() => _indent++;

        public void Unindent() => _indent--;

        public void Line()
        {
            _builder.Append('\n');
        }

        public void Line(string text)
        {
            for (var i = 0; i < _indent; i++)
            {
                _builder.Append("    ");
            }

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
