using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Драйвер потокового чтения на корневой тип (PLAN.md §12.9, фаза 10,
    /// пункт 6в): автомат верхнего уровня плюс точки входа.
    ///
    /// <para>
    /// Печатается только автомат. Всё, что от типа не зависит - цикл по трубе,
    /// сборка непрерывного окна, потолок на недочитанную конструкцию, возврат
    /// аренды, - живёт в рантайме (<c>JsonStreamReader&lt;T&gt;</c>) и
    /// печататься в каждый хост не должно.
    /// </para>
    ///
    /// <para>
    /// <b>Класс-наследник, а не набор статических методов</b>: состояние
    /// разбора обязано пережить <c>await</c>, а окно - нет. Поля наследника
    /// переживают, спан приходит параметром синхронного метода и умирает вместе
    /// с ним. Это и есть «переигрывать, а не возобновляться», выраженное
    /// системой типов.
    /// </para>
    /// </summary>
    public static class StreamDriverProducer
    {
        private const string TryScan = "__TryScan";
        private const string Context = "global::JsonGoddess.JsonParseContext";
        private const string Span = "global::System.ReadOnlySpan<byte>";
        private const string DocumentException = "global::JsonGoddess.JsonDocumentException";
        private const string StreamReader = "global::JsonGoddess.Internal.JsonStreamReader";
        private const string PooledList = "global::JsonGoddess.Internal.JsonPooledList";
        private const string StreamPath = "global::JsonGoddess.Internal.JsonStreamPath";
        private const string PipeReader = "global::System.IO.Pipelines.PipeReader";
        private const string Token = "global::System.Threading.CancellationToken";
        private const string Task = "global::System.Threading.Tasks.Task";
        private const string List = "global::System.Collections.Generic.List";
        private const string IList = "global::System.Collections.Generic.IList";

        //фазы автомата массива. Их пять, а не три, и это находка прототипа:
        //окно кончается где угодно, в том числе МЕЖДУ элементом и запятой, а
        //закрывающая скобка законна только до первого элемента ([] законно,
        //[1,] - нет)
        private const int BeforeArray = 0;
        private const int BeforeFirstElement = 1;
        private const int BeforeElement = 2;
        private const int AfterElement = 3;
        private const int Done = 4;

        public static void Emit(
            SourceBuilder builder,
            HostModel host,
            IReadOnlyCollection<string> servable
            )
        {
            var roots = host.Subjects
                .Where(s => s.IsRoot && servable.Contains(s.MethodSuffix))
                .ToList();

            if (roots.Count == 0)
            {
                return;
            }

            var injectors = host.InjectorTypes;

            for (var i = 0; i < injectors.Count; i++)
            {
                foreach (var root in roots)
                {
                    EmitArrayDriver(builder, root, injectors[i], ClassName("Stream_", root, injectors.Count, i));

                    if (TryReaderProducer.ReadsByProperty(root, host.Guards))
                    {
                        EmitSingleDriver(
                            builder,
                            root,
                            injectors[i],
                            ClassName("StreamOne_", root, injectors.Count, i),
                            host.Guards,
                            servable
                            );
                    }
                }
            }
        }

        /// <summary>
        /// Имя класса драйвера. Индекс инжектора приписывается, только когда
        /// инжекторов больше одного: у точек входа перегрузки различаются
        /// типом первого параметра, а у класса различать нечем.
        /// </summary>
        private static string ClassName(string prefix, SubjectModel subject, int injectorCount, int index)
        {
            return prefix + subject.MethodSuffix
                + (injectorCount > 1 ? "__" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        }

        private static void EmitArrayDriver(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            string className
            )
        {
            var element = subject.FullName;
            var reader = TryReaderProducer.SubjectMethod(subject);

            EmitEntryPoints(builder, subject, injector, className, element);

            builder.Line("/// <summary>Автомат верхнего уровня: корневой массив элементов " + element + ".</summary>");

            //IList<T>, а не T[] и не List<T>: корнем запроса бывает и то и
            //другое, а копировать одно в другое значило бы отдать ровно ту
            //аллокацию, ради отсутствия которой накопитель и заведён.
            //
            //internal, а не private: у драйвера есть счётчики (окно,
            //переигрывания, сборки окна копией), и без доступа к ним потоковый
            //путь нечем проверять - именно они показывают, что окно не растёт
            //с телом
            builder.OpenBlock(
                "internal sealed class " + className + " : " + StreamReader + "<" + IList + "<" + element + ">>"
                );

            builder.Line("private readonly " + injector + " _injector;");
            builder.Line();
            builder.Line("private readonly bool _asList;");
            builder.Line();
            builder.Line("private " + PooledList + "<" + element + "> _items;");
            builder.Line();
            builder.Line("private int _phase;");
            builder.Line();

            builder.Line("/// <summary>");
            builder.Line("/// Наибольший виденный элемент: не начинать разбор там, где он заведомо");
            builder.Line("/// не влезет. Стои́т это не времени, а мусора - до места обрыва элемент");
            builder.Line("/// уже построен и будет выброшен.");
            builder.Line("/// </summary>");
            builder.Line("private int _largest;");
            builder.Line();

            builder.OpenBlock("internal " + className + "(" + injector + " injector, bool asList)");
            builder.Line("_injector = injector;");
            builder.Line("_asList = asList;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("protected override bool IsDone => _phase == " + Done + ";");
            builder.Line();

            builder.Line(
                "protected override " + IList + "<" + element + "> Finish() =>"
                );
            builder.Indent();
            builder.Line("_asList ? _items.FinishAsList() : _items.Finish();");
            builder.Unindent();
            builder.Line();

            builder.Line("protected override void Release() => _items.Release();");
            builder.Line();

            builder.OpenBlock(
                "protected override int ParseWhatFits(scoped " + Span + " json, bool final)"
                );

            builder.Line("var context = new " + Context + "(json);");
            builder.Line();
            builder.OpenBlock("try");

            builder.Line("var position = 0;");
            builder.Line("var consumed = 0;");
            builder.Line();

            builder.OpenBlock("if (_phase == " + BeforeArray + ")");
            Wait(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".OpenBracket, final)");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + BeforeFirstElement + ";");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("while (true)");

            //пустой массив законен только до первого элемента
            builder.OpenBlock("if (_phase == " + BeforeFirstElement + ")");
            Wait(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".CloseBracket, final, out var empty)");
            builder.OpenBlock("if (empty)");
            builder.Line("_phase = " + Done + ";");
            builder.Line("return position;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("_phase = " + BeforeElement + ";");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (_phase == " + BeforeElement + ")");
            builder.Line("var start = position;");
            builder.Line();
            builder.OpenBlock("if (!final && json.Length - position < _largest)");
            builder.Line("Skipped++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("bool read;");
            builder.Line(subject.Declaration + " item;");
            builder.Line();
            builder.OpenBlock("try");
            builder.Line(
                "read = " + reader + "(_injector, json, ref position, ref context, final, out item);"
                );
            builder.CloseBlock();
            builder.OpenBlock("catch (" + DocumentException + " failure)");

            //путь эталона - $[3].id: индекс знает драйвер, место внутри -
            //холодный проход по элементу; окна целиком для прохода нет и быть
            //не может, оно кончается где попало
            builder.Line(
                "throw " + StreamPath + ".Inside(failure, json, start, " + StreamPath + ".Index(_items.Count));"
                );
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (!read)");
            builder.Line("Retries++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("_items.Add(item!);");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + AfterElement + ";");
            builder.Line();
            builder.Line("var size = position - start;");
            builder.OpenBlock("if (size > _largest)");
            builder.Line("_largest = size;");
            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (_phase == " + AfterElement + ")");
            Wait(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".Comma, final, out var more)");
            builder.OpenBlock("if (more)");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + BeforeElement + ";");
            builder.Line("continue;");
            builder.CloseBlock();
            builder.Line();
            Wait(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".CloseBracket, final)");
            builder.Line("_phase = " + Done + ";");
            builder.Line("return position;");
            builder.CloseBlock();
            builder.CloseBlock();

            builder.CloseBlock();

            //отказ ВНЕ элемента: до массива, между элементами, на закрытии.
            //Внутри элемента путь уже поставлен, и у такого отказа он не null
            builder.OpenBlock("catch (" + DocumentException + " failure) when (failure.Path is null)");
            builder.Line(
                "throw " + StreamPath + ".Outside(failure, json, _phase == " + BeforeArray
                + " ? \"$\" : " + StreamPath + ".Index(_items.Count));"
                );
            builder.CloseBlock();
            builder.OpenBlock("finally");
            builder.Line("context.Release();");
            builder.CloseBlock();

            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Точки входа. Три, потому что корнем запроса бывает и массив, и
        /// список: обе формы читает один автомат, и различаются они только тем,
        /// во что накопитель отдаёт прочитанное.
        /// </summary>
        private static void EmitEntryPoints(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            string className,
            string element
            )
        {
            EmitEntry(
                builder, injector, className, element,
                "StreamReadArray_" + subject.MethodSuffix, element + "[]", "false", "(" + element + "[])"
                );

            EmitEntry(
                builder, injector, className, element,
                "StreamReadList_" + subject.MethodSuffix, List + "<" + element + ">", "true",
                "(" + List + "<" + element + ">)"
                );
        }

        private static void EmitEntry(
            SourceBuilder builder,
            string injector,
            string className,
            string element,
            string name,
            string resultType,
            string asList,
            string cast
            )
        {
            builder.Line("internal static async " + Task + "<" + resultType + "> " + name + "(");
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line(PipeReader + " pipe,");
            builder.Line("int cap = " + StreamReader + "<" + IList + "<" + element + ">>.DefaultCap,");
            builder.Line(Token + " cancellationToken = default");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            builder.Line("var driver = new " + className + "(injector, " + asList + ");");
            builder.Line();
            builder.Line(
                "return " + cast + "await driver.ReadAsync(pipe, cap, cancellationToken).ConfigureAwait(false);"
                );

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Драйвер корневого <b>объекта</b>.
        ///
        /// <para>
        /// Заведён потому, что на таком теле драйвер массива вырождается:
        /// элемент там один, и «переиграть элемент» означает «перечитать весь
        /// документ», то есть держать его в памяти целиком. Здесь единицей
        /// переигрывания становится <b>свойство</b>: объект создаётся один раз и
        /// живёт в поле драйвера, а откатывается и перечитывается только то
        /// свойство, которое не влезло.
        /// </para>
        ///
        /// <para>
        /// А для свойства-коллекции драйвер спускается ещё на уровень, и
        /// единицей становится элемент. Без этого окно упиралось бы в такое
        /// свойство целиком, а у толстого объекта оно и есть почти весь
        /// документ: замер прототипа - 255 570 Б против 4206 Б.
        /// </para>
        /// </summary>
        private static void EmitSingleDriver(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            string className,
            JsonGuard guards,
            IReadOnlyCollection<string> servable
            )
        {
            var members = subject.Members.Where(m => m.CanRead).ToList();
            var descents = members
                .Select((member, index) => new { member, index, })
                .Where(m => Descendable(m.member.Value, servable))
                .ToList();

            EmitSingleEntry(builder, subject, injector, className);

            builder.Line("/// <summary>Автомат верхнего уровня: корневой объект " + subject.FullName + ".</summary>");
            builder.OpenBlock(
                "internal sealed class " + className + " : " + StreamReader + "<" + subject.Declaration + ">"
                );

            builder.Line("private readonly " + injector + " _injector;");
            builder.Line();
            builder.Line("private " + subject.Declaration + " _result;");
            builder.Line();
            builder.Line("private int _phase;");
            builder.Line();
            builder.Line("private int _largestProperty;");
            builder.Line();

            foreach (var descent in descents)
            {
                var element = descent.member.Value.Element!;

                builder.Line("private int _largest" + descent.index + ";");
                builder.Line();
                builder.Line(
                    "private " + List + "<" + element.Declaration + ">? _items" + descent.index + ";"
                    );
                builder.Line();
            }

            builder.OpenBlock("internal " + className + "(" + injector + " injector)");
            builder.Line("_injector = injector;");
            builder.Line("_result = default!;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("protected override bool IsDone => _phase == " + Done + ";");
            builder.Line();
            builder.Line("protected override " + subject.Declaration + " Finish() => _result;");
            builder.Line();

            builder.OpenBlock("protected override int ParseWhatFits(scoped " + Span + " json, bool final)");
            builder.Line("var context = new " + Context + "(json);");
            builder.Line();
            builder.OpenBlock("try");
            builder.Line("var position = 0;");
            builder.Line("var consumed = 0;");
            builder.Line();

            builder.OpenBlock("if (_phase == " + BeforeArray + ")");

            if (!subject.IsValueType)
            {
                Wait(builder, TryScan + ".TryReadNull(json, ref position, final, out var isNull)");
                builder.OpenBlock("if (isNull)");
                builder.Line("_phase = " + Done + ";");
                builder.Line("return position;");
                builder.CloseBlock();
                builder.Line();
            }

            Wait(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".OpenBrace, final)");
            builder.Line("_result = " + subject.NewExpression + ";");
            builder.Line("_phase = " + BeforeFirstElement + ";");
            builder.Line("consumed = position;");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("while (true)");

            //пустой объект законен только до первого свойства
            builder.OpenBlock("if (_phase == " + BeforeFirstElement + ")");
            Wait(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".CloseBrace, final, out var empty)");
            builder.OpenBlock("if (empty)");
            builder.Line("_phase = " + Done + ";");
            builder.Line("return position;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("_phase = " + BeforeElement + ";");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (_phase == " + BeforeElement + ")");
            builder.Line("var start = position;");
            builder.Line();
            builder.OpenBlock("if (!final && json.Length - position < _largestProperty)");
            builder.Line("Skipped++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("bool read;");
            builder.Line("int which;");
            builder.Line();
            builder.OpenBlock("try");
            builder.Line(
                "read = " + TryReaderProducer.NameMethod(subject)
                + "(_injector, json, ref position, ref context, final, out which);"
                );
            builder.Line();
            builder.OpenBlock("if (read)");

            if (descents.Count > 0)
            {
                builder.OpenBlock("switch (which)");

                foreach (var descent in descents)
                {
                    var member = descent.member;
                    var element = member.Value.Element!;
                    var index = descent.index;

                    builder.Line("case " + index + ": //" + member.JsonName + " - спуск до элемента");
                    builder.OpenBlock();

                    builder.OpenBlock(
                        "if (!" + TryScan + ".TryReadNull(json, ref position, final, out var absent" + index + "))"
                        );
                    builder.Line("Retries++;");
                    builder.Line("return consumed;");
                    builder.CloseBlock();
                    builder.Line();

                    builder.OpenBlock("if (absent" + index + ")");
                    builder.Line("_result!." + member.MemberName + " = null;");
                    builder.Line("consumed = position;");
                    builder.Line("_phase = " + AfterElement + ";");
                    builder.Line("goto separator;");
                    builder.CloseBlock();
                    builder.Line();

                    builder.OpenBlock(
                        "if (!" + TryScan + ".Expect(json, ref position, " + TryScan + ".OpenBracket, final))"
                        );
                    builder.Line("Retries++;");
                    builder.Line("return consumed;");
                    builder.CloseBlock();
                    builder.Line();

                    //список заводится сразу и сразу же кладётся в объект: дальше
                    //элементы дописываются в него по одному, и объект ни разу не
                    //перестраивается
                    builder.Line("_items" + index + " = new " + List + "<" + element.Declaration + ">();");
                    builder.Line("_result!." + member.MemberName + " = _items" + index + ";");
                    builder.Line("consumed = position;");
                    builder.Line("_phase = " + DescentFirst(descents.IndexOf(descent)) + ";");
                    builder.Line("continue;");

                    builder.CloseBlock();
                    builder.Line();
                }

                builder.CloseBlock();
                builder.Line();
            }

            builder.Line(
                "read = " + TryReaderProducer.ValueMethod(subject)
                + "(_injector, json, ref position, ref context, final, _result!, which);"
                );
            builder.CloseBlock();
            builder.CloseBlock();
            builder.OpenBlock("catch (" + DocumentException + " failure) when (failure.Path is null)");
            builder.Line("throw " + StreamPath + ".Property(failure, json, start);");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (!read)");

            //объект НЕ выбрасывается - в этом вся разница с драйвером массива
            builder.Line("Retries++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("consumed = position;");
            builder.Line("_phase = " + AfterElement + ";");
            builder.Line();
            builder.Line("var size = position - start;");
            builder.OpenBlock("if (size > _largestProperty)");
            builder.Line("_largestProperty = size;");
            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();

            for (var k = 0; k < descents.Count; k++)
            {
                EmitDescent(builder, descents[k].member, descents[k].index, k);
            }

            builder.Unindent();
            builder.Line("separator:");
            builder.Indent();
            builder.Line();

            Wait(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".Comma, final, out var more)");
            builder.OpenBlock("if (more)");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + BeforeElement + ";");
            builder.Line("continue;");
            builder.CloseBlock();
            builder.Line();
            Wait(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".CloseBrace, final)");
            builder.Line("_phase = " + Done + ";");
            builder.Line("return position;");

            builder.CloseBlock();

            builder.CloseBlock();
            builder.OpenBlock("catch (" + DocumentException + " failure) when (failure.Path is null)");
            builder.Line("throw " + StreamPath + ".Outside(failure, json, \"$\");");
            builder.CloseBlock();
            builder.OpenBlock("finally");
            builder.Line("context.Release();");
            builder.CloseBlock();

            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Спуск в свойство-коллекцию: те же три фазы, что у корневого массива,
        /// но элементы дописываются в уже созданный список.
        /// </summary>
        private static void EmitDescent(SourceBuilder builder, MemberModel member, int index, int slot)
        {
            var element = member.Value.Element!;
            var reader = "TryRead_" + element.MethodSuffix;
            var first = DescentFirst(slot);
            var inside = DescentElement(slot);
            var after = DescentAfter(slot);

            builder.OpenBlock("if (_phase == " + first + ")");
            Wait(
                builder,
                TryScan + ".TryConsume(json, ref position, " + TryScan + ".CloseBracket, final, out var none" + index + ")"
                );
            builder.OpenBlock("if (none" + index + ")");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + AfterElement + ";");
            builder.Line("goto separator;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("_phase = " + inside + ";");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("while (_phase == " + inside + " || _phase == " + after + ")");

            builder.OpenBlock("if (_phase == " + inside + ")");
            builder.Line("var elementStart" + index + " = position;");
            builder.Line();
            builder.OpenBlock("if (!final && json.Length - position < _largest" + index + ")");
            builder.Line("Skipped++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("bool got" + index + ";");
            builder.Line();
            builder.OpenBlock("try");
            builder.Line(
                "got" + index + " = " + reader
                + "(_injector, json, ref position, ref context, final, out var item" + index + ");"
                );
            builder.Line();
            builder.OpenBlock("if (got" + index + ")");
            builder.Line("_items" + index + "!.Add(item" + index + "!);");
            builder.CloseBlock();
            builder.CloseBlock();
            builder.OpenBlock("catch (" + DocumentException + " failure) when (failure.Path is null)");

            //путь эталона внутри коллекции - $.lines[1].price: имя свойства и
            //индекс знает драйвер, место внутри элемента - холодный проход
            builder.Line(
                "throw " + StreamPath + ".Inside(failure, json, elementStart" + index + ", \"$."
                + member.JsonName + "[\" + _items" + index
                + "!.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\");"
                );
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (!got" + index + ")");
            builder.Line("Retries++;");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("consumed = position;");
            builder.Line("_phase = " + after + ";");
            builder.Line();
            builder.Line("var length" + index + " = position - elementStart" + index + ";");
            builder.OpenBlock("if (length" + index + " > _largest" + index + ")");
            builder.Line("_largest" + index + " = length" + index + ";");
            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (_phase == " + after + ")");
            Wait(
                builder,
                TryScan + ".TryConsume(json, ref position, " + TryScan + ".Comma, final, out var another" + index + ")"
                );
            builder.OpenBlock("if (another" + index + ")");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + inside + ";");
            builder.Line("continue;");
            builder.CloseBlock();
            builder.Line();
            Wait(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".CloseBracket, final)");
            builder.Line("consumed = position;");
            builder.Line("_phase = " + AfterElement + ";");
            builder.CloseBlock();

            builder.CloseBlock();
            builder.Line();
        }

        //фазы спуска идут тройками после Done
        private static int DescentFirst(int slot) => 5 + (slot * 3);

        private static int DescentElement(int slot) => 6 + (slot * 3);

        private static int DescentAfter(int slot) => 7 + (slot * 3);

        /// <summary>
        /// Спускаться стои́т в список субъектов: элемент там - самостоятельное
        /// значение, которое можно дочитать и положить, не трогая остального.
        /// Массив пришлось бы держать целиком до конца (его длина неизвестна),
        /// словарь - отдельная форма, скаляры того не стоят.
        /// </summary>
        private static bool Descendable(ValueModel value, IReadOnlyCollection<string> servable)
        {
            return (value.Form == ValueForm.List || value.Form == ValueForm.Enumerable)
                && value.Element is not null
                && value.Element.Form == ValueForm.Subject
                && servable.Contains(value.Element.MethodSuffix);
        }

        private static void EmitSingleEntry(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            string className
            )
        {
            builder.Line(
                "internal static async " + Task + "<" + subject.Declaration + "> StreamReadOne_"
                + subject.MethodSuffix + "("
                );
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line(PipeReader + " pipe,");
            builder.Line("int cap = " + StreamReader + "<" + subject.Declaration + ">.DefaultCap,");
            builder.Line(Token + " cancellationToken = default");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            builder.Line("var driver = new " + className + "(injector);");
            builder.Line();
            builder.Line("return await driver.ReadAsync(pipe, cap, cancellationToken).ConfigureAwait(false);");

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Вызов, ложь которого означает «не хватило»: отдать съеденное и ждать
        /// добавки. Отличие от <c>TryReaderProducer.Fail</c> одно, но важное -
        /// возвращается не <c>false</c>, а <b>граница</b>, за которую
        /// откатываться уже не придётся.
        /// </summary>
        private static void Wait(SourceBuilder builder, string call)
        {
            builder.OpenBlock("if (!" + call + ")");
            builder.Line("return consumed;");
            builder.CloseBlock();
            builder.Line();
        }
    }
}
