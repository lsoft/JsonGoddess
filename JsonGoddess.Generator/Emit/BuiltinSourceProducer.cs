using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Код для одного builtin-члена: как его написать и как его прочитать.
    ///
    /// Вся структура документа - скобки, имя свойства, двоеточие, запятая -
    /// сюда не попадает: она известна на этапе компиляции и печатается
    /// литералами в <see cref="ClassSourceProducer"/>. Здесь только значение.
    /// </summary>
    public static class BuiltinSourceProducer
    {
        public const string Scan = "global::JsonGoddess.Internal.JsonScan";

        /// <summary>
        /// Запись. Sink пишет законченную лексему вместе с кавычками, поэтому
        /// отличие типов сводится к выбору перегрузки, а <c>byte[]</c> -
        /// единственный, у кого метод свой.
        /// </summary>
        public static void WriteValue(SourceBuilder builder, MemberModel member, string accessor)
        {
            if (member.Kind == BuiltinKind.ByteArray)
            {
                builder.Line("exhauster.AppendBase64(" + accessor + ");");
                return;
            }

            builder.Line("exhauster.Append(" + accessor + ");");
        }

        /// <summary>
        /// Чтение значения в член. <c>null</c> до инжектора не доезжает:
        /// решение "положить null или отказать" принадлежит члену, а не лексике,
        /// и потому принимается здесь.
        /// </summary>
        public static void ReadValue(SourceBuilder builder, MemberModel member, string target)
        {
            if (member.IsNullable)
            {
                builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                builder.Line(target + " = null;");
                builder.CloseBlock();
                builder.OpenBlock("else");
                ReadLexeme(builder, member, target);
                builder.CloseBlock();
                return;
            }

            ReadLexeme(builder, member, target);
        }

        private static void ReadLexeme(SourceBuilder builder, MemberModel member, string target)
        {
            var typeName = BuiltinTypes.GetTypeName(member.Kind);

            switch (BuiltinTypes.GetLexeme(member.Kind))
            {
                case LexemeKind.Number:
                {
                    builder.Line("var raw = " + Scan + ".ReadNumberRaw(json, ref position);");
                    builder.Line("injector.Parse(ref context, raw, out " + typeName + " parsed);");
                    break;
                }

                case LexemeKind.Literal:
                {
                    builder.Line("var raw = " + Scan + ".ReadLiteralRaw(json, ref position);");
                    builder.Line("injector.Parse(ref context, raw, out " + typeName + " parsed);");
                    break;
                }

                default:
                {
                    builder.Line("var raw = " + Scan + ".ReadStringContent(json, ref position, out var rawEscaped);");
                    builder.Line("injector.ParseText(ref context, raw, rawEscaped, out " + typeName + " parsed);");
                    break;
                }
            }

            builder.Line(target + " = parsed;");
        }
    }
}
