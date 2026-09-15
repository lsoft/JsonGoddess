using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;
using JsonGoddess.Generator.Shared;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Код для одного значения: как его написать и как его прочитать.
    ///
    /// Структура объекта - скобки, имя свойства, двоеточие, запятая между
    /// членами - сюда не попадает: она известна на этапе компиляции и
    /// печатается литералами в <see cref="ClassSourceProducer"/>. Структура
    /// <b>массива</b>, наоборот, здесь: длина коллекции на этапе компиляции
    /// неизвестна, и запятая между элементами - единственная ветка по
    /// разделителю во всём порождаемом коде.
    /// </summary>
    public static class ValueSourceProducer
    {
        public const string Scan = "global::JsonGoddess.Internal.JsonScan";
        public const string Array = "global::System.Array";
        public const string Naming = "global::JsonGoddess.Internal.JsonNaming";
        public const string NamingStyle = "global::JsonGoddess.Internal.JsonNamingStyle";

        /// <summary>
        /// Ключ словаря под политикой именования.
        ///
        /// Имена членов преобразуются на компиляции и печатаются литералами,
        /// то есть политика для них бесплатна. С ключом так нельзя: он данные,
        /// а не объявление, и преобразовывать его приходится на каждой записи -
        /// со строкой на ключ. Поэтому без политики здесь не появляется ни
        /// одного лишнего вызова, а с политикой цена видна и названа.
        /// </summary>
        private static string Key(string accessor, JsonNamingStyle style)
        {
            return style == JsonNamingStyle.None
                ? accessor
                : Naming + ".Convert(" + accessor + ", " + NamingStyle + "." + style + ")";
        }

        /// <summary>
        /// Запись. Sink пишет законченную лексему вместе с кавычками, поэтому
        /// отличие builtin-типов сводится к выбору перегрузки - и <c>null</c>
        /// он тоже пишет сам, откуда отсутствие проверок на скалярах.
        ///
        /// Коллекция печатается по месту, а не вызовом: цикл по элементам -
        /// это одна строка разделителя плюс тело, и выносить его в метод
        /// значило бы добавить вызов на каждую коллекцию ради экономии,
        /// которой нет. Чтение устроено наоборот, и почему - сказано там же.
        /// </summary>
        public static void WriteValue(SourceBuilder builder, ValueModel value, string accessor, int depth, JsonNamingStyle keyNaming)
        {
            switch (value.Form)
            {
                case ValueForm.Builtin:
                {
                    builder.Line(
                        value.Builtin == BuiltinKind.ByteArray
                            ? "exhauster.AppendBase64(" + accessor + ");"
                            : "exhauster.Append(" + accessor + ");"
                        );
                    return;
                }

                case ValueForm.Subject:
                {
                    builder.Line("Write_" + value.MethodSuffix + "(exhauster, " + accessor + ");");
                    return;
                }

                case ValueForm.Enum:
                {
                    WriteEnum(builder, value, accessor, depth);
                    return;
                }

                default:
                {
                    WriteCollection(builder, value, accessor, depth, keyNaming);
                    return;
                }
            }
        }

        /// <summary>
        /// Запись enum'а. В числовом режиме это приведение к подлежащему типу и
        /// та же перегрузка <c>Append</c>, что у обычного числа: значение вне
        /// набора при этом проезжает как есть - ровно так же ведёт себя эталон.
        /// </summary>
        private static void WriteEnum(SourceBuilder builder, ValueModel value, string accessor, int depth)
        {
            if (!value.IsStringEnum)
            {
                var cast = "(" + BuiltinTypes.GetTypeName(value.Builtin) + (value.IsNullable ? "?" : "") + ")";
                builder.Line("exhauster.Append(" + cast + accessor + ");");
                return;
            }

            if (!value.IsNullable)
            {
                builder.Line("WriteEnum_" + value.MethodSuffix + "(exhauster, " + accessor + ");");
                return;
            }

            var local = "enumValue" + depth;

            builder.OpenBlock();
            builder.Line("var " + local + " = " + accessor + ";");

            builder.OpenBlock("if (" + local + " is null)");
            builder.Line("exhauster.AppendNull();");
            builder.CloseBlock();

            builder.OpenBlock("else");
            builder.Line("WriteEnum_" + value.MethodSuffix + "(exhauster, " + local + ".Value);");
            builder.CloseBlock();

            builder.CloseBlock();
        }

        private static void WriteCollection(SourceBuilder builder, ValueModel value, string accessor, int depth, JsonNamingStyle keyNaming)
        {
            var isMap = value.Form == ValueForm.Dictionary;
            var items = "items" + depth;
            var index = "i" + depth;
            var pair = "pair" + depth;

            //собственный блок: у двух коллекций в одном объекте локальные имена
            //совпали бы, а нумеровать их по номеру члена значило бы поставить
            //текст порождаемого кода в зависимость от порядка членов сильнее,
            //чем он и так от него зависит
            builder.OpenBlock();
            builder.Line("var " + items + " = " + accessor + ";");

            builder.OpenBlock("if (" + items + " is null)");
            builder.Line("exhauster.AppendNull();");
            builder.CloseBlock();

            builder.OpenBlock("else");
            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isMap ? "{" : "[") + ");");

            if (isMap)
            {
                //foreach по конкретному Dictionary<,> берёт структурный
                //перечислитель и ничего не выделяет; по позиции словарь не
                //индексируется, поэтому счётчик ведётся руками
                builder.Line("var " + index + " = 0;");
                builder.OpenBlock("foreach (var " + pair + " in " + items + ")");
            }
            else
            {
                builder.OpenBlock(
                    "for (var " + index + " = 0; " + index + " < " + items
                    + (value.Form == ValueForm.Array ? ".Length" : ".Count") + "; " + index + "++)"
                    );
            }

            builder.OpenBlock("if (" + index + " > 0)");
            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(",") + ");");
            builder.CloseBlock();
            builder.Line();

            if (isMap)
            {
                builder.Line(index + "++;");

                //ключ словаря - не константа этапа компиляции, и экранировать
                //его приходится по-настоящему: System.Text.Json пишет ключ
                //"a\"b" экранированным, и совпасть с ним иначе нельзя
                builder.Line("exhauster.Append(" + Key(pair + ".Key", keyNaming) + ");");
                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(":") + ");");
                WriteValue(builder, value.Element!, pair + ".Value", depth + 1, keyNaming);
            }
            else
            {
                WriteValue(builder, value.Element!, items + "[" + index + "]", depth + 1, keyNaming);
            }

            builder.CloseBlock();
            builder.Line();

            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isMap ? "}" : "]") + ");");
            builder.CloseBlock();

            builder.CloseBlock();
        }

        /// <summary>
        /// Чтение значения в уже объявленную цель. <c>null</c> до инжектора не
        /// доезжает: решение «положить null или отказать» принадлежит типу
        /// члена, а не лексике, и потому принимается здесь.
        ///
        /// Составные значения читаются вызовом, а не по месту: у субъекта тело
        /// читателя общее для всех мест, где он встретился, а у коллекции
        /// вложенность иначе развернулась бы в цикл внутри цикла прямо в ветке
        /// диспетчера - там, где и без того тесно.
        /// </summary>
        public static void ReadValue(SourceBuilder builder, ValueModel value, string target)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                {
                    builder.Line(
                        target + " = Read_" + value.MethodSuffix + "(injector, json, ref position, ref context);"
                        );
                    return;
                }

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                {
                    builder.Line(
                        target + " = ReadCollection_" + value.MethodSuffix
                        + "(injector, json, ref position, ref context);"
                        );
                    return;
                }
            }

            if (value.IsNullable)
            {
                builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                builder.Line(target + " = null;");
                builder.CloseBlock();
                builder.OpenBlock("else");
                ReadScalar(builder, value, target);
                builder.CloseBlock();
                return;
            }

            ReadScalar(builder, value, target);
        }

        private static void ReadScalar(SourceBuilder builder, ValueModel value, string target)
        {
            if (value.Form != ValueForm.Enum)
            {
                ReadLexeme(builder, value, target);
                return;
            }

            if (value.IsStringEnum)
            {
                builder.Line(
                    target + " = ReadEnum_" + value.MethodSuffix + "(injector, json, ref position, ref context);"
                    );
                return;
            }

            builder.Line("var raw = " + Scan + ".ReadNumberRaw(json, ref position);");
            builder.Line(
                "injector.Parse(ref context, raw, out " + BuiltinTypes.GetTypeName(value.Builtin) + " parsed);"
                );
            builder.Line(target + " = (" + value.TypeName + ")parsed;");
        }

        private static void ReadLexeme(SourceBuilder builder, ValueModel value, string target)
        {
            var typeName = BuiltinTypes.GetTypeName(value.Builtin);

            switch (BuiltinTypes.GetLexeme(value.Builtin))
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
