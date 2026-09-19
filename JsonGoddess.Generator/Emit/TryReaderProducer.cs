using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Печатает <b>потоковый</b> читатель - тот же граф типов, но в форме
    /// «вернуть <c>false</c> вместо отказа, когда байты кончились» (PLAN.md
    /// §12.9, фаза 10, пункт 6б).
    ///
    /// <para>
    /// Отдельным файлом и отдельным producer'ом, а не вторым режимом
    /// <see cref="ClassSourceProducer"/>. Причина не в красоте: обычный
    /// читатель - самое замеренное и самое оттестированное место проекта, а
    /// вторым режимом пришлось бы разветвить в нём каждую печатающую строку.
    /// Здесь же тело <b>структурно повторяет</b> обычное, и всё, что не
    /// зависит от исхода - диспетчер имён, маска обязательных, отложенная
    /// сборка, флаги повторов, - берётся у него же вызовом, а не копией.
    /// </para>
    ///
    /// <para>
    /// <b>Откат безвреден по построению.</b> Недобранное значение выбрасывается
    /// целиком: всё состояние читателя - локальные переменные, а единственное,
    /// что переживает попытку, - арендованные буферы <c>JsonParseContext</c>
    /// (они черновые) и счётчик глубины, который возвращает <c>finally</c>.
    /// </para>
    ///
    /// <para>
    /// Обслуживается не всякий тип. Полиморфный субъект и субъект-коллекция
    /// пока не печатаются, и неспособность заразна: тип, у которого такой тип
    /// членом, тоже не обслуживается. Список посчитан
    /// <see cref="Servable(HostModel)"/> и достаётся форматтеру - тот не
    /// заявляет типов, которых мы не читаем, и работа уходит эталону, а не
    /// приблизительной поддержке.
    /// </para>
    /// </summary>
    public static class TryReaderProducer
    {
        private const string TryScan = "__TryScan";
        private const string TryScanFullName = "global::JsonGoddess.Internal.JsonTryScan";
        private const string Mem = ValueSourceProducer.Mem;
        private const string TokenKind = "global::JsonGoddess.Internal.JsonTokenKind";
        private const string Context = "global::JsonGoddess.JsonParseContext";
        private const string Span = "global::System.ReadOnlySpan<byte>";
        private const string DocumentException = "global::JsonGoddess.JsonDocumentException";
        private const string RequiredSeen = ClassSourceProducer.RequiredSeen;

        /// <summary>
        /// Субъекты, которые потоковый читатель обслуживает.
        ///
        /// <para>
        /// Считается неподвижной точкой, а не одним проходом: «не обслуживается»
        /// распространяется вверх по членам, и один полиморфный тип на дне
        /// графа обязан снять обслуживание со всех, кто до него дотягивается.
        /// </para>
        /// </summary>
        public static HashSet<string> Servable(HostModel host)
        {
            var servable = new HashSet<string>(
                host.Subjects.Where(ServableAlone).Select(s => s.MethodSuffix)
                );

            bool changed;

            do
            {
                changed = false;

                foreach (var subject in host.Subjects)
                {
                    if (!servable.Contains(subject.MethodSuffix))
                    {
                        continue;
                    }

                    if (subject.Members.Any(m => m.CanRead && !ValueIsServable(m.Value, servable)))
                    {
                        servable.Remove(subject.MethodSuffix);
                        changed = true;
                    }
                }
            }
            while (changed);

            return Reachable(host, servable);
        }

        /// <summary>
        /// Из обслуживаемых оставить тех, до кого дотягивается обслуживаемый
        /// <b>корень</b>.
        ///
        /// <para>
        /// Иначе печатался бы мёртвый код, и не в теории: производный тип
        /// полиморфной базы сам по себе неполиморфен, то есть проходит по
        /// первому правилу, - а позвать его потоком некому, к нему ходят только
        /// через базу, которая не обслужена. Потоковый читатель и так удваивает
        /// половину порождаемого кода; печатать в эту половину недостижимое
        /// незачем.
        /// </para>
        /// </summary>
        private static HashSet<string> Reachable(HostModel host, HashSet<string> servable)
        {
            var bySuffix = host.Subjects.ToDictionary(s => s.MethodSuffix);
            var reached = new HashSet<string>();
            var pending = new Stack<string>();

            foreach (var root in host.Subjects.Where(s => s.IsRoot && servable.Contains(s.MethodSuffix)))
            {
                pending.Push(root.MethodSuffix);
            }

            while (pending.Count > 0)
            {
                var suffix = pending.Pop();

                if (!reached.Add(suffix))
                {
                    continue;
                }

                foreach (var member in bySuffix[suffix].Members.Where(m => m.CanRead))
                {
                    foreach (var referenced in Referenced(member.Value))
                    {
                        if (servable.Contains(referenced) && !reached.Contains(referenced))
                        {
                            pending.Push(referenced);
                        }
                    }
                }
            }

            return reached;
        }

        private static IEnumerable<string> Referenced(ValueModel value)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                    yield return value.MethodSuffix;
                    break;

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                case ValueForm.Enumerable:
                    foreach (var nested in Referenced(value.Element!))
                    {
                        yield return nested;
                    }

                    break;
            }
        }

        /// <summary>
        /// Почему тип не обслуживается - словами, для <c>JGD005</c>.
        ///
        /// <para>
        /// Причину называть обязательно: «не обслуживается» без неё
        /// превращается в «неизвестно почему», а это ровно то молчание, против
        /// которого весь §1 плана.
        /// </para>
        /// </summary>
        public static string WhyNotServed(HostModel host, SubjectModel subject, HashSet<string> servable)
        {
            if (subject.IsPolymorphic)
            {
                return "it is polymorphic, and the streaming reader does not print polymorphic readers yet";
            }

            if (subject.CollectionShape is not null)
            {
                return "it is a collection subject, and the streaming reader does not print those yet";
            }

            foreach (var member in subject.Members)
            {
                if (!member.CanRead || ValueIsServable(member.Value, servable))
                {
                    continue;
                }

                var nested = host.Subjects
                    .FirstOrDefault(s => s.MethodSuffix == Offender(member.Value));

                return "its member '" + member.MemberName + "' is of a type that is not served"
                    + (nested is null ? string.Empty : " (" + WhyNotServed(host, nested, servable) + ")");
            }

            return "the root it belongs to is not served";
        }

        private static string? Offender(ValueModel value)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                    return value.MethodSuffix;

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                case ValueForm.Enumerable:
                    return Offender(value.Element!);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Что не печатается <b>само по себе</b>, без оглядки на членов.
        ///
        /// <para>
        /// Полиморфный субъект - потому что его читатель начинается с отката
        /// позиции к дискриминатору и вызова парного читателя; форма рабочая,
        /// но проверить её нечем, пока нет драйвера на такой корень.
        /// Субъект-коллекция - потому что единицей переигрывания у него был бы
        /// элемент, а этого драйвера тоже ещё нет. Оба - работа, а не преграда.
        /// </para>
        /// </summary>
        private static bool ServableAlone(SubjectModel subject)
        {
            return !subject.IsPolymorphic && subject.CollectionShape is null;
        }

        private static bool ValueIsServable(ValueModel value, HashSet<string> servable)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                    return servable.Contains(value.MethodSuffix);

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                case ValueForm.Enumerable:
                    return ValueIsServable(value.Element!, servable);

                default:
                    return true;
            }
        }

        /// <summary>
        /// Второй partial-файл хоста - или <c>null</c>, если обслуживать нечего.
        /// </summary>
        public static string? Produce(HostModel host)
        {
            var servable = Servable(host);

            if (servable.Count == 0)
            {
                return null;
            }

            var builder = new SourceBuilder();

            builder.Line("// <auto-generated/>");
            builder.Line("#pragma warning disable");
            builder.Line("#nullable enable");
            builder.Line();

            builder.Line("using " + TryScan + " = " + TryScanFullName + ";");
            builder.Line("using " + Mem + " = " + ValueSourceProducer.MemoryExtensionsFullName + ";");
            builder.Line();

            if (!string.IsNullOrEmpty(host.Namespace))
            {
                builder.Line("namespace " + host.Namespace + ";");
                builder.Line();
            }

            builder.OpenBlock(host.TypeName);

            foreach (var injector in host.InjectorTypes)
            {
                foreach (var subject in host.Subjects.Where(s => servable.Contains(s.MethodSuffix)))
                {
                    EmitSubjectReader(builder, subject, injector, host.Guards, host.MaxDepth, host.Features);

                    //половинки поимущественного чтения печатаются только
                    //корню: спускаться по свойствам имеет смысл там, где
                    //объект и есть весь документ
                    if (subject.IsRoot && ReadsByProperty(subject, host.Guards))
                    {
                        var members = subject.Members.Where(m => m.CanRead).ToList();

                        EmitNameReader(builder, subject, injector, members, host.Guards, host.Features);
                        EmitPropertyValueReader(
                            builder, subject, injector, members, host.Guards, host.MaxDepth, host.Features
                            );
                    }
                }

                foreach (var scalar in host.Scalars)
                {
                    EmitScalarReader(builder, scalar, injector, host.Guards, host.Features);
                }

                foreach (var collection in host.Collections.Where(c => ValueIsServable(c, servable)))
                {
                    EmitCollectionReader(builder, collection, injector, host.Guards, host.MaxDepth, host.Features);
                }

                foreach (var enumModel in host.StringEnums)
                {
                    EmitEnumReader(builder, enumModel, injector, host.Guards, host.Features);
                }
            }

            StreamDriverProducer.Emit(builder, host, servable);

            builder.CloseBlock();

            return builder.ToString();
        }

        /// <summary>
        /// Имя <c>Try</c>-метода. Приставка одна на всё семейство, чтобы имя
        /// само говорило, какой читатель зовут: в одном классе живут оба.
        /// </summary>
        public static string SubjectMethod(SubjectModel subject) => "TryRead_" + subject.MethodSuffix;

        private static string ValueMethod(ValueModel value)
        {
            switch (value.Form)
            {
                case ValueForm.Subject:
                    return "TryRead_" + value.MethodSuffix;

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Dictionary:
                case ValueForm.Enumerable:
                    return "TryReadCollection_" + value.MethodSuffix;

                case ValueForm.Enum:
                    return "TryReadEnum_" + value.MethodSuffix;

                default:
                    return "TryReadScalar_" + BuiltinTypes.MethodSuffix(value.Builtin, value.IsNullable);
            }
        }

        /// <summary>
        /// Подпись у всего семейства одна: к обычной добавлены
        /// <c>final</c> (труба закрыта) и <c>out</c> на результат, потому что
        /// возвращаемое значение занято исходом «хватило ли байт».
        /// </summary>
        private static void EmitSignature(
            SourceBuilder builder, string name, string injector, string resultType
            )
        {
            builder.Line("internal static bool " + name + "(");
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context,");
            builder.Line("bool final,");
            builder.Line("out " + resultType + " value");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();
        }

        /// <summary>
        /// Пропуск пробелов и комментариев - печатается по тому же правилу, что
        /// и у обычного читателя: без <c>JsonFeature.Comments</c> не печатается
        /// вовсе, потому что следующий вызов сканера скользит по пробелам сам.
        /// </summary>
        private static void EmitTrivia(SourceBuilder builder, JsonFeature features)
        {
            if ((features & JsonFeature.Comments) == 0)
            {
                return;
            }

            Fail(builder, TryScan + ".SkipWhitespaceAndComments(json, ref position, final)");
        }

        /// <summary>
        /// Вызов, у которого ложь означает «не хватило»: сразу наверх, к тому,
        /// кто переиграет попытку.
        /// </summary>
        private static void Fail(SourceBuilder builder, string call)
        {
            builder.OpenBlock("if (!" + call + ")");
            builder.Line("return false;");
            builder.CloseBlock();
            builder.Line();
        }

        private static string StringRead(JsonGuard guards)
        {
            return TryScan + ((guards & JsonGuard.ControlCharsInStrings) != 0
                ? ".ReadStringContentStrict"
                : ".ReadStringContent");
        }

        private static string NumberRead(JsonGuard guards)
        {
            return TryScan + ((guards & JsonGuard.StrictNumbers) != 0
                ? ".ReadNumberRawStrict"
                : ".ReadNumberRaw");
        }

        /// <summary>
        /// <c>JsonFeature.TrailingCommas</c>: та же проверка, что у обычного
        /// читателя, но <c>Peek</c> теперь тоже двухисходный.
        /// </summary>
        private static void EmitTrailingCommaCheck(SourceBuilder builder, JsonFeature features, string endTokenKind)
        {
            if ((features & JsonFeature.TrailingCommas) == 0)
            {
                return;
            }

            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".Peek(json, ref position, final, out var __end)");
            builder.OpenBlock("if (__end == " + endTokenKind + ")");
            builder.Line("break;");
            builder.CloseBlock();
        }

        private static void EmitDepthOpen(SourceBuilder builder, bool guardsDepth, int maxDepth)
        {
            if (!guardsDepth)
            {
                return;
            }

            builder.Line("context.Depth++;");
            builder.OpenBlock("if (context.Depth > " + maxDepth + ")");
            builder.Line(
                "throw new " + DocumentException + "(\"The maximum configured depth of " + maxDepth
                + " has been exceeded.\", position);"
                );
            builder.CloseBlock();

            //try/finally здесь делает ещё одну работу сверх обычного читателя:
            //возврат лжи - тоже выход из метода, и без finally недосчитанная
            //глубина уехала бы в следующую попытку, отказав на вложенности, до
            //которой документ не доходил
            builder.OpenBlock("try");
        }

        private static void EmitDepthClose(SourceBuilder builder, bool guardsDepth)
        {
            if (!guardsDepth)
            {
                return;
            }

            builder.CloseBlock();
            builder.OpenBlock("finally");
            builder.Line("context.Depth--;");
            builder.CloseBlock();
        }

        private static void EmitSubjectReader(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            JsonGuard guards,
            int maxDepth,
            JsonFeature features
            )
        {
            var members = subject.Members.Where(m => m.CanRead).ToList();
            var required = members.Where(m => m.IsRequired).ToList();
            var deferred = subject.NeedsDeferredConstruction;
            var guardsDepth = (guards & JsonGuard.MaxDepth) != 0;

            EmitSignature(builder, SubjectMethod(subject), injector, subject.Declaration);

            builder.Line("value = default!;");
            builder.Line();

            EmitTrivia(builder, features);

            if (!subject.IsValueType)
            {
                Fail(builder, TryScan + ".TryReadNull(json, ref position, final, out var __null)");
                builder.OpenBlock("if (__null)");
                builder.Line("return true;");
                builder.CloseBlock();
                builder.Line();
            }

            Fail(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".OpenBrace, final)");
            EmitDepthOpen(builder, guardsDepth, maxDepth);

            if (deferred)
            {
                ClassSourceProducer.EmitDeferredLocals(builder, subject, members);
            }
            else
            {
                builder.Line("var result = " + subject.NewExpression + ";");
                builder.Line();
            }

            if ((guards & JsonGuard.DuplicateProperties) != 0)
            {
                foreach (var member in members)
                {
                    builder.Line("var " + ClassSourceProducer.DupSeen(member) + " = false;");
                }

                if (members.Count > 0)
                {
                    builder.Line();
                }
            }

            if (required.Count > 0)
            {
                builder.Line("var " + RequiredSeen + " = 0UL;");
                builder.Line();
            }

            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".CloseBrace, final, out var __empty)");
            builder.OpenBlock("if (__empty)");
            ClassSourceProducer.EmitRequiredCheck(builder, subject, required);
            builder.Line("value = " + (deferred ? ClassSourceProducer.Construct(subject, members) : "result") + ";");
            builder.Line("return true;");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("while (true)");

            EmitTrivia(builder, features);
            Fail(builder, StringRead(guards) + "(json, ref position, final, out var name, out var nameEscaped)");

            //между именем и двоеточием комментарий не пропускается никогда -
            //ровно как у обычного читателя, и по той же пробе
            Fail(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".Colon, final)");

            if (members.Count > 0)
            {
                builder.Unindent();
                builder.Line("dispatch:");
                builder.Indent();

                var temps = new Temps();

                NameDispatcher.Emit(
                    builder,
                    members,
                    features,
                    member =>
                    {
                        if ((guards & JsonGuard.DuplicateProperties) != 0)
                        {
                            builder.OpenBlock("if (" + ClassSourceProducer.DupSeen(member) + ")");
                            builder.Line(
                                "throw new " + DocumentException + "(\"Duplicate property '"
                                + member.JsonName + "'.\", position);"
                                );
                            builder.CloseBlock();
                            builder.Line(ClassSourceProducer.DupSeen(member) + " = true;");
                        }

                        if (member.IsRequired)
                        {
                            builder.Line(
                                RequiredSeen + " |= 0x"
                                + ClassSourceProducer.RequiredBit(required, member).ToString("X") + "UL;"
                                );
                        }

                        EmitValueRead(builder, member.Value, ClassSourceProducer.Target(member, deferred), temps);

                        if (deferred && !member.IsConstructorParameter)
                        {
                            builder.Line(ClassSourceProducer.Seen(member) + " = true;");
                        }
                    }
                    );

                builder.OpenBlock("if (nameEscaped)");
                builder.Line("nameEscaped = false;");
                builder.Line(
                    "name = context.UnescapeName(name"
                    + ((guards & JsonGuard.InvalidUtf8) != 0 ? ", true" : string.Empty) + ");"
                    );
                builder.Line("goto dispatch;");
                builder.CloseBlock();
                builder.Line();
            }

            if ((guards & JsonGuard.UnknownProperties) != 0)
            {
                builder.Line(
                    "throw new " + DocumentException
                    + "(\"Unknown property '\" + global::System.Text.Encoding.UTF8.GetString(name.ToArray())"
                    + " + \"'.\", position);"
                    );
                builder.Line();
            }
            else if (guardsDepth)
            {
                Fail(
                    builder,
                    TryScan + ".SkipValueGuarded(json, ref position, ref context.Depth, " + maxDepth + ", final)"
                    );
            }
            else
            {
                Fail(builder, TryScan + ".SkipValue(json, ref position, final)");
            }

            builder.Unindent();
            builder.Line("next:");
            builder.Indent();
            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".Comma, final, out var __more)");
            builder.OpenBlock("if (!__more)");
            builder.Line("break;");
            builder.CloseBlock();

            EmitTrailingCommaCheck(builder, features, TokenKind + ".EndObject");

            builder.CloseBlock();
            builder.Line();

            Fail(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".CloseBrace, final)");

            ClassSourceProducer.EmitRequiredCheck(builder, subject, required);

            if (deferred)
            {
                builder.Line("var result = " + ClassSourceProducer.Construct(subject, members) + ";");

                foreach (var member in members.Where(m => !m.IsConstructorParameter && !m.IsRequired))
                {
                    builder.OpenBlock("if (" + ClassSourceProducer.Seen(member) + ")");
                    builder.Line("result." + member.MemberName + " = " + ClassSourceProducer.Assigned(member) + ";");
                    builder.CloseBlock();
                }

                builder.Line();
            }

            builder.Line("value = result;");
            builder.Line("return true;");

            EmitDepthClose(builder, guardsDepth);

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Годится ли субъект на <b>поимущественное</b> чтение - то, при
        /// котором единицей переигрывания становится свойство, а сам объект
        /// живёт в состоянии драйвера и не выбрасывается.
        ///
        /// <para>
        /// Отложенная сборка это исключает: пока не прочитано всё, объекта не
        /// существует - класть свойство некуда. Страж повторов тоже: его флаги
        /// живут в теле читателя, а тут тела нет, есть отдельные вызовы.
        /// Оба случая не отказ, а возврат к прежней единице - объекту целиком.
        /// </para>
        /// </summary>
        public static bool ReadsByProperty(SubjectModel subject, JsonGuard guards)
        {
            return !subject.NeedsDeferredConstruction
                && (guards & JsonGuard.DuplicateProperties) == 0;
        }

        public static string NameMethod(SubjectModel subject) => "TryReadName_" + subject.MethodSuffix;

        public static string ValueMethod(SubjectModel subject) => "TryReadValue_" + subject.MethodSuffix;

        /// <summary>
        /// Имя свойства и двоеточие, плюс номер попавшегося члена
        /// (<c>-1</c> - незнакомое имя).
        ///
        /// <para>
        /// Отдельно от значения не ради красоты: драйвер одиночного объекта,
        /// узнав член, иногда берёт разбор его значения на себя - чтобы единицей
        /// переигрывания стал элемент коллекции, а не свойство целиком. На
        /// толстом объекте это разница между окном в весь документ и окном в
        /// один элемент.
        /// </para>
        /// </summary>
        private static void EmitNameReader(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            List<MemberModel> members,
            JsonGuard guards,
            JsonFeature features
            )
        {
            builder.Line("internal static bool " + NameMethod(subject) + "(");
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context,");
            builder.Line("bool final,");
            builder.Line("out int which");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            builder.Line("which = -1;");
            builder.Line();

            EmitTrivia(builder, features);
            Fail(builder, StringRead(guards) + "(json, ref position, final, out var name, out var nameEscaped)");
            Fail(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".Colon, final)");

            if (members.Count > 0)
            {
                builder.Unindent();
                builder.Line("dispatch:");
                builder.Indent();

                NameDispatcher.Emit(
                    builder,
                    members,
                    features,
                    member => builder.Line(
                        "which = " + members.IndexOf(member).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";"
                        )
                    );

                builder.OpenBlock("if (nameEscaped)");
                builder.Line("nameEscaped = false;");
                builder.Line(
                    "name = context.UnescapeName(name"
                    + ((guards & JsonGuard.InvalidUtf8) != 0 ? ", true" : string.Empty) + ");"
                    );
                builder.Line("goto dispatch;");
                builder.CloseBlock();
                builder.Line();

                builder.Unindent();
                builder.Line("next:");
                builder.Indent();
            }

            builder.Line("return true;");

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Значение члена по его номеру - вторая половина поимущественного
        /// чтения.
        /// </summary>
        private static void EmitPropertyValueReader(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            IReadOnlyList<MemberModel> members,
            JsonGuard guards,
            int maxDepth,
            JsonFeature features
            )
        {
            builder.Line("internal static bool " + ValueMethod(subject) + "(");
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context,");
            builder.Line("bool final,");
            builder.Line(subject.FullName + " result,");
            builder.Line("int which");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            builder.OpenBlock("switch (which)");

            var temps = new Temps();

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                builder.Line("case " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": //" + member.JsonName);
                builder.OpenBlock();
                EmitValueRead(builder, member.Value, "result." + member.MemberName, temps);
                builder.Line("return true;");
                builder.CloseBlock();
                builder.Line();
            }

            builder.Line("default:");
            builder.OpenBlock();

            if ((guards & JsonGuard.UnknownProperties) != 0)
            {
                builder.Line(
                    "throw new " + DocumentException
                    + "(\"Unknown property.\", position);"
                    );
            }
            else if ((guards & JsonGuard.MaxDepth) != 0)
            {
                builder.Line(
                    "return " + TryScan + ".SkipValueGuarded(json, ref position, ref context.Depth, "
                    + maxDepth + ", final);"
                    );
            }
            else
            {
                builder.Line("return " + TryScan + ".SkipValue(json, ref position, final);");
            }

            builder.CloseBlock();

            builder.CloseBlock();

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Чтение значения в уже объявленную цель - <c>Try</c>-двойник
        /// <c>ValueSourceProducer.ReadValue</c>.
        ///
        /// <para>
        /// Временная локальная нужна потому, что результат приезжает через
        /// <c>out</c>, а цель бывает свойством: <c>out result.Id</c> язык не
        /// принимает.
        /// </para>
        /// </summary>
        private static void EmitValueRead(SourceBuilder builder, ValueModel value, string target, Temps temps)
        {
            if (value.Form == ValueForm.Subject && value.IsValueType && value.IsNullable)
            {
                //null на месте Nullable<структуры> законен, а Try-читатель
                //структуры вернуть его не может - снимается здесь, как и у
                //обычного читателя
                var flag = temps.Next();
                Fail(builder, TryScan + ".TryReadNull(json, ref position, final, out var " + flag + ")");
                builder.OpenBlock("if (" + flag + ")");
                builder.Line(target + " = null;");
                builder.CloseBlock();
                builder.OpenBlock("else");
                EmitPlainRead(builder, value, target, temps);
                builder.CloseBlock();
                return;
            }

            if (value.Form == ValueForm.Enum && value.IsNullable)
            {
                var flag = temps.Next();
                Fail(builder, TryScan + ".TryReadNull(json, ref position, final, out var " + flag + ")");
                builder.OpenBlock("if (" + flag + ")");
                builder.Line(target + " = null;");
                builder.CloseBlock();
                builder.OpenBlock("else");
                EmitPlainRead(builder, value, target, temps);
                builder.CloseBlock();
                return;
            }

            EmitPlainRead(builder, value, target, temps);
        }

        private static void EmitPlainRead(SourceBuilder builder, ValueModel value, string target, Temps temps)
        {
            var slot = temps.Next();

            Fail(
                builder,
                ValueMethod(value) + "(injector, json, ref position, ref context, final, out var " + slot + ")"
                );

            //числовой enum читается читателем подлежащего типа, и приведение -
            //единственное, чем он от него отличается
            var cast = value.Form == ValueForm.Enum && !value.IsStringEnum
                ? "(" + value.TypeName + ")"
                : string.Empty;

            builder.Line(target + " = " + cast + slot + ";");
        }

        /// <summary>
        /// Читатель скаляра. Тело - двойник
        /// <c>ValueSourceProducer.ReadScalarBody</c>.
        /// </summary>
        private static void EmitScalarReader(
            SourceBuilder builder, ValueModel scalar, string injector, JsonGuard guards, JsonFeature features
            )
        {
            EmitSignature(
                builder, "TryReadScalar_" + scalar.MethodSuffix, injector, scalar.Declaration
                );

            builder.Line("value = default!;");
            builder.Line();

            EmitTrivia(builder, features);

            if (scalar.IsNullable)
            {
                Fail(builder, TryScan + ".TryReadNull(json, ref position, final, out var __null)");
                builder.OpenBlock("if (__null)");
                builder.Line("return true;");
                builder.CloseBlock();
                builder.Line();
            }

            var typeName = BuiltinTypes.GetTypeName(scalar.Builtin);

            switch (BuiltinTypes.GetLexeme(scalar.Builtin))
            {
                case LexemeKind.Number:
                {
                    EmitNumberScalarBody(builder, scalar, guards, features, typeName);
                    break;
                }

                case LexemeKind.Literal:
                {
                    Fail(builder, TryScan + ".ReadLiteralRaw(json, ref position, final, out var raw)");
                    builder.Line("injector.Parse(ref context, raw, out " + typeName + " parsed);");
                    break;
                }

                default:
                {
                    Fail(builder, StringRead(guards) + "(json, ref position, final, out var raw, out var rawEscaped)");

                    if (scalar.Builtin == BuiltinKind.String && (guards & JsonGuard.InvalidUtf8) != 0)
                    {
                        builder.Line(ValueSourceProducer.StringDecoder + ".EnsureValidUtf8(raw, rawEscaped);");
                    }

                    builder.Line("injector.ParseText(ref context, raw, rawEscaped, out " + typeName + " parsed);");
                    break;
                }
            }

            builder.Line("value = parsed;");
            builder.Line("return true;");

            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitNumberScalarBody(
            SourceBuilder builder, ValueModel value, JsonGuard guards, JsonFeature features, string typeName
            )
        {
            var wantsNamedFloat = (value.Builtin == BuiltinKind.Single || value.Builtin == BuiltinKind.Double)
                && (features & JsonFeature.NamedFloatingPointLiterals) != 0;
            var wantsFromString = (features & JsonFeature.NumbersFromStrings) != 0;

            if (!wantsNamedFloat && !wantsFromString)
            {
                Fail(builder, NumberRead(guards) + "(json, ref position, final, out var raw)");
                builder.Line("injector.Parse(ref context, raw, out " + typeName + " parsed);");
                return;
            }

            builder.Line(typeName + " parsed;");
            Fail(builder, TryScan + ".Peek(json, ref position, final, out var __kind)");
            builder.OpenBlock("if (__kind == " + TokenKind + ".String)");

            Fail(builder, StringRead(guards) + "(json, ref position, final, out var raw, out var rawEscaped)");
            builder.OpenBlock("if (rawEscaped)");
            builder.Line(
                "raw = context.UnescapeValue(raw" + ((guards & JsonGuard.InvalidUtf8) != 0 ? ", true" : string.Empty) + ");"
                );
            builder.CloseBlock();
            builder.Line();

            if (wantsNamedFloat)
            {
                builder.OpenBlock("if (global::JsonGoddess.Internal.JsonNamedFloat.TryParse(raw, out var named))");
                builder.Line("parsed = (" + typeName + ")named;");
                builder.CloseBlock();
                builder.OpenBlock("else");

                if (wantsFromString)
                {
                    builder.Line("injector.Parse(ref context, raw, out parsed);");
                }
                else
                {
                    builder.Line("throw new " + DocumentException + "(\"Expected a number.\", position);");
                }

                builder.CloseBlock();
            }
            else
            {
                builder.Line("injector.Parse(ref context, raw, out parsed);");
            }

            builder.CloseBlock();
            builder.OpenBlock("else");
            Fail(builder, NumberRead(guards) + "(json, ref position, final, out var rawNumber)");
            builder.Line("injector.Parse(ref context, rawNumber, out parsed);");
            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>Читатель строкового enum'а - двойник <c>EmitEnumReader</c>.</summary>
        private static void EmitEnumReader(
            SourceBuilder builder, EnumModel enumModel, string injector, JsonGuard guards, JsonFeature features
            )
        {
            var underlying = BuiltinTypes.GetTypeName(enumModel.Underlying);

            EmitSignature(builder, "TryReadEnum_" + enumModel.MethodSuffix, injector, enumModel.FullName);

            builder.Line("value = default;");
            builder.Line();

            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".Peek(json, ref position, final, out var __kind)");
            builder.OpenBlock("if (__kind == " + TokenKind + ".String)");

            Fail(builder, StringRead(guards) + "(json, ref position, final, out var raw, out var rawEscaped)");
            builder.OpenBlock("if (rawEscaped)");
            builder.Line(
                "raw = context.UnescapeValue(raw" + ((guards & JsonGuard.InvalidUtf8) != 0 ? ", true" : string.Empty) + ");"
                );
            builder.CloseBlock();
            builder.Line();

            var buckets = enumModel.Members
                .GroupBy(m => System.Text.Encoding.UTF8.GetByteCount(m.JsonName))
                .OrderBy(g => g.Key);

            builder.OpenBlock("switch (raw.Length)");

            foreach (var bucket in buckets)
            {
                builder.Line("case " + bucket.Key + ":");
                builder.OpenBlock();

                foreach (var member in bucket)
                {
                    var comparison = member.MatchExactly
                        ? Mem + ".SequenceEqual(raw, " + SourceBuilder.Utf8Literal(member.JsonName) + ")"
                        : "global::JsonGoddess.Internal.JsonAsciiName.EqualsIgnoreCase(raw, "
                            + SourceBuilder.Utf8Literal(member.JsonName) + ")";

                    builder.OpenBlock("if (" + comparison + ")");
                    builder.Line("value = " + enumModel.FullName + "." + member.MemberName + ";");
                    builder.Line("return true;");
                    builder.CloseBlock();
                    builder.Line();
                }

                builder.Line("break;");
                builder.CloseBlock();
                builder.Line();
            }

            builder.CloseBlock();
            builder.Line();

            builder.Line("injector.Parse(ref context, raw, out " + underlying + " named);");
            builder.Line("value = (" + enumModel.FullName + ")named;");
            builder.Line("return true;");

            builder.CloseBlock();
            builder.Line();

            Fail(builder, NumberRead(guards) + "(json, ref position, final, out var rawNumber)");
            builder.Line("injector.Parse(ref context, rawNumber, out " + underlying + " number);");
            builder.Line("value = (" + enumModel.FullName + ")number;");
            builder.Line("return true;");

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>Читатель коллекции - двойник <c>EmitCollectionReader</c>.</summary>
        private static void EmitCollectionReader(
            SourceBuilder builder,
            ValueModel collection,
            string injector,
            JsonGuard guards,
            int maxDepth,
            JsonFeature features
            )
        {
            var isArray = collection.Form == ValueForm.Array;
            var isMap = collection.Form == ValueForm.Dictionary;
            var element = collection.Element!;
            var guardsDepth = (guards & JsonGuard.MaxDepth) != 0;

            var open = TryScan + (isMap ? ".OpenBrace" : ".OpenBracket");
            var close = TryScan + (isMap ? ".CloseBrace" : ".CloseBracket");

            EmitSignature(
                builder, "TryReadCollection_" + collection.MethodSuffix, injector, collection.TypeName + "?"
                );

            builder.Line("value = null;");
            builder.Line();

            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".TryReadNull(json, ref position, final, out var __null)");
            builder.OpenBlock("if (__null)");
            builder.Line("return true;");
            builder.CloseBlock();
            builder.Line();

            Fail(builder, TryScan + ".Expect(json, ref position, " + open + ", final)");
            EmitDepthOpen(builder, guardsDepth, maxDepth);

            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".TryConsume(json, ref position, " + close + ", final, out var __empty)");
            builder.OpenBlock("if (__empty)");
            builder.Line(
                "value = " + (isArray
                    ? ValueSourceProducer.Array + ".Empty<" + element.Declaration + ">()"
                    : "new " + collection.ConstructTypeName + "()")
                + ";"
                );
            builder.Line("return true;");
            builder.CloseBlock();
            builder.Line();

            if (isArray)
            {
                builder.Line("var result = new " + element.Declaration + "[4];");
                builder.Line("var count = 0;");
            }
            else
            {
                builder.Line("var result = new " + collection.ConstructTypeName + "();");
            }

            builder.Line();
            builder.OpenBlock("while (true)");

            if (isMap)
            {
                EmitTrivia(builder, features);
                Fail(builder, StringRead(guards) + "(json, ref position, final, out var rawKey, out var keyEscaped)");

                if ((guards & JsonGuard.InvalidUtf8) != 0)
                {
                    builder.Line(ValueSourceProducer.StringDecoder + ".EnsureValidUtf8(rawKey, keyEscaped);");
                }

                builder.Line("injector.ParseText(ref context, rawKey, keyEscaped, out string key);");
                Fail(builder, TryScan + ".Expect(json, ref position, " + TryScan + ".Colon, final)");
            }

            builder.Line(element.Declaration + " item;");
            EmitValueRead(builder, element, "item", new Temps());
            builder.Line();

            if (isArray)
            {
                builder.OpenBlock("if (count == result.Length)");
                builder.Line(ValueSourceProducer.Array + ".Resize(ref result, count * 2);");
                builder.CloseBlock();
                builder.Line();
                builder.Line("result[count] = item;");
                builder.Line("count++;");
            }
            else if (isMap)
            {
                builder.Line("result[key] = item;");
            }
            else
            {
                builder.Line("result.Add(item);");
            }

            builder.Line();
            EmitTrivia(builder, features);
            Fail(builder, TryScan + ".TryConsume(json, ref position, " + TryScan + ".Comma, final, out var __more)");
            builder.OpenBlock("if (!__more)");
            builder.Line("break;");
            builder.CloseBlock();

            EmitTrailingCommaCheck(builder, features, TokenKind + (isMap ? ".EndObject" : ".EndArray"));

            builder.CloseBlock();
            builder.Line();

            Fail(builder, TryScan + ".Expect(json, ref position, " + close + ", final)");

            if (isArray)
            {
                builder.OpenBlock("if (count != result.Length)");
                builder.Line(ValueSourceProducer.Array + ".Resize(ref result, count);");
                builder.CloseBlock();
                builder.Line();
            }

            builder.Line("value = result;");
            builder.Line("return true;");

            EmitDepthClose(builder, guardsDepth);

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Имена временных. Счётчик один на метод: два чтения в одной области
        /// видимости обязаны получить разные имена, а вложенное чтение внутри
        /// <c>else</c>-ветки - тем более.
        /// </summary>
        private sealed class Temps
        {
            private int _count;

            internal string Next()
            {
                _count++;
                return "__v" + _count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
