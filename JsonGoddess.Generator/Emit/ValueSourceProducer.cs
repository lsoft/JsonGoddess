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
        /// <summary>Полное имя - оно печатается ровно один раз, в псевдоним.</summary>
        public const string ScanFullName = "global::JsonGoddess.Internal.JsonScan";

        /// <summary>Псевдоним, которым сканер зовётся во всём порождённом коде.</summary>
        public const string Scan = "__Scan";

        public const string MemoryExtensionsFullName = "global::System.MemoryExtensions";

        public const string Mem = "__Mem";
        public const string Array = "global::System.Array";
        public const string Naming = "global::JsonGoddess.Internal.JsonNaming";
        public const string NamingStyle = "global::JsonGoddess.Internal.JsonNamingStyle";
        public const string StringDecoder = "global::JsonGoddess.Internal.JsonStringDecoder";

        /// <summary>
        /// Имя метода сканера для содержимого строки - обычное или строгое
        /// (<c>JsonGuard.ControlCharsInStrings</c>).
        ///
        /// Выбор делается на этапе генерации, а не в рантайме: выключенный
        /// страж обязан не стоить ни одной ветки, а не «стоить одну дешёвую
        /// проверку булева параметра». Поэтому это конкатенация строки внутри
        /// эмиттера, а не аргумент функции сканера.
        /// </summary>
        internal static string StringReadCall(JsonGuard guards)
        {
            return Scan + ((guards & JsonGuard.ControlCharsInStrings) != 0
                ? ".ReadStringContentStrict"
                : ".ReadStringContent");
        }

        /// <summary>Тот же приём для чисел - <c>JsonGuard.StrictNumbers</c>.</summary>
        internal static string NumberReadCall(JsonGuard guards)
        {
            return Scan + ((guards & JsonGuard.StrictNumbers) != 0
                ? ".ReadNumberRawStrict"
                : ".ReadNumberRaw");
        }

        /// <summary>
        /// Ключ словаря под политикой именования.
        ///
        /// Имена членов преобразуются на компиляции и печатаются литералами,
        /// то есть политика для них бесплатна. С ключом так нельзя: он данные,
        /// а не объявление, и преобразовывать его приходится на каждой записи -
        /// со строкой на ключ. Поэтому без политики здесь не появляется ни
        /// одного лишнего вызова, а с политикой цена видна и названа.
        /// </summary>
        //internal, а не private: писатель субъекта-коллекции (§9.10 плана,
        //ClassSourceProducer.EmitCollectionSubjectWriterBody) пишет ключ
        //словаря точно так же, как обычная Dictionary<string,V>, и заводить
        //вторую копию преобразования политики ради видимости не стоит
        internal static string Key(string accessor, JsonNamingStyle style)
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
        /// Коллекция, как и на чтении, печатается <b>вызовом</b>: тело цикла
        /// одно на все места, где встретился один и тот же тип коллекции, и
        /// печатать его по месту значило бы копировать два десятка строк на
        /// каждый член. Раньше здесь стояло обратное решение с обоснованием
        /// «экономии нет»; экономия померена (§16.2 плана) и оказалась
        /// заметной, а цена - один статический вызов на коллекцию, то есть
        /// ровно столько же, сколько платит чтение.
        /// </summary>
        public static void WriteValue(SourceBuilder builder, ValueModel value, string accessor, JsonNamingStyle keyNaming)
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
                    //Класс разбирает null сам, внутри Write_: тело у него одно
                    //на все места, где он встретился. Nullable<структура> так
                    //не может - Write_ принимает не-nullable, - поэтому null
                    //снимается здесь, на месте члена.
                    if (value.IsValueType && value.IsNullable)
                    {
                        WriteNullable(
                            builder,
                            accessor,
                            local => "Write_" + value.MethodSuffix + "(exhauster, " + local + ".Value);"
                            );
                        return;
                    }

                    builder.Line("Write_" + value.MethodSuffix + "(exhauster, " + accessor + ");");
                    return;
                }

                case ValueForm.Enum:
                {
                    WriteEnum(builder, value, accessor);
                    return;
                }

                default:
                {
                    builder.Line("WriteCollection_" + value.MethodSuffix + "(exhauster, " + accessor + ");");
                    return;
                }
            }
        }

        /// <summary>
        /// Запись enum'а. В числовом режиме это приведение к подлежащему типу и
        /// та же перегрузка <c>Append</c>, что у обычного числа: значение вне
        /// набора при этом проезжает как есть - ровно так же ведёт себя эталон.
        /// </summary>
        private static void WriteEnum(SourceBuilder builder, ValueModel value, string accessor)
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

            WriteNullable(
                builder,
                accessor,
                local => "WriteEnum_" + value.MethodSuffix + "(exhauster, " + local + ".Value);"
                );
        }

        /// <summary>
        /// <c>Nullable&lt;T&gt;</c> над тем, чей писатель принимает не-nullable:
        /// enum в строковой форме и структура-субъект.
        ///
        /// Локальная переменная, а не два обращения к члену: член может быть
        /// свойством с телом, и вычислять его дважды - менять смысл кода ради
        /// более короткого текста.
        /// </summary>
        private static void WriteNullable(
            SourceBuilder builder,
            string accessor,
            System.Func<string, string> writeValue
            )
        {
            const string local = "nullable";

            builder.OpenBlock();
            builder.Line("var " + local + " = " + accessor + ";");

            builder.OpenBlock("if (" + local + " is null)");
            builder.Line("exhauster.AppendNull();");
            builder.CloseBlock();

            builder.OpenBlock("else");
            builder.Line(writeValue(local));
            builder.CloseBlock();

            builder.CloseBlock();
        }

        /// <summary>
        /// Тело писателя коллекции. Параметр у метода один - по умолчанию
        /// <c>items</c> (обычная коллекция-член), а у писателя субъекта,
        /// который сам является коллекцией (§9.10), - <c>value</c>, потому что
        /// имя параметра там уже задано сигнатурой обычного писателя субъекта.
        /// Номеров в именах локальных нет: вложенная коллекция уезжает в свой
        /// собственный метод, а не разворачивается в цикл внутри цикла.
        ///
        /// Три формы цикла, а не одна: индексированная (<see cref="ValueForm.List"/>,
        /// <see cref="ValueForm.Array"/> - <c>Count</c>/<c>Length</c> и
        /// индексатор гарантированы), с ключом (<see cref="ValueForm.Dictionary"/> -
        /// <c>foreach</c>, ключ не константа и экранируется по-настоящему) и
        /// без ключа (<see cref="ValueForm.Enumerable"/>, фаза 6: интерфейсы
        /// без индексатора - <c>ICollection&lt;T&gt;</c>,
        /// <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>, -
        /// <c>foreach</c> со своим счётчиком, как у словаря, но без пары).
        /// </summary>
        public static void WriteCollectionBody(
            SourceBuilder builder,
            ValueModel value,
            JsonNamingStyle keyNaming,
            string accessor = "items"
            )
        {
            var isMap = value.Form == ValueForm.Dictionary;
            var isForEach = isMap || value.Form == ValueForm.Enumerable;

            builder.OpenBlock("if (" + accessor + " is null)");
            builder.Line("exhauster.AppendNull();");
            builder.Line("return;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isMap ? "{" : "[") + ");");

            if (isForEach)
            {
                //foreach по конкретному Dictionary<,> берёт структурный
                //перечислитель и ничего не выделяет; по позиции ни словарь, ни
                //ICollection<T>/IEnumerable<T> не индексируются, поэтому
                //счётчик ведётся руками
                builder.Line("var i = 0;");
                builder.OpenBlock("foreach (var " + (isMap ? "pair" : "element") + " in " + accessor + ")");
            }
            else
            {
                builder.OpenBlock(
                    "for (var i = 0; i < " + accessor
                    + (value.Form == ValueForm.Array ? ".Length" : ".Count") + "; i++)"
                    );
            }

            builder.OpenBlock("if (i > 0)");
            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(",") + ");");
            builder.CloseBlock();
            builder.Line();

            if (isMap)
            {
                builder.Line("i++;");

                //ключ словаря - не константа этапа компиляции, и экранировать
                //его приходится по-настоящему: System.Text.Json пишет ключ
                //"a\"b" экранированным, и совпасть с ним иначе нельзя
                builder.Line("exhauster.Append(" + Key("pair.Key", keyNaming) + ");");
                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(":") + ");");
                WriteValue(builder, value.Element!, "pair.Value", keyNaming);
            }
            else if (isForEach)
            {
                builder.Line("i++;");
                WriteValue(builder, value.Element!, "element", keyNaming);
            }
            else
            {
                WriteValue(builder, value.Element!, accessor + "[i]", keyNaming);
            }

            builder.CloseBlock();
            builder.Line();

            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isMap ? "}" : "]") + ");");
        }

        /// <summary>
        /// Чтение значения в уже объявленную цель. <c>null</c> до инжектора не
        /// доезжает: решение «положить null или отказать» принадлежит типу
        /// члена, а не лексике, и потому принимается здесь.
        ///
        /// Читается вызовом <b>всё</b>, вплоть до скаляра: тело читателя не
        /// зависит ни от имени члена, ни от типа, которому член принадлежит, и
        /// потому у каждого значения оно одно на весь хост. В ветке
        /// диспетчера остаётся одна строка - та, в которой видно, что куда
        /// присваивается.
        /// </summary>
        public static void ReadValue(SourceBuilder builder, ValueModel value, string target)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                {
                    var read = target + " = Read_" + value.MethodSuffix
                        + "(injector, json, ref position, ref context);";

                    //У класса null снимает сам Read_. У структуры его снимать
                    //нечем и не нужно: null на её месте - отказ, и Read_ им и
                    //кончится. Остаётся Nullable<структура>, где null законен,
                    //а Read_ его вернуть не может.
                    if (value.IsValueType && value.IsNullable)
                    {
                        builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                        builder.Line(target + " = null;");
                        builder.CloseBlock();
                        builder.OpenBlock("else");
                        builder.Line(read);
                        builder.CloseBlock();
                        return;
                    }

                    builder.Line(read);
                    return;
                }

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                case ValueForm.Enumerable:
                {
                    builder.Line(
                        target + " = ReadCollection_" + value.MethodSuffix
                        + "(injector, json, ref position, ref context);"
                        );
                    return;
                }
            }

            if (value.Form == ValueForm.Enum)
            {
                ReadEnum(builder, value, target);
                return;
            }

            builder.Line(
                target + " = ReadScalar_" + BuiltinTypes.MethodSuffix(value.Builtin, value.IsNullable)
                + "(injector, json, ref position, ref context);"
                );
        }

        /// <summary>
        /// Enum. Строковая форма читается своим методом - набор имён у enum'а
        /// свой; числовая идёт читателем подлежащего типа и приводится здесь,
        /// потому что приведение к типу члена - единственное, чем она от него
        /// отличается.
        /// </summary>
        private static void ReadEnum(SourceBuilder builder, ValueModel value, string target)
        {
            if (value.IsStringEnum)
            {
                var read = target + " = ReadEnum_" + value.MethodSuffix
                    + "(injector, json, ref position, ref context);";

                if (!value.IsNullable)
                {
                    builder.Line(read);
                    return;
                }

                builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                builder.Line(target + " = null;");
                builder.CloseBlock();
                builder.OpenBlock("else");
                builder.Line(read);
                builder.CloseBlock();
                return;
            }

            var number = target + " = (" + value.TypeName + ")ReadScalar_"
                + BuiltinTypes.MethodSuffix(value.Builtin, false)
                + "(injector, json, ref position, ref context);";

            if (!value.IsNullable)
            {
                builder.Line(number);
                return;
            }

            builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
            builder.Line(target + " = null;");
            builder.CloseBlock();
            builder.OpenBlock("else");
            builder.Line(number);
            builder.CloseBlock();
        }

        /// <summary>
        /// Тело читателя скаляра: лексема плюс разбор. Возвращает значение, а
        /// не пишет в цель, - цель у него на каждом месте своя, а тело одно.
        ///
        /// <paramref name="guards"/> выбирает лексику сканера
        /// (<c>JsonGuard.StrictNumbers</c>/<c>ControlCharsInStrings</c>) и,
        /// для <c>string</c> - единственного builtin'а, чья материализация
        /// может молча замолчать битую UTF-8 (§6.3 плана: <c>InvalidUtf8</c>),
        /// - предварительную проверку перед вызовом инжектора: даты, GUID'ы и
        /// подобные и так упадут на разборе некорректного текста
        /// <c>FormatException</c>'ом, а строка - нет, ей подходит любой байт.
        /// </summary>
        public static void ReadScalarBody(SourceBuilder builder, ValueModel value, JsonGuard guards)
        {
            var typeName = BuiltinTypes.GetTypeName(value.Builtin);

            if (value.IsNullable)
            {
                builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                builder.Line("return null;");
                builder.CloseBlock();
                builder.Line();
            }

            switch (BuiltinTypes.GetLexeme(value.Builtin))
            {
                case LexemeKind.Number:
                {
                    builder.Line("var raw = " + NumberReadCall(guards) + "(json, ref position);");
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
                    builder.Line("var raw = " + StringReadCall(guards) + "(json, ref position, out var rawEscaped);");

                    if (value.Builtin == BuiltinKind.String && (guards & JsonGuard.InvalidUtf8) != 0)
                    {
                        builder.Line(StringDecoder + ".EnsureValidUtf8(raw, rawEscaped);");
                    }

                    builder.Line("injector.ParseText(ref context, raw, rawEscaped, out " + typeName + " parsed);");
                    break;
                }
            }

            builder.Line("return parsed;");
        }
    }
}
