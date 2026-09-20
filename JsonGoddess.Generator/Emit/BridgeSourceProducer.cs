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
        /// Субъекты, которых мост берёт на себя. Сказать «нет» здесь ничего не
        /// стои́т: тип просто не попадёт в мост и продолжит работать через
        /// эталон - ровно так же, как при отказе dropin.
        ///
        /// <para>
        /// <b>Неподвижная точка, а не проверка по одному</b>, и это не
        /// аккуратность ради аккуратности. Неспособность <b>заразна</b>:
        /// читатель субъекта зовёт <c>BridgeRead_</c> на каждого члена формы
        /// «субъект», и если тот сам не напечатан, получается не медленный
        /// код, а несобирающийся - <c>CS0103</c> в сборке потребителя. Ровно
        /// та же неподвижная точка давно написана для потока
        /// (<see cref="TryReaderProducer.Servable"/>); мост её не получил, и
        /// пробой это стоило двух сломанных сборок.
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

                    //производные - такие же члены графа, как и свойства: тело
                    //производного печатается парным читателем, и необслуженное
                    //тело снимает обслуживание с базы целиком
                    if (subject.Members.Any(m => m.CanRead && !ValueIsServable(m.Value, servable))
                        || Dispatchable(subject).Any(d => !DerivedIsServable(host, d, servable)))
                    {
                        servable.Remove(subject.MethodSuffix);
                        changed = true;
                    }
                }
            }
            while (changed);

            return servable;
        }

        /// <summary>
        /// Что мост не берёт <b>само по себе</b>, без оглядки на членов.
        /// Остался субъект-коллекция: чтение у него устроено иначе, и делать
        /// его заодно значило бы отложить всё остальное.
        ///
        /// <para>
        /// Полиморфный субъект отсюда ушёл: его читатель печатается, и условие
        /// у него не «сам по себе», а «все производные обслужены» - оно живёт
        /// в неподвижной точке <see cref="Servable(HostModel)"/>.
        /// </para>
        /// </summary>
        private static bool ServableAlone(SubjectModel subject)
        {
            return subject.CollectionShape is null;
        }

        /// <summary>
        /// Производные, до которых диспетчер дискриминатора вообще способен
        /// добраться: <c>[JsonDerivedType(typeof(D))]</c> без значения эталон
        /// пишет без дискриминатора, и прочитать документ обратно производным
        /// не по чему.
        /// </summary>
        private static IEnumerable<DerivedTypeModel> Dispatchable(SubjectModel subject)
        {
            return subject.Derived.Where(d => d.DiscriminatorLiteral is not null);
        }

        private static bool DerivedIsServable(HostModel host, DerivedTypeModel derived, HashSet<string> servable)
        {
            var subject = host.Subjects.FirstOrDefault(s => s.FullName == derived.FullName);

            return subject is not null && servable.Contains(subject.MethodSuffix);
        }

        /// <summary>
        /// Можно ли отдать тип эталону <b>корнем</b> - то есть зарегистрировать
        /// его читатель и писатель в реестре моста.
        ///
        /// <para>
        /// Полиморфный - нельзя, и это <b>не наше ограничение, а его</b>.
        /// Снято пробой: <c>Deserialize&lt;Animal&gt;(json, мост)</c> на типе с
        /// <c>[JsonDerivedType]</c> бросает
        /// <c>NotSupportedException: The converter for derived type 'Animal'
        /// does not support metadata writes or reads</c> - и бросает ДО того,
        /// как управление дойдёт до нас. Чужой конвертер в свою полиморфную
        /// машинерию эталон не пускает вовсе.
        /// </para>
        ///
        /// <para>
        /// Это отказ, а не поломка, но зарегистрировать такой тип было бы
        /// хуже отказа: вместо тихого отступления к эталону потребитель
        /// получил бы исключение на ровном месте.
        /// </para>
        ///
        /// <para>
        /// Читатель при этом печатается и работает - но там, где дискриминатор
        /// разбирает <b>не эталон, а мы</b>: полиморфный член внутри
        /// обслуженного типа читается порождённым кодом как любой другой, и
        /// ровно в этом весь смысл снятого каскада.
        /// </para>
        /// </summary>
        public static bool CanRegisterRoot(SubjectModel subject)
        {
            return !subject.IsPolymorphic;
        }

        /// <summary>
        /// Почему тип не обслуживается - словами, для <c>JGD006</c>.
        ///
        /// <para>
        /// Причину называть обязательно: «не обслуживается» без неё
        /// превращается в «неизвестно почему», а это ровно то молчание, против
        /// которого весь §1 плана.
        /// </para>
        /// </summary>
        public static string WhyNotServed(HostModel host, SubjectModel subject, HashSet<string> servable)
        {
            if (!CanRegisterRoot(subject))
            {
                return "System.Text.Json refuses a third-party converter for a type that carries polymorphic "
                    + "metadata, and throws rather than falling back, so the bridge does not register it as a "
                    + "root. Generated code still reads it wherever it appears inside another served type";
            }

            if (subject.CollectionShape is not null)
            {
                return "it is a collection subject, and the bridge does not print those yet";
            }

            foreach (var derived in Dispatchable(subject))
            {
                if (DerivedIsServable(host, derived, servable))
                {
                    continue;
                }

                var nestedDerived = host.Subjects.FirstOrDefault(s => s.FullName == derived.FullName);

                return "its derived type '" + derived.FullName.Replace("global::", string.Empty)
                    + "' is not served"
                    + (nestedDerived is null
                        ? " (it is not registered as a subject of this host)"
                        : " (" + WhyNotServed(host, nestedDerived, servable) + ")");
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
                case ValueForm.Enumerable:
                case ValueForm.Dictionary:
                    return Offender(value.Element!);

                default:
                    return null;
            }
        }

        private static bool ValueIsServable(ValueModel value, HashSet<string> servable)
        {
            switch (value.Form)
            {
                case ValueForm.Builtin:
                case ValueForm.Enum:
                    return true;

                case ValueForm.Subject:
                    return servable.Contains(value.MethodSuffix);

                case ValueForm.List:
                case ValueForm.Array:
                case ValueForm.Enumerable:
                case ValueForm.Dictionary:
                    return value.Element is not null && ValueIsServable(value.Element, servable);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Читатели всех обслуживаемых субъектов. Печатаются в тот же
        /// частичный класс, где лежит порождённый писатель: регистрации нужны
        /// оба, и разносить их по типам незачем.
        /// </summary>
        public static void Emit(SourceBuilder builder, IReadOnlyList<SubjectModel> subjects, JsonFeature features)
        {
            foreach (var subject in subjects)
            {
                EmitSubjectReader(builder, subject, features, null, null);

                //Тело каждого производного - отдельным читателем. Обслужен он
                //наверняка: неподвижная точка не пустила бы сюда базу, у
                //которой производное не обслужено.
                foreach (var derived in Dispatchable(subject))
                {
                    EmitSubjectReader(
                        builder,
                        subjects.First(s => s.FullName == derived.FullName),
                        features,
                        PairReaderName(subject, derived),
                        subject
                        );
                }
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
                EmitCollectionReader(builder, collection, features);
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
        private static void EmitCollectionReader(SourceBuilder builder, ValueModel value, JsonFeature features)
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
                EmitDictionaryBody(builder, value, element, features);
            }
            else
            {
                EmitListBody(builder, value, element, features);
            }

            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitListBody(
            SourceBuilder builder,
            ValueModel value,
            ValueModel element,
            JsonFeature features
            )
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
            builder.Line("items.Add(" + ValueExpression(element, features) + ");");
            builder.CloseBlock();
            builder.Line();

            builder.Line(value.Form == ValueForm.Array ? "return items.ToArray();" : "return items;");
        }

        private static void EmitDictionaryBody(
            SourceBuilder builder,
            ValueModel value,
            ValueModel element,
            JsonFeature features
            )
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
            builder.Line("map[key] = " + ValueExpression(element, features) + ";");
            builder.CloseBlock();
            builder.Line();

            builder.Line("return map;");
        }

        public static string ReaderName(SubjectModel subject) => "BridgeRead_" + subject.MethodSuffix;

        private static string EnumReaderName(EnumModel model) => "BridgeReadEnum_" + model.MethodSuffix;

        /// <param name="bodyName">
        /// Не <c>null</c> - печатается <b>тело</b> производного: читатель,
        /// которого позвали, когда читатель эталона стои́т на значении
        /// дискриминатора. Скобку и <c>null</c> разобрал звавший.
        /// </param>
        /// <param name="declaredAs">
        /// Чем результат объявлен, если это не сам субъект: у тела
        /// производного он объявлен базой.
        /// </param>
        private static void EmitSubjectReader(
            SourceBuilder builder,
            SubjectModel subject,
            JsonFeature features,
            string? bodyName,
            SubjectModel? declaredAs
            )
        {
            var members = subject.Members.Where(m => m.CanRead).ToList();
            var required = members.Where(m => m.IsRequired).ToList();
            var deferred = subject.NeedsDeferredConstruction;
            var body = bodyName is not null;
            var discriminatorGuard = (declaredAs ?? subject).IsPolymorphic
                ? (declaredAs ?? subject).DiscriminatorName
                : null;

            builder.Line(
                "internal static " + (declaredAs ?? subject).Declaration + " "
                + (bodyName ?? ReaderName(subject)) + "(" + Reader + ")"
                );
            builder.OpenBlock();

            if (!body)
            {
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

                if (subject.IsPolymorphic)
                {
                    EmitDiscriminatorDispatch(builder, subject);
                }
            }

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

            //Дискриминатор, встреченный не первым свойством, - отказ. Эталон
            //здесь отказывает тоже, и принять такой документ значило бы
            //прочесть то, чего не читает он. Без этой проверки имя просто не
            //нашло бы члена и уехало бы в Skip, то есть документ был бы принят
            //молча. Платят за неё только полиморфные типы.
            if (discriminatorGuard is not null)
            {
                builder.OpenBlock(
                    "if (reader.ValueTextEquals(" + SourceBuilder.Utf8Literal(discriminatorGuard) + "))"
                    );
                builder.Line(
                    "throw new global::System.Text.Json.JsonException("
                    + "\"the type discriminator must be the first property\");"
                    );
                builder.CloseBlock();
                builder.Line();
            }

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
                    features,
                    member => EmitMemberRead(builder, member, required, deferred, features)
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

                var caseInsensitive = (features & JsonFeature.CaseInsensitiveNames) != 0;

                if (caseInsensitive)
                {
                    //ValueTextEquals сравнивает точно, а здесь регистр значить
                    //не должен. Строка на этом пути не стои́т ничего: он и так
                    //редкий, а имена в нём - экранированные или разрезанные.
                    //
                    //OrdinalIgnoreCase, а не культурная свёртка, - потому что
                    //так сворачивает эталон (проверено пробой: ЁЖИК находит
                    //член Ёжик). На ASCII-литералах, а других генератор под
                    //этой фичей не печатает, оба правила совпадают.
                    builder.Line("var text = reader.GetString();");
                    builder.Line();
                }

                foreach (var member in members)
                {
                    builder.OpenBlock(
                        caseInsensitive
                            ? "if (string.Equals(text, " + SourceBuilder.Literal(member.JsonName)
                                + ", global::System.StringComparison.OrdinalIgnoreCase))"
                            : "if (reader.ValueTextEquals(" + SourceBuilder.Utf8Literal(member.JsonName) + "))"
                        );
                    EmitMemberRead(builder, member, required, deferred, features);
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

        /// <summary>
        /// Диспетчер дискриминатора над читателем эталона.
        ///
        /// <para>
        /// Откат здесь устроен иначе, чем у двух наших читателей, и это не
        /// выдумка: <c>Utf8JsonReader</c> - структура, и его <b>копия</b> есть
        /// полноценное сохранённое состояние. Присвоили копию обратно -
        /// вернулись на открывающую скобку, и дальше объект читается базой как
        /// ни в чём не бывало.
        /// </para>
        ///
        /// <para>
        /// Значение сравнивается не сырыми байтами, как у буферного читателя,
        /// а <c>ValueTextEquals</c> и <c>TryGetInt32</c> - тем же, чем сравнил
        /// бы его сам эталон. Сырых байтов здесь и нет: документ нам не
        /// принадлежит, а кусок значения приезжает разрезанным.
        /// </para>
        /// </summary>
        private static void EmitDiscriminatorDispatch(SourceBuilder builder, SubjectModel subject)
        {
            builder.Line("var __beforeDiscriminator = reader;");
            builder.Line();
            builder.Line("reader.Read();");
            builder.Line();

            builder.OpenBlock(
                "if (reader.TokenType == " + TokenType + ".PropertyName && reader.ValueTextEquals("
                + SourceBuilder.Utf8Literal(subject.DiscriminatorName) + "))"
                );
            builder.Line("reader.Read();");
            builder.Line();

            var index = 0;

            foreach (var derived in Dispatchable(subject))
            {
                builder.OpenBlock("if (" + DiscriminatorTest(derived.DiscriminatorLiteral!, index) + ")");
                builder.Line("return " + PairReaderName(subject, derived) + "(ref reader);");
                builder.CloseBlock();
                builder.Line();
                index++;
            }

            builder.Line(
                "throw new global::System.Text.Json.JsonException("
                + "\"unrecognized type discriminator for '"
                + subject.FullName.Replace("global::", string.Empty) + "'\");"
                );
            builder.CloseBlock();
            builder.Line();

            builder.Line("reader = __beforeDiscriminator;");
            builder.Line();
        }

        /// <summary>
        /// Сравнение значения дискриминатора. Литерал приезжает готовым к
        /// печати в документ - <c>"dog"</c> с кавычками либо <c>7</c> без них,
        /// - и по кавычке видно, какого он рода. Третьего рода не бывает:
        /// связыватель принимает только строку и <c>int</c>.
        /// </summary>
        private static string DiscriminatorTest(string literal, int index)
        {
            if (literal.Length > 1 && literal[0] == '"')
            {
                var text = literal.Substring(1, literal.Length - 2);

                return "reader.TokenType == " + TokenType + ".String && reader.ValueTextEquals("
                    + SourceBuilder.Utf8Literal(text) + ")";
            }

            //имя переменной по номеру, а не по значению: у отрицательного
            //дискриминатора значение в идентификатор не годится
            var temp = "__discriminator" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return "reader.TokenType == " + TokenType + ".Number && reader.TryGetInt32(out var " + temp
                + ") && " + temp + " == " + literal;
        }

        private static string PairReaderName(SubjectModel subject, DerivedTypeModel derived) =>
            "BridgeReadBody_" + derived.MethodSuffix + "_As_" + subject.MethodSuffix;

        private static void EmitMemberRead(
            SourceBuilder builder,
            MemberModel member,
            IReadOnlyList<MemberModel> required,
            bool deferred,
            JsonFeature features
            )
        {
            //Отметка присутствия - ДО чтения значения, как и у маршрута A:
            //обязательность у эталона про имя, а не про значение.
            if (member.IsRequired)
            {
                builder.Line(RequiredSeen + " |= 0x" + RequiredBit(required, member).ToString("X") + "UL;");
            }

            builder.Line("reader.Read();");
            builder.Line(Target(member, deferred) + " = " + ValueExpression(member.Value, features) + ";");

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
        private static string ValueExpression(ValueModel value, JsonFeature features)
        {
            switch (value.Form)
            {
                case ValueForm.Builtin:
                    return Read + "." + BuiltinMethod(value, features) + "(ref reader)";

                case ValueForm.Enum:
                    return EnumExpression(value, features);

                case ValueForm.Subject:
                    return "BridgeRead_" + value.MethodSuffix + "(ref reader)";

                default:
                    return "BridgeRead_" + value.MethodSuffix + "(ref reader)";
            }
        }

        private static string BuiltinMethod(ValueModel value, JsonFeature features)
        {
            var name = value.Builtin.ToString();

            //строка и массив байт умеют быть null сами по себе - отдельного
            //метода им не нужно
            var nullable = value.IsNullable
                && value.Builtin != BuiltinKind.String
                && value.Builtin != BuiltinKind.ByteArray;

            if (nullable)
            {
                name += "OrNull";
            }

            //Число, которое разрешено прислать строкой, читается отдельным
            //методом, а не строгим с флагом: флаг стоил бы ветки на каждое
            //значение у всех ради поведения, которого у большинства нет.
            //Строк, дат, GUID'ов и логических это не касается - у эталона
            //AllowReadingFromString тоже только про числа (проверено пробой:
            //"true" на bool он отвергает).
            return (features & JsonFeature.NumbersFromStrings) != 0 && IsNumeric(value.Builtin)
                ? name + "Lenient"
                : name;
        }

        private static bool IsNumeric(BuiltinKind kind)
        {
            switch (kind)
            {
                case BuiltinKind.SByte:
                case BuiltinKind.Byte:
                case BuiltinKind.Int16:
                case BuiltinKind.UInt16:
                case BuiltinKind.Int32:
                case BuiltinKind.UInt32:
                case BuiltinKind.Int64:
                case BuiltinKind.UInt64:
                case BuiltinKind.Single:
                case BuiltinKind.Double:
                case BuiltinKind.Decimal:
                    return true;

                default:
                    return false;
            }
        }

        private static string EnumExpression(ValueModel value, JsonFeature features)
        {
            var model = value.Enum!;

            if (value.IsStringEnum)
            {
                return EnumReaderName(model) + "(ref reader)";
            }

            //числовой режим: эталон читает подлежащее число и приводит его к
            //типу enum'а, не проверяя, объявлен ли такой член
            var read = Read + "." + model.Underlying
                + ((features & JsonFeature.NumbersFromStrings) != 0 ? "Lenient" : string.Empty)
                + "(ref reader)";

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
