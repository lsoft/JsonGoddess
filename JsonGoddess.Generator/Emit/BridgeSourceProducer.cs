using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Model;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Читатель для моста (§10, маршрут B): разбор прямо из
    /// <c>Utf8JsonReader</c>.
    ///
    /// <para>
    /// Второй читатель в проекте, и это не дублирование, а разные входные
    /// данные. <see cref="ClassSourceProducer"/> печатает разбор
    /// <c>ReadOnlySpan&lt;byte&gt;</c> своим сканером - он владеет документом
    /// целиком и волен ходить по нему как угодно. Здесь документ нам не
    /// принадлежит: мы вызваны изнутри конвейера эталона, получили его
    /// читатель в середине работы и обязаны вернуть его ровно на закрывающем
    /// токене нашего значения.
    /// </para>
    ///
    /// <para>
    /// Замер (§12.8) объясняет, зачем второй читатель вообще написан.
    /// Симметричный записи способ - добыть у читателя сырой кусок и отдать его
    /// сканеру - обязан сперва пройти значение целиком, чтобы узнать, где оно
    /// кончается, и документ читается дважды: 1062 ns против 1063 ns у самого
    /// эталона, то есть мост ровно бесполезен. Разбор по токенам платит за
    /// токенизацию один раз: 635 ns, а на массиве из десяти - 6354 ns против
    /// 11863 ns.
    /// </para>
    ///
    /// <para>
    /// <b>Стражей здесь нет ни одного</b>, и это не упущение. Управляющий
    /// символ в строке, негодный UTF-8, число не по грамматике JSON,
    /// превышенная глубина, висячая запятая, мусор после документа - всё это
    /// отвергает сам <c>Utf8JsonReader</c>, до того как управление дойдёт до
    /// нас. Причём отвергает не «так же, как эталон», а буквально им и
    /// является. Написать свою проверку значило бы завести второе мнение там,
    /// где нужно ровно одно.
    /// </para>
    /// </summary>
    public static class BridgeSourceProducer
    {
        private const string Reader = "ref global::System.Text.Json.Utf8JsonReader reader";
        private const string TokenType = "global::System.Text.Json.JsonTokenType";
        private const string Read = "global::JsonGoddess.Compat.Interop.BridgeRead";
        private const string Mem = "global::System.MemoryExtensions";
        private const string RequiredSeen = "requiredSeen";

        /// <summary>
        /// Псевдонимы, на которые рассчитывает общий диспетчер имён. В C# они
        /// файловой области, поэтому объявляются заново в каждом файле, где
        /// диспетчер печатается.
        /// </summary>
        public static void EmitAliases(SourceBuilder builder)
        {
            builder.Line("using " + ValueSourceProducer.Mem + " = " + ValueSourceProducer.MemoryExtensionsFullName + ";");
            builder.Line();
        }

        /// <summary>
        /// Можно ли обслужить субъект мостом. Сказать «нет» здесь ничего не
        /// стоит: тип просто не попадёт в мост и продолжит работать через
        /// эталон - ровно так же, как при отказе маршрута A.
        /// </summary>
        public static bool CanServe(SubjectModel subject)
        {
            //Полиморфизм и субъект-коллекция ждут своей очереди: у обоих
            //чтение устроено иначе, и делать их заодно значило бы отложить
            //всё остальное.
            if (subject.IsPolymorphic || subject.CollectionShape is not null)
            {
                return false;
            }

            return subject.Members.Where(m => m.CanRead).All(m => CanServe(m.Value));
        }

        private static bool CanServe(ValueModel value)
        {
            switch (value.Form)
            {
                case ValueForm.Builtin:
                case ValueForm.Enum:
                case ValueForm.Subject:
                    return true;

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Enumerable:
                case ValueForm.Dictionary:
                    return value.Element is not null && CanServe(value.Element);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Читатели всех обслуживаемых субъектов. Печатаются в тот же
        /// частичный класс, где лежит порождённый писатель: регистрации нужны
        /// оба, и разносить их по типам незачем.
        /// </summary>
        public static void Emit(SourceBuilder builder, IReadOnlyList<SubjectModel> subjects)
        {
            foreach (var subject in subjects)
            {
                EmitSubjectReader(builder, subject);
            }

            //Коллекции и строковые enum'ы печатаются по одному разу на форму, а
            //не на член: List<int> у трёх членов - это один читатель. Порядок
            //по имени метода, а не по порядку встречи: текст порождаемого кода
            //не должен зависеть от того, чей член попался первым.
            var values = subjects
                .SelectMany(s => s.Members.Where(m => m.CanRead))
                .Select(m => m.Value)
                .SelectMany(Unfold)
                .ToList();

            foreach (var collection in values
                .Where(v => v.IsCollection)
                .GroupBy(v => v.MethodSuffix)
                .Select(g => g.First())
                .OrderBy(v => v.MethodSuffix, System.StringComparer.Ordinal))
            {
                EmitCollectionReader(builder, collection);
            }

            foreach (var enumModel in values
                .Where(v => v is { Form: ValueForm.Enum, IsStringEnum: true, })
                .Select(v => v.Enum!)
                .GroupBy(e => e.MethodSuffix)
                .Select(g => g.First())
                .OrderBy(e => e.MethodSuffix, System.StringComparer.Ordinal))
            {
                EmitStringEnumReader(builder, enumModel);
            }
        }

        /// <summary>
        /// Значение и всё, что внутри него: <c>List&lt;Dictionary&lt;string,
        /// Mood[]&gt;&gt;</c> требует читателя на каждый уровень.
        /// </summary>
        private static IEnumerable<ValueModel> Unfold(ValueModel value)
        {
            yield return value;

            if (value.Element is null)
            {
                yield break;
            }

            foreach (var inner in Unfold(value.Element))
            {
                yield return inner;
            }
        }

        /// <summary>
        /// Читатель коллекции. Список, массив и последовательность собираются
        /// одинаково - через <c>List&lt;T&gt;</c>, - и различаются только тем,
        /// что отдают: массиву нужен <c>ToArray</c>, остальным довольно самого
        /// списка, который и есть искомый тип либо законно им притворяется.
        /// </summary>
        private static void EmitCollectionReader(SourceBuilder builder, ValueModel value)
        {
            var element = value.Element!;

            builder.Line(
                "internal static " + value.TypeName + "? BridgeRead_" + value.MethodSuffix + "(" + Reader + ")"
                );
            builder.OpenBlock();

            builder.OpenBlock("if (reader.TokenType == " + TokenType + ".Null)");
            builder.Line("return null;");
            builder.CloseBlock();
            builder.Line();

            if (value.Form == ValueForm.Dictionary)
            {
                EmitDictionaryBody(builder, value, element);
            }
            else
            {
                EmitListBody(builder, value, element);
            }

            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitListBody(SourceBuilder builder, ValueModel value, ValueModel element)
        {
            builder.Line(Read + ".ExpectStartArray(ref reader, typeof(" + value.TypeName + "));");
            builder.Line();
            builder.Line(
                "var items = new global::System.Collections.Generic.List<" + element.Declaration + ">();"
                );
            builder.Line();

            builder.OpenBlock("while (true)");
            builder.Line("reader.Read();");
            builder.OpenBlock("if (reader.TokenType == " + TokenType + ".EndArray)");
            builder.Line("break;");
            builder.CloseBlock();
            builder.Line();
            builder.Line("items.Add(" + ValueExpression(element) + ");");
            builder.CloseBlock();
            builder.Line();

            builder.Line(value.Form == ValueForm.Array ? "return items.ToArray();" : "return items;");
        }

        private static void EmitDictionaryBody(SourceBuilder builder, ValueModel value, ValueModel element)
        {
            builder.Line(Read + ".ExpectStartObject(ref reader, typeof(" + value.TypeName + "));");
            builder.Line();
            builder.Line(
                "var map = new global::System.Collections.Generic.Dictionary<string, "
                + element.Declaration + ">();"
                );
            builder.Line();

            builder.OpenBlock("while (true)");
            builder.Line("reader.Read();");
            builder.OpenBlock("if (reader.TokenType == " + TokenType + ".EndObject)");
            builder.Line("break;");
            builder.CloseBlock();
            builder.Line();

            //Ключ словаря - строка документа, и брать её надо через GetString:
            //он разэкранирует и соберёт разрезанное имя. Сырые байты здесь не
            //годятся ни при каких условиях - имя тут произвольное, а не из
            //заранее известного списка.
            builder.Line("var key = reader.GetString()!;");
            builder.Line("reader.Read();");
            builder.Line("map[key] = " + ValueExpression(element) + ";");
            builder.CloseBlock();
            builder.Line();

            builder.Line("return map;");
        }

        public static string ReaderName(SubjectModel subject) => "BridgeRead_" + subject.MethodSuffix;

        private static string EnumReaderName(EnumModel model) => "BridgeReadEnum_" + model.MethodSuffix;

        private static void EmitSubjectReader(SourceBuilder builder, SubjectModel subject)
        {
            var members = subject.Members.Where(m => m.CanRead).ToList();
            var required = members.Where(m => m.IsRequired).ToList();
            var deferred = subject.NeedsDeferredConstruction;

            builder.Line(
                "internal static " + subject.Declaration + " " + ReaderName(subject) + "(" + Reader + ")"
                );
            builder.OpenBlock();

            //null на месте ссылочного типа - законное значение; на месте
            //структуры - отказ, и отказывает он тем же сообщением, что и
            //эталон
            if (!subject.IsValueType)
            {
                builder.OpenBlock("if (reader.TokenType == " + TokenType + ".Null)");
                builder.Line("return null;");
                builder.CloseBlock();
                builder.Line();
            }

            builder.Line(Read + ".ExpectStartObject(ref reader, typeof(" + subject.FullName + "));");
            builder.Line();

            if (deferred)
            {
                EmitDeferredLocals(builder, members);
            }
            else
            {
                builder.Line("var result = " + subject.NewExpression + ";");
                builder.Line();
            }

            if (required.Count > 0)
            {
                builder.Line("var " + RequiredSeen + " = 0UL;");
                builder.Line();
            }

            builder.OpenBlock("while (true)");

            builder.Line("reader.Read();");
            builder.OpenBlock("if (reader.TokenType == " + TokenType + ".EndObject)");
            builder.Line("break;");
            builder.CloseBlock();
            builder.Line();

            if (members.Count > 0)
            {
                //Диспетчер имён стои́т ПЕРВЫМ и никакой проверки перед собой не
                //имеет. Проверка там была и стоила 8-11% на чтении; убрать её
                //позволяет доказанное пробой (scratchpad/NameProbe) свойство:
                //ни экранированное, ни разрезанное имя не может совпасть ни с
                //одним печатаемым литералом.
                //
                //Экранированное - потому что его сырые байты содержат обратную
                //косую, а литерал не содержит её никогда: имя, требующее
                //экранирования, генератор отвергает (JGD027), а не-ASCII
                //экранирования не требует.
                //
                //Разрезанное - потому что при HasValueSequence читатель отдаёт
                //ПУСТОЙ ValueSpan (проверено на размерах куска от 1 до 12), а
                //корзины нулевой длины среди членов нет.
                //
                //Оба, промахнувшись, доходят до медленного пути ниже - то есть
                //цена проверки платится только там, где она нужна.
                var emptyName = members.Any(m => m.JsonNameUtf8.Length == 0);

                if (emptyName)
                {
                    //Единственное исключение: пустое JSON-имя. Оно законно
                    //({"":1} - валидный документ), и тогда корзина нулевой
                    //длины существует, а разрезанное имя в неё попадает. Здесь
                    //проверка обязана вернуться на горячий путь.
                    builder.OpenBlock("if (!reader.HasValueSequence)");
                }

                builder.Line("var name = reader.ValueSpan;");
                builder.Line();

                NameDispatcher.Emit(
                    builder,
                    members,
                    JsonFeature.None,
                    member => EmitMemberRead(builder, member, required, deferred)
                    );

                if (emptyName)
                {
                    builder.CloseBlock();
                    builder.Line();
                }

                //Медленный путь: имя приехало кусками или экранированным.
                //ValueTextEquals умеет и то и другое - он для этого и есть, -
                //но сравнивает по одному кандидату за раз, поэтому цепочка
                //печатается без всякого диспетчера.
                //
                //Условие спрашивается здесь, а не выше, и потому не стои́т
                //ничего на обычном документе: сюда управление доходит только
                //тогда, когда диспетчер уже промахнулся, то есть на
                //незнакомом, экранированном или разрезанном имени.
                builder.OpenBlock("if (!" + Read + ".NameIsPlain(ref reader))");

                foreach (var member in members)
                {
                    builder.OpenBlock(
                        "if (reader.ValueTextEquals(" + SourceBuilder.Utf8Literal(member.JsonName) + "))"
                        );
                    EmitMemberRead(builder, member, required, deferred);
                    builder.Line("goto next;");
                    builder.CloseBlock();
                    builder.Line();
                }

                builder.CloseBlock();
                builder.Line();
            }

            //Незнакомое свойство эталон с опциями по умолчанию пропускает
            //(проверено пробой), и мост обязан пропустить его так же. Skip
            //стоит на значении, а не на имени, поэтому сначала шаг.
            builder.Line("reader.Read();");
            builder.Line("reader.Skip();");
            builder.Line();

            builder.Unindent();
            builder.Line("next:");
            builder.Indent();
            builder.Line(";");

            builder.CloseBlock();
            builder.Line();

            EmitRequiredCheck(builder, subject, required);

            if (deferred)
            {
                builder.Line("var result = " + Construct(subject, members) + ";");

                foreach (var member in members.Where(m => !m.IsConstructorParameter && !m.IsRequired))
                {
                    builder.OpenBlock("if (" + Seen(member) + ")");
                    builder.Line("result." + member.MemberName + " = " + Assigned(member) + ";");
                    builder.CloseBlock();
                }
            }

            builder.Line("return result;");
            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitMemberRead(
            SourceBuilder builder,
            MemberModel member,
            IReadOnlyList<MemberModel> required,
            bool deferred
            )
        {
            //Отметка присутствия - ДО чтения значения, как и у маршрута A:
            //обязательность у эталона про имя, а не про значение.
            if (member.IsRequired)
            {
                builder.Line(RequiredSeen + " |= 0x" + RequiredBit(required, member).ToString("X") + "UL;");
            }

            builder.Line("reader.Read();");
            builder.Line(Target(member, deferred) + " = " + ValueExpression(member.Value) + ";");

            if (deferred && !member.IsConstructorParameter)
            {
                builder.Line(Seen(member) + " = true;");
            }
        }

        /// <summary>
        /// Выражение, читающее одно значение. Коллекции и субъекты уходят в
        /// отдельные методы, скаляры - в помощник: тело читателя обязано
        /// оставаться коротким, иначе JIT перестанет его встраивать (§12.6.1).
        /// </summary>
        private static string ValueExpression(ValueModel value)
        {
            switch (value.Form)
            {
                case ValueForm.Builtin:
                    return Read + "." + BuiltinMethod(value) + "(ref reader)";

                case ValueForm.Enum:
                    return EnumExpression(value);

                case ValueForm.Subject:
                    return "BridgeRead_" + value.MethodSuffix + "(ref reader)";

                default:
                    return "BridgeRead_" + value.MethodSuffix + "(ref reader)";
            }
        }

        private static string BuiltinMethod(ValueModel value)
        {
            var name = value.Builtin.ToString();

            //строка и массив байт умеют быть null сами по себе - отдельного
            //метода им не нужно
            var nullable = value.IsNullable
                && value.Builtin != BuiltinKind.String
                && value.Builtin != BuiltinKind.ByteArray;

            return nullable ? name + "OrNull" : name;
        }

        private static string EnumExpression(ValueModel value)
        {
            var model = value.Enum!;

            if (value.IsStringEnum)
            {
                return EnumReaderName(model) + "(ref reader)";
            }

            //числовой режим: эталон читает подлежащее число и приводит его к
            //типу enum'а, не проверяя, объявлен ли такой член
            var read = Read + "." + model.Underlying + "(ref reader)";

            return value.IsNullable
                ? "(reader.TokenType == " + TokenType + ".Null ? (" + value.TypeName + "?)null : ("
                    + value.TypeName + ")" + read + ")"
                : "(" + value.TypeName + ")" + read;
        }

        private static void EmitStringEnumReader(SourceBuilder builder, EnumModel model)
        {
            builder.Line("internal static " + model.FullName + " " + EnumReaderName(model) + "(" + Reader + ")");
            builder.OpenBlock();

            builder.OpenBlock("if (reader.TokenType != " + TokenType + ".String)");
            builder.Line("throw " + Read + ".Fail(typeof(" + model.FullName + "));");
            builder.CloseBlock();
            builder.Line();

            //Имя члена enum'а эталон сравнивает без учёта регистра, а
            //экранированное или разрезанное значение сравнивать сырыми байтами
            //нельзя - там та же развилка, что и у имён свойств.
            builder.OpenBlock("if (" + Read + ".NameIsPlain(ref reader))");
            builder.Line("var raw = reader.ValueSpan;");
            builder.Line();

            foreach (var member in model.Members)
            {
                builder.OpenBlock(
                    "if (global::JsonGoddess.Internal.JsonAsciiName.EqualsIgnoreCase(raw, "
                    + SourceBuilder.Utf8Literal(member.JsonName) + "))"
                    );
                builder.Line("return " + model.FullName + "." + member.MemberName + ";");
                builder.CloseBlock();
            }

            builder.CloseBlock();
            builder.OpenBlock("else");
            builder.Line("var text = reader.GetString();");
            builder.Line();

            foreach (var member in model.Members)
            {
                builder.OpenBlock(
                    "if (string.Equals(text, " + SourceBuilder.Literal(member.JsonName)
                    + ", global::System.StringComparison.OrdinalIgnoreCase))"
                    );
                builder.Line("return " + model.FullName + "." + member.MemberName + ";");
                builder.CloseBlock();
            }

            builder.CloseBlock();
            builder.Line();

            builder.Line("throw " + Read + ".Fail(typeof(" + model.FullName + "));");
            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitDeferredLocals(SourceBuilder builder, IReadOnlyList<MemberModel> members)
        {
            foreach (var member in members)
            {
                builder.Line(member.Value.Declaration + " " + Assigned(member) + " = default!;");

                if (!member.IsConstructorParameter)
                {
                    builder.Line("var " + Seen(member) + " = false;");
                }
            }

            builder.Line();
        }

        private static void EmitRequiredCheck(
            SourceBuilder builder,
            SubjectModel subject,
            IReadOnlyList<MemberModel> required
            )
        {
            if (required.Count == 0)
            {
                return;
            }

            builder.OpenBlock("if (" + RequiredSeen + " != 0x" + RequiredMask(required).ToString("X") + "UL)");
            builder.Line(
                "throw new global::System.Text.Json.JsonException("
                + "\"JSON deserialization for type '" + subject.FullName.Replace("global::", string.Empty)
                + "' was missing required properties including: \" + "
                + MissingName(subject) + "(" + RequiredSeen + ") + \".\");"
                );
            builder.CloseBlock();
            builder.Line();
        }

        private static string MissingName(SubjectModel subject) => "MissingRequired_" + subject.MethodSuffix;

        private static ulong RequiredBit(IReadOnlyList<MemberModel> required, MemberModel member)
        {
            for (var i = 0; i < required.Count; i++)
            {
                if (ReferenceEquals(required[i], member))
                {
                    return 1UL << i;
                }
            }

            return 0UL;
        }

        private static ulong RequiredMask(IReadOnlyList<MemberModel> required)
        {
            var mask = 0UL;
            for (var i = 0; i < required.Count; i++)
            {
                mask |= 1UL << i;
            }

            return mask;
        }

        private static string Assigned(MemberModel member) => "arg_" + member.MemberName;

        private static string Seen(MemberModel member) => "has_" + member.MemberName;

        private static string Target(MemberModel member, bool deferred)
        {
            return deferred ? Assigned(member) : "result." + member.MemberName;
        }

        private static string Construct(SubjectModel subject, IReadOnlyList<MemberModel> members)
        {
            var arguments = subject.Parameters
                .Select(p =>
                {
                    var member = members.FirstOrDefault(m =>
                        m.IsConstructorParameter
                        && string.Equals(m.MemberName, p.MemberName, System.StringComparison.Ordinal));

                    return member is null ? p.DefaultExpression : Assigned(member);
                });

            var initialized = subject.RequiredInitialized
                .Select(m => m.MemberName + " = " + Assigned(m))
                .ToList();

            var construction = "new " + subject.FullName + "(" + string.Join(", ", arguments) + ")";

            return initialized.Count == 0
                ? construction
                : construction + " { " + string.Join(", ", initialized) + " }";
        }
    }
}
