using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;
using JsonGoddess.Generator.Shared;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Печатает partial-часть хоста целиком.
    ///
    /// Преамбула здесь не формальность: сгенерированный код живёт в чужой
    /// сборке, и её настройки анализа - не наше дело. <c>&lt;auto-generated/&gt;</c>
    /// плюс общее подавление предупреждений означают, что подключение
    /// JsonGoddess не может испортить чужую сборку с
    /// <c>TreatWarningsAsErrors</c>.
    /// </summary>
    public static class ClassSourceProducer
    {
        private const string Scan = ValueSourceProducer.Scan;
        private const string Mem = ValueSourceProducer.Mem;
        private const string TokenKind = "global::JsonGoddess.Internal.JsonTokenKind";
        private const string AsciiName = "global::JsonGoddess.Internal.JsonAsciiName";
        private const string Context = "global::JsonGoddess.JsonParseContext";
        private const string Span = "global::System.ReadOnlySpan<byte>";
        private const string EqualityComparer = "global::System.Collections.Generic.EqualityComparer";
        private const string DocumentException = "global::JsonGoddess.JsonDocumentException";
        private const string PathBuilder = "global::JsonGoddess.Internal.JsonPath";
        private const string PathAnchor = "global::JsonGoddess.JsonPathAnchor";
        private const string RequiredNames = "global::JsonGoddess.Internal.JsonRequiredNames";

        /// <summary>
        /// Просьба к JIT'у вставить тело по месту. Ставится только на читатель
        /// скаляра, и стоит она 810 байт на графе из двухсот типов.
        ///
        /// Обоснование у неё узкое, и назвать его надо точно (§16.2 плана). В
        /// <b>рукописном</b> читателе WIDE вызов читателя скаляра не стоит
        /// ничего и без атрибута: три формы - скаляр по месту, скаляр вызовом,
        /// скаляр вызовом с атрибутом - в одном round-robin различаются на
        /// проценты при разбросе того же порядка, то есть JIT там встраивает
        /// сам. В <b>порождённом</b> читателе метод больше - гибридный
        /// диспетчер плюс путь разэкранирования, - и отставание от лучшей
        /// рукописной формы, померенное внутри каждого прогона, выходит 0.6%
        /// (REGULAR) и 2.4% (WIDE) с атрибутом против 2.2% и 6.4% без него.
        /// </summary>
        private const string Inline =
            "[global::System.Runtime.CompilerServices.MethodImpl("
            + "global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";

        /// <summary>
        /// <c>JsonFeature.Comments</c>: имя метода, которым печатается пропуск
        /// пробелов перед значением или структурным токеном - обычное или
        /// комментарий-осведомлённое. Выбор делается на этапе генерации, как
        /// и у <c>StringReadCall</c>/<c>NumberReadCall</c> в
        /// <see cref="ValueSourceProducer"/>: выключенная фича не должна
        /// стоить даже одной проверки, поэтому печатается только одна из двух
        /// форм, а не обе с условием между ними.
        /// </summary>
        private static string SkipTrivia(JsonFeature features)
        {
            return Scan + ((features & JsonFeature.Comments) != 0
                ? ".SkipWhitespaceAndComments"
                : ".SkipWhitespace");
        }

        /// <summary>
        /// Печатает явный пропуск пробелов и комментариев перед следующим
        /// значением/структурным токеном - но только когда
        /// <c>JsonFeature.Comments</c> включена; без неё не печатается вообще
        /// ничего, потому что метод, который читает следующий токен
        /// (<c>Expect</c>/<c>TryConsume</c>/<c>Peek</c>/<c>ReadStringContent</c>
        /// и так далее), и так делает обычный <c>SkipWhitespace</c> первым
        /// действием - лишний вызов был бы платой без всякой фичи.
        ///
        /// Печатается как самое первое действие каждого
        /// <c>Read_</c>/<c>ReadCollection_</c>/<c>ReadScalar_</c>/<c>ReadEnum_</c>/
        /// дискриминаторного блока и перед каждой структурной проверкой внутри
        /// цикла (имя следующего члена, запятая, конец коллекции) - везде,
        /// где документ, по которому прогонялся пробой, разрешает
        /// комментарий. Единственное исключение - между именем свойства и
        /// двоеточием (см. <see cref="JsonScan.SkipWhitespaceAndComments"/>):
        /// там эта функция не зовётся вовсе, и туда её звать нельзя.
        /// </summary>
        private static void EmitCommentSkip(SourceBuilder builder, JsonFeature features)
        {
            if ((features & JsonFeature.Comments) != 0)
            {
                builder.Line(Scan + ".SkipWhitespaceAndComments(json, ref position);");
            }
        }

        /// <summary>
        /// <c>JsonFeature.TrailingCommas</c>: печатается сразу после успешно
        /// потреблённой запятой, между членом/элементом и следующим - если
        /// дальше сразу стоит закрывающая скобка, это она и есть, и цикл
        /// обязан остановиться, не пытаясь прочесть ещё один член/элемент.
        /// <c>Peek</c> не потребляет токен, поэтому последующий
        /// <c>Expect(Close...)</c> после выхода из цикла видит скобку на том
        /// же месте.
        /// </summary>
        private static void EmitTrailingCommaCheck(SourceBuilder builder, JsonFeature features, string endTokenKind)
        {
            if ((features & JsonFeature.TrailingCommas) == 0)
            {
                return;
            }

            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (" + Scan + ".Peek(json, ref position) == " + endTokenKind + ")");
            builder.Line("break;");
            builder.CloseBlock();
        }

        public static string Produce(HostModel host)
        {
            var builder = new SourceBuilder();

            builder.Line("// <auto-generated/>");
            builder.Line("#pragma warning disable");
            builder.Line("#nullable enable");
            builder.Line();

            EmitAliases(builder);

            //namespace файловой области, а не блоком: она снимает один уровень
            //отступа со всего файла, а на графе из двухсот типов отступы - это
            //треть его объёма (§16.2 плана). C# 10 для неё достаточно, а у нас
            //и без того требуется одиннадцатый - из-за u8-литералов.
            if (!string.IsNullOrEmpty(host.Namespace))
            {
                builder.Line("namespace " + host.Namespace + ";");
                builder.Line();
            }

            builder.OpenBlock(host.TypeName);

            var features = host.Features;

            foreach (var exhauster in host.ExhausterTypes)
            {
                foreach (var subject in host.Subjects)
                {
                    if (subject.IsRoot)
                    {
                        EmitSerializeEntry(builder, subject, exhauster);
                    }

                    if (subject.IsPolymorphic)
                    {
                        EmitPolymorphicWriter(builder, host, subject, exhauster, host.DictionaryKeyNaming, features);
                    }
                    else
                    {
                        EmitWriter(
                            builder, subject, exhauster, host.DictionaryKeyNaming, features,
                            host.EscapesLikeReference, "Write_" + subject.MethodSuffix, null
                            );
                    }
                }

                foreach (var collection in host.Collections)
                {
                    EmitCollectionWriter(builder, collection, exhauster, host.DictionaryKeyNaming, features);
                }

                foreach (var enumModel in host.StringEnums)
                {
                    EmitEnumWriter(builder, enumModel, exhauster, host.EscapesLikeReference);
                }
            }

            foreach (var injector in host.InjectorTypes)
            {
                foreach (var subject in host.Subjects)
                {
                    if (subject.IsRoot)
                    {
                        EmitDeserializeEntry(builder, subject, injector, host.Guards, features);
                    }

                    EmitReader(
                        builder, subject, injector, null,
                        subject.IsPolymorphic ? subject.DiscriminatorName : null,
                        host.Guards, host.MaxDepth, features
                        );

                    foreach (var derived in subject.Derived)
                    {
                        EmitReader(
                            builder,
                            host.Subjects.First(s => s.FullName == derived.FullName),
                            injector,
                            PairReaderName(subject, derived),
                            subject.DiscriminatorName,
                            host.Guards, host.MaxDepth, features
                            );
                    }
                }

                foreach (var scalar in host.Scalars)
                {
                    EmitScalarReader(builder, scalar, injector, host.Guards, features);
                }

                foreach (var collection in host.Collections)
                {
                    EmitCollectionReader(builder, collection, injector, host.Guards, host.MaxDepth, features);
                }

                foreach (var enumModel in host.StringEnums)
                {
                    EmitEnumReader(builder, enumModel, injector, host.Guards, features);
                }
            }

            builder.CloseBlock();

            return builder.ToString();
        }

        /// <summary>
        /// Псевдонимы вместо полных имён.
        ///
        /// <c>global::JsonGoddess.Internal.JsonScan</c> встречается в
        /// порождённом коде чаще всего остального вместе взятого, и на графе
        /// из двухсот типов одно это имя занимало 164 килобайта из 1.9
        /// мегабайта (§16.2 плана). Псевдоним - то же самое связывание, но
        /// один раз.
        /// </summary>
        /// <remarks>
        /// Имена с двумя подчёркиваниями - не кокетство. Псевдоним уровня
        /// файла проигрывает типу, объявленному в том же namespace, что и
        /// хост: назови мы его <c>Scan</c>, чужой <c>Scan</c> рядом с хостом
        /// молча перехватил бы связывание. С <c>__</c> такого типа не бывает,
        /// а если он всё же найдётся, это будет ошибка компиляции, а не другое
        /// поведение.
        /// </remarks>
        private static void EmitAliases(SourceBuilder builder)
        {
            builder.Line("using " + Scan + " = " + ValueSourceProducer.ScanFullName + ";");
            builder.Line("using " + Mem + " = " + ValueSourceProducer.MemoryExtensionsFullName + ";");
            builder.Line();
        }

        private static void EmitSerializeEntry(SourceBuilder builder, SubjectModel subject, string exhauster)
        {
            builder.OpenBlock(
                "public static void Serialize(" + exhauster + " exhauster, " + subject.Declaration + " value)"
                );
            builder.Line("Write_" + subject.MethodSuffix + "(exhauster, value);");
            builder.CloseBlock();
            builder.Line();
        }

        private static void EmitDeserializeEntry(
            SourceBuilder builder, SubjectModel subject, string injector, JsonGuard guards, JsonFeature features
            )
        {
            builder.OpenBlock(
                "public static void Deserialize(" + injector + " injector, " + Span + " json, out "
                + subject.Declaration + " result)"
                );
            builder.Line("var position = 0;");
            builder.Line("var context = new " + Context + "(json);");

            //try/finally здесь стоит один раз на документ, а не на член:
            //контекст арендует буфер только если в документе попалось
            //экранированное имя, но вернуть арендованное надо в любом случае
            builder.OpenBlock("try");
            builder.Line("result = Read_" + subject.MethodSuffix + "(injector, json, ref position, ref context);");

            //JsonGuard.TrailingContent: пробой подтверждено (System.Text.Json
            //9.0.0) - у эталона это встроенное и безусловное поведение
            //string/ReadOnlySpan<byte>-перегрузок, способа его выключить у
            //самого эталона нет. У нас по умолчанию хвост документа не
            //проверяется вовсе, и это остаётся так, пока хост не попросил
            //иначе, - ветка ниже печатается только под флагом.
            //
            //JsonFeature.Comments: хвостовой комментарий после единственного
            //значения документа эталон тоже принимает
            //(ReadCommentHandling.Skip, пробоем подтверждено) - поэтому здесь
            //печатается комментарий-осведомлённый пропуск пробелов вместо
            //обычного, когда фича включена.
            if ((guards & JsonGuard.TrailingContent) != 0)
            {
                builder.Line(SkipTrivia(features) + "(json, ref position);");
                builder.OpenBlock("if (position != json.Length)");
                builder.Line(
                    "throw new " + DocumentException
                    + "(\"Unexpected trailing content after the top-level value.\", position);"
                    );
                builder.CloseBlock();
            }

            builder.CloseBlock();

            //§6.4: путь в сообщении достаётся хосту с любым стражем, и только
            //ему. Стоит он ровно этих двух ветвей: порождённые читатели о
            //путях не знают вовсе, а собирается путь холодным проходом по
            //документу - см. JsonPath, там же о том, почему не раскруткой.
            //
            //Смещение отказа берётся из двух разных мест, и это не небрежность.
            //Сканер знает, на каком байте споткнулся, и кладёт его в само
            //исключение. Инжектор не знает ничего - лексема к тому моменту
            //прочитана, - зато position точки входа уже стоит за её концом:
            //все читатели правят по ref именно эту переменную.
            if (guards != JsonGuard.None)
            {
                builder.OpenBlock("catch (" + DocumentException + " __failure)");
                builder.Line("throw " + PathBuilder + ".Decorate(__failure, json);");
                builder.CloseBlock();

                builder.OpenBlock("catch (global::System.FormatException __failure)");
                builder.Line("throw " + PathBuilder + ".Decorate(__failure, json, position);");
                builder.CloseBlock();
            }

            builder.OpenBlock("finally");
            builder.Line("context.Release();");
            builder.CloseBlock();

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Запись. Разделители склеены с именами свойств в один
        /// UTF-8-литерал: структура документа известна на этапе компиляции, и
        /// автомата состояний, стека глубины и проверки "нужна ли запятая"
        /// здесь нет вовсе - в отличие от <c>Utf8JsonWriter</c>, который не
        /// знает, что ему напишут следующим.
        ///
        /// Ни одной ветки на разделители в фазе 2 не бывает и по второй
        /// причине: условно опускаемых членов ещё нет, поэтому запятые
        /// безусловны.
        /// </summary>
        /// <summary>
        /// Писатель полиморфной базы: развилка по <b>точному</b> типу значения.
        ///
        /// Не <c>is</c>, и это не стилистика. Незарегистрированный потомок
        /// зарегистрированного потомка (<c>Poodle : Dog</c>) у эталона -
        /// <c>NotSupportedException</c>, а <c>is Dog</c> записал бы его как
        /// <c>Dog</c>, потеряв половину членов и не сказав об этом.
        ///
        /// Дальше по методу на пару «база + производный»: значение
        /// дискриминатора принадлежит паре, а тело у пары своё, потому что
        /// дискриминатор склеивается со скобкой и именем первого члена в один
        /// литерал - ровно как всё остальное строение документа.
        /// </summary>
        private static void EmitPolymorphicWriter(
            SourceBuilder builder,
            HostModel host,
            SubjectModel subject,
            string exhauster,
            JsonNamingStyle keyNaming,
            JsonFeature features
            )
        {
            var selfIsDerived = subject.Derived.Any(d => d.FullName == subject.FullName);

            builder.OpenBlock(
                "private static void Write_" + subject.MethodSuffix + "("
                + exhauster + " exhauster, " + subject.Declaration + " value)"
                );

            builder.OpenBlock("if (value is null)");
            builder.Line("exhauster.AppendNull();");
            builder.Line("return;");
            builder.CloseBlock();
            builder.Line();

            builder.Line("var runtimeType = value.GetType();");
            builder.Line();

            foreach (var derived in subject.Derived)
            {
                builder.OpenBlock("if (runtimeType == typeof(" + derived.FullName + "))");
                builder.Line(
                    PairWriterName(subject, derived) + "(exhauster, (" + derived.FullName + ")value);"
                    );
                builder.Line("return;");
                builder.CloseBlock();
                builder.Line();
            }

            if (selfIsDerived)
            {
                //сам тип уже разобран веткой выше, значит сюда доезжает только
                //то, чего в списке нет
                builder.Line("throw " + Unsupported(subject) + ";");
            }
            else
            {
                builder.OpenBlock("if (runtimeType != typeof(" + subject.FullName + "))");
                builder.Line("throw " + Unsupported(subject) + ";");
                builder.CloseBlock();
                builder.Line();
                builder.Line("WriteSelf_" + subject.MethodSuffix + "(exhauster, value);");
            }

            builder.CloseBlock();
            builder.Line();

            if (!selfIsDerived)
            {
                //база без собственного дискриминатора пишется как обычный
                //объект - проверено прогоном: у эталона он появляется только
                //тогда, когда база объявлена производной от самой себя
                EmitWriter(
                    builder, subject, exhauster, keyNaming, features, host.EscapesLikeReference,
                    "WriteSelf_" + subject.MethodSuffix, null
                    );
            }

            foreach (var derived in subject.Derived)
            {
                EmitWriter(
                    builder,
                    host.Subjects.First(s => s.FullName == derived.FullName),
                    exhauster,
                    keyNaming,
                    features,
                    host.EscapesLikeReference,
                    PairWriterName(subject, derived),
                    derived.DiscriminatorLiteral is null
                        ? null
                        : "\"" + Name(subject.DiscriminatorName, host.EscapesLikeReference) + "\":"
                            + Value(derived.DiscriminatorLiteral, host.EscapesLikeReference)
                    );
            }
        }

        /// <summary>
        /// Имя, печатаемое в документ константой. Compat-слой экранирует его
        /// по-эталонному; обычный хост - нет, и тогда это тождество.
        /// </summary>
        private static string Name(string name, bool escapeNames) =>
            escapeNames ? ReferenceEscaping.Body(name) : name;

        /// <summary>
        /// Готовый JSON-литерал (значение дискриминатора): у строки
        /// экранируется тело, число остаётся числом.
        /// </summary>
        private static string Value(string literal, bool escapeNames) =>
            escapeNames ? ReferenceEscaping.Literal(literal) : literal;

        private static string PairWriterName(SubjectModel subject, DerivedTypeModel derived) =>
            "Write_" + derived.MethodSuffix + "_As_" + subject.MethodSuffix;

        private static string PairReaderName(SubjectModel subject, DerivedTypeModel derived) =>
            "ReadBody_" + derived.MethodSuffix + "_As_" + subject.MethodSuffix;

        private static string Unsupported(SubjectModel subject)
        {
            return "new global::System.NotSupportedException(\"runtime type \" + runtimeType + "
                + "\" is not supported by polymorphic type '" + subject.FullName.Replace("global::", string.Empty)
                + "'\")";
        }

        /// <param name="escapeNames">
        /// Имена свойств печатать экранированными по-эталонному. Включено
        /// только у Compat-слоя (<see cref="HostModel.EscapesLikeReference"/>);
        /// у обычного хоста имя приезжает сюда как есть, и текст порождаемого
        /// кода от появления этого параметра не изменился ни на байт.
        /// </param>
        private static void EmitWriter(
            SourceBuilder builder,
            SubjectModel subject,
            string exhauster,
            JsonNamingStyle keyNaming,
            JsonFeature features,
            bool escapeNames,
            string methodName,
            string? discriminator
            )
        {
            var members = subject.Members.Where(m => m.CanWrite).ToList();

            builder.OpenBlock(
                "private static void " + methodName + "("
                + exhauster + " exhauster, " + subject.Declaration + " value)"
                );

            //у структуры проверки нет, и это не экономия ветки: значение
            //структуры null описать не может, а Nullable<> над ней разбирается
            //на месте члена, до вызова
            if (!subject.IsValueType)
            {
                builder.OpenBlock("if (value is null)");
                builder.Line("exhauster.AppendNull();");
                builder.Line("return;");
                builder.CloseBlock();
                builder.Line();
            }

            //Субъект, который сам является коллекцией (§9.10 плана): у него
            //нет обычных членов вовсе (members здесь и так пуст), а тело -
            //цикл по элементам. Проверка стоит раньше "members.Count == 0",
            //иначе такой субъект уехал бы как "{}" - ветка ниже про пустой
            //объект, а не про пустую коллекцию.
            if (subject.CollectionShape is not null)
            {
                EmitCollectionSubjectWriterBody(builder, subject, keyNaming, features);
                builder.CloseBlock();
                builder.Line();
                return;
            }

            if (members.Count == 0)
            {
                builder.Line(
                    "exhauster.AppendRaw("
                    + SourceBuilder.Utf8Literal(discriminator is null ? "{}" : "{" + discriminator + "}")
                    + ");"
                    );
                builder.CloseBlock();
                builder.Line();
                return;
            }

            //Состояние на этапе компиляции, а не в рантайме. pendingOpen -
            //фигурная скобка ещё не напечатана и склеится с именем первого
            //члена; commaIsCertain - до этого места точно что-то написано, и
            //запятая снова становится частью литерала.
            var pendingOpen = true;
            var commaIsCertain = false;
            var needCommaDeclared = false;

            //дискриминатор печатается первым свойством - так его пишет эталон,
            //и так же он его требует на чтении. Для автомата состояний это
            //означает, что скобка уже напечатана и запятая перед следующим
            //членом заведомо нужна
            if (discriminator is not null)
            {
                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal("{" + discriminator) + ");");
                pendingOpen = false;
                commaIsCertain = true;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (member.Condition == WriteCondition.Always)
                {
                    //скобка склеивается с именем первого члена только здесь: у
                    //условного члена она обязана быть напечатана снаружи его
                    //ветки, иначе объект без членов остался бы без скобки
                    var literal = (pendingOpen ? "{" : commaIsCertain ? "," : string.Empty)
                        + "\"" + Name(member.JsonName, escapeNames) + "\":";

                    if (!pendingOpen && !commaIsCertain && needCommaDeclared)
                    {
                        builder.OpenBlock("if (needComma)");
                        builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(",") + ");");
                        builder.CloseBlock();
                        builder.Line();
                    }

                    builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(literal) + ");");
                    ValueSourceProducer.WriteValue(builder, member.Value, "value." + member.MemberName, keyNaming, features);

                    pendingOpen = false;
                    commaIsCertain = true;
                    continue;
                }

                if (pendingOpen)
                {
                    builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal("{") + ");");
                    pendingOpen = false;
                }

                if (!commaIsCertain && !needCommaDeclared)
                {
                    builder.Line("var needComma = false;");
                    needCommaDeclared = true;
                }

                builder.Line();

                var candidate = "candidate" + i;
                var conditionalLiteral = (commaIsCertain ? "," : string.Empty)
                    + "\"" + Name(member.JsonName, escapeNames) + "\":";

                builder.OpenBlock();
                builder.Line("var " + candidate + " = value." + member.MemberName + ";");
                builder.OpenBlock("if (" + Condition(member, candidate) + ")");

                if (!commaIsCertain)
                {
                    //у самого первого члена needComma заведомо false, и ветка
                    //здесь была бы веткой ради симметрии
                    if (i > 0)
                    {
                        builder.OpenBlock("if (needComma)");
                        builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(",") + ");");
                        builder.CloseBlock();
                        builder.Line();
                    }

                    builder.Line("needComma = true;");
                }

                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(conditionalLiteral) + ");");
                ValueSourceProducer.WriteValue(builder, member.Value, candidate, keyNaming, features);

                builder.CloseBlock();
                builder.CloseBlock();
                builder.Line();
            }

            if (pendingOpen)
            {
                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal("{") + ");");
            }

            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal("}") + ");");
            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Условие записи. Для <c>default</c> сравнение печатается прямое, а не
        /// через <c>EqualityComparer&lt;T&gt;.Default</c>: тип известен, и на
        /// всех обслуживаемых типах <c>!=</c> даёт тот же ответ - включая
        /// <c>-0.0</c>, который эталон тоже опускает.
        /// </summary>
        private static string Condition(MemberModel member, string candidate)
        {
            if (member.Condition == WriteCondition.WhenNotNull || member.Value.IsNullable)
            {
                return candidate + " is not null";
            }

            //Структура-субъект: у неё нет operator !=, и печатать сравнение
            //значило бы выдать код, который не компилируется. Эталон в этом
            //месте сравнивает через EqualityComparer<T>.Default - проверено
            //прогоном: структура в значении по умолчанию из документа
            //исчезает, - и цена его вызова тут уместна, потому что платит за
            //неё тот, кто явно написал WhenWritingDefault на структуре.
            if (member.Value.IsValueType)
            {
                return "!" + EqualityComparer + "<" + member.Value.TypeName + ">.Default.Equals("
                    + candidate + ", default(" + member.Value.TypeName + "))";
            }

            return candidate + " != default(" + member.Value.TypeName + ")";
        }

        /// <summary>
        /// Развилка по дискриминатору - <b>только первым свойством</b>.
        ///
        /// Это не наше упрощение, а требование эталона: документ, в котором
        /// <c>$type</c> стои́т не первым, он читать отказывается
        /// (<c>JsonException</c>), и принять такой документ значило бы прочесть
        /// то, чего он не читает.
        ///
        /// Значение дискриминатора сравнивается <b>сырым текстом</b>, вместе с
        /// кавычками, если они есть. Одна ветка на строку и на число вместо
        /// двух: <c>"dog"</c> и <c>7</c> различаются как байты и без разбора
        /// лексемы.
        /// </summary>
        private static void EmitDiscriminatorDispatch(
            SourceBuilder builder, SubjectModel subject, JsonGuard guards, JsonFeature features
            )
        {
            builder.Line("var discriminatorStart = position;");
            builder.Line("var hasDiscriminator = false;");
            builder.Line();

            //первое обращение к сканеру после '{' в этом блоке - комментарий
            //между скобкой и именем дискриминатора здесь ещё не пропущен
            //никем
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (" + Scan + ".Peek(json, ref position) == " + TokenKind + ".String)");
            builder.Line("var firstName = " + ValueSourceProducer.StringReadCall(guards) + "(json, ref position, out var firstEscaped);");
            builder.OpenBlock("if (firstEscaped)");
            builder.Line(
                "firstName = context.UnescapeName(firstName"
                + ((guards & JsonGuard.InvalidUtf8) != 0 ? ", true" : string.Empty) + ");"
                );
            builder.CloseBlock();
            builder.Line();
            builder.Line(
                "hasDiscriminator = " + Mem + ".SequenceEqual(firstName, "
                + SourceBuilder.Utf8Literal(subject.DiscriminatorName) + ");"
                );
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("if (hasDiscriminator)");
            builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".Colon);");
            builder.Line(SkipTrivia(features) + "(json, ref position);");
            builder.Line("var valueStart = position;");
            builder.Line(Scan + ".SkipValue(json, ref position);");
            builder.Line("var discriminator = json.Slice(valueStart, position - valueStart);");
            builder.Line();

            foreach (var derived in subject.Derived)
            {
                if (derived.DiscriminatorLiteral is null)
                {
                    //[JsonDerivedType(typeof(D))] без значения: эталон такой тип
                    //пишет без дискриминатора, значит и прочитать его обратно
                    //производным не по чему - у него просто нет имени
                    continue;
                }

                builder.OpenBlock(
                    "if (" + Mem + ".SequenceEqual(discriminator, "
                    + SourceBuilder.Utf8Literal(derived.DiscriminatorLiteral) + "))"
                    );
                builder.Line(
                    "return " + PairReaderName(subject, derived)
                    + "(injector, json, ref position, ref context);"
                    );
                builder.CloseBlock();
                builder.Line();
            }

            builder.Line(
                "throw new " + DocumentException + "(\"unrecognized type discriminator for '"
                + subject.FullName.Replace("global::", string.Empty) + "'\", valueStart);"
                );
            builder.CloseBlock();
            builder.Line();

            builder.Line("position = discriminatorStart;");
            builder.Line();
        }

        /// <summary>
        /// Читатель субъекта.
        /// </summary>
        /// <param name="bodyName">
        /// Не <c>null</c> - печатается <b>тело</b>: читатель, которого позвали
        /// уже внутри объекта, сразу после значения дискриминатора. Скобку и
        /// <c>null</c> разобрал звавший, и первое, что здесь бывает, - запятая
        /// или закрывающая скобка.
        ///
        /// Своя копия тела, а не разделение с обычным читателем: разделить их
        /// значило бы добавить вызов на каждое чтение каждого объекта ради
        /// экономии текста у полиморфных типов.
        /// </param>
        private static void EmitReader(
            SourceBuilder builder,
            SubjectModel subject,
            string injector,
            string? bodyName,
            string? discriminatorGuard,
            JsonGuard guards,
            int maxDepth,
            JsonFeature features
            )
        {
            var members = subject.Members.Where(m => m.CanRead).ToList();
            var body = bodyName is not null;

            builder.Line(
                "private static " + subject.Declaration + " "
                + (bodyName ?? "Read_" + subject.MethodSuffix) + "("
                );
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            //У структуры ветки на null нет, и это тоже не экономия: null на
            //месте структуры обязан кончиться отказом - ровно так ведёт себя
            //эталон, - и он кончается им сам, на Expect(OpenBrace) ниже.
            //Nullable<> над структурой разбирается на месте члена, до вызова.
            //Самое первое обращение к сканеру во всём методе - комментарий
            //перед значением целиком ("// hi\n{...}") ещё никем не пропущен.
            //Для body-читателя (полиморфная пара) это место другое - см.
            //ниже, перед проверкой запятой после дискриминатора.
            if (!body)
            {
                EmitCommentSkip(builder, features);
            }

            if (!body && !subject.IsValueType)
            {
                builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
                builder.Line("return null;");
                builder.CloseBlock();
                builder.Line();
            }

            //Субъект-коллекция (§9.10) не полиморфен (связыватель отказывает
            //на этой комбинации), значит body здесь всегда null, и метод
            //целиком печатается этой веткой - без диспетчера имён, без
            //отложенной сборки: строить нечем, кроме конструктора без
            //параметров, а класть - только в саму коллекцию.
            if (subject.CollectionShape is not null)
            {
                EmitCollectionSubjectReaderBody(builder, subject, guards, maxDepth, features);
                builder.CloseBlock();
                builder.Line();
                return;
            }

            //JsonGuard.MaxDepth: счётчик - общий на весь документ
            //(context.Depth), поэтому увеличивается только на "своём" входе в
            //объект, то есть не у body-читателя полиморфной пары - тот вызван
            //уже внутри объекта, который увеличил счётчик сам. try/finally
            //оборачивает всё, что дальше в методе, ровно потому, что выходов
            //из него несколько (пустой объект, конец цикла), а декремент
            //обязан отработать на любом из них - и на исключении тоже.
            var guardsDepth = (guards & JsonGuard.MaxDepth) != 0;

            if (!body)
            {
                builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".OpenBrace);");
                EmitDepthCheckAndOpenTry(builder, guardsDepth, maxDepth);
            }

            var deferred = subject.NeedsDeferredConstruction;

            if (deferred)
            {
                EmitDeferredLocals(builder, subject, members);
            }
            else
            {
                builder.Line("var result = " + subject.NewExpression + ";");
                builder.Line();
            }

            //JsonGuard.DuplicateProperties: флаг "видели" заводится на каждый
            //читаемый член отдельно от has_X отложенной формы (§9.8) -
            //последний означает совсем другое (member вообще был присвоен) и
            //не заводится для членов-параметров конструктора вовсе, а страж
            //обязан ловить повтор и на них тоже.
            if ((guards & JsonGuard.DuplicateProperties) != 0)
            {
                foreach (var member in members)
                {
                    builder.Line("var " + DupSeen(member) + " = false;");
                }

                if (members.Count > 0)
                {
                    builder.Line();
                }
            }

            //required/[JsonRequired]: одна битовая маска на всех обязательных
            //членов, а не флаг на каждого. Считать нельзя - эталон разрешает
            //повтор имени (проверено пробой), и счётчик от повтора одного
            //члена закрыл бы отсутствие другого.
            var required = members.Where(m => m.IsRequired).ToList();

            if (required.Count > 0)
            {
                builder.Line("var " + RequiredSeen + " = 0UL;");
                builder.Line();
            }

            if (subject.IsPolymorphic && !body)
            {
                EmitDiscriminatorDispatch(builder, subject, guards, features);
            }

            //Фреш-точка: у не-body читателя дискриминатор (если был) откатил
            //позицию обратно к discriminatorStart, а если полиморфизма нет
            //вовсе, сюда ещё ничего не заходило после Expect(OpenBrace) - в
            //обоих случаях комментарий сразу после '{' ещё не пропущен. У
            //body-читателя это вообще первое обращение к сканеру в методе -
            //комментарий между значением дискриминатора и запятой.
            EmitCommentSkip(builder, features);
            builder.OpenBlock(
                body
                    ? "if (!" + Scan + ".TryConsume(json, ref position, " + Scan + ".Comma))"
                    : "if (" + Scan + ".TryConsume(json, ref position, " + Scan + ".CloseBrace))"
                );

            if (body)
            {
                builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".CloseBrace);");
            }

            //Выходов из читателя два - пустой объект здесь и конец цикла ниже,
            //- и проверка обязана стоять на обоих. На этом ни один
            //обязательный член не мог быть виден ни разу, но печатается та же
            //проверка, а не безусловный throw: так у обоих выходов одна форма,
            //и сообщение собирается одним и тем же кодом.
            EmitRequiredCheck(builder, subject, required);
            builder.Line(deferred ? "return " + Construct(subject, members) + ";" : "return result;");
            builder.CloseBlock();
            builder.Line();

            builder.OpenBlock("while (true)");

            //комментарий перед именем следующего члена - после запятой,
            //пропущенной TryConsume ниже в этом же цикле, ничего ещё не
            //скользило по пробелам заново
            EmitCommentSkip(builder, features);
            builder.Line(
                "var name = " + ValueSourceProducer.StringReadCall(guards)
                + "(json, ref position, out var nameEscaped);"
                );

            //Между именем свойства и двоеточием комментарий здесь НЕ
            //пропускается никогда, даже при включённой JsonFeature.Comments:
            //пробой (System.Text.Json 10.0, ReadCommentHandling.Skip) на
            //{"Id" /* c */ : 1} показал отказ эталона именно в этой точке -
            //единственной, где комментарий вокруг структуры объекта незаконен.
            builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".Colon);");
            builder.Line();

            if (members.Count > 0)
            {
                builder.Unindent();
                builder.Line("dispatch:");
                builder.Indent();

                //Дискриминатор, встреченный не первым свойством, - отказ.
                //Эталон здесь отказывает тоже, и принять такой документ
                //значило бы прочесть то, чего не читает он. Проверка стои́т
                //после ярлыка, чтобы сработать и на разэкранированном имени.
                if (discriminatorGuard is not null)
                {
                    builder.OpenBlock(
                        "if (" + Mem + ".SequenceEqual(name, "
                        + SourceBuilder.Utf8Literal(discriminatorGuard) + "))"
                        );
                    builder.Line(
                        "throw new " + DocumentException
                        + "(\"the type discriminator must be the first property\", position);"
                        );
                    builder.CloseBlock();
                    builder.Line();
                }

                NameDispatcher.Emit(
                    builder,
                    members,
                    features,
                    member =>
                    {
                        //JsonGuard.DuplicateProperties: пробой подтверждено -
                        //эталон отказывает только на повторе СОПОСТАВЛЕННОГО
                        //члена (AllowDuplicateProperties=false), а повтор
                        //незнакомого имени пропускает не глядя; поэтому
                        //проверка стоит здесь, а не на пути неизвестного
                        //свойства.
                        if ((guards & JsonGuard.DuplicateProperties) != 0)
                        {
                            builder.OpenBlock("if (" + DupSeen(member) + ")");
                            builder.Line(
                                "throw new " + DocumentException + "(\"Duplicate property '"
                                + member.JsonName + "'.\", position);"
                                );
                            builder.CloseBlock();
                            builder.Line(DupSeen(member) + " = true;");
                        }

                        //Отметка присутствия стои́т ДО чтения значения, и это
                        //не вкусовщина: у эталона обязательность - про имя, а
                        //не про значение, поэтому {"Amount":null} на
                        //required string проходит, а на required int падает
                        //разбором значения, а не отсутствием (проверено
                        //пробой). Порядок строк это и воспроизводит.
                        if (member.IsRequired)
                        {
                            builder.Line(RequiredSeen + " |= 0x" + RequiredBit(required, member).ToString("X") + "UL;");
                        }

                        ValueSourceProducer.ReadValue(builder, member.Value, Target(member, deferred));

                        if (deferred && !member.IsConstructorParameter)
                        {
                            builder.Line(Seen(member) + " = true;");
                        }
                    }
                    );

                //Экранированное имя не совпадёт ни с одним литералом, потому
                //что литералы печатаются из имён, которым escape не нужен.
                //Значит управление доходит сюда само, и разэкранирование
                //оказывается ровно там, где оно и должно быть: на пути, по
                //которому обычный документ не идёт ни разу. Член, который
                //нашёлся, ушёл по goto раньше.
                //
                //Второй заход в тот же switch, а не второй switch: копия
                //диспетчера удвоила бы объём порождаемого кода ради случая,
                //который почти не встречается.
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

            //Свойство не нашло члена. JsonGuard.UnknownProperties - отказ
            //(аналог JsonUnmappedMemberHandling.Disallow); иначе, если хост
            //включил MaxDepth, поддерево пропускается с тем же счётчиком
            //глубины, что и известные типы (пробой подтверждено: у эталона
            //предел общий на оба случая) - а без обоих флагов ничего не
            //меняется вовсе.
            if ((guards & JsonGuard.UnknownProperties) != 0)
            {
                builder.Line(
                    "throw new " + DocumentException
                    + "(\"Unknown property '\" + global::System.Text.Encoding.UTF8.GetString(name.ToArray())"
                    + " + \"'.\", position);"
                    );
            }
            else if (guardsDepth)
            {
                builder.Line(Scan + ".SkipValueGuarded(json, ref position, ref context.Depth, " + maxDepth + ");");
            }
            else
            {
                builder.Line(Scan + ".SkipValue(json, ref position);");
            }

            builder.Line();

            builder.Unindent();
            builder.Line("next:");
            builder.Indent();
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (!" + Scan + ".TryConsume(json, ref position, " + Scan + ".Comma))");
            builder.Line("break;");
            builder.CloseBlock();

            //JsonFeature.TrailingCommas: ровно одна запятая перед закрывающей
            //скобкой - пробоем подтверждено, что System.Text.Json принимает
            //только эту форму (не [,], не [1,,2], не [,1]), поэтому проверка
            //стоит здесь, сразу после успешного потребления запятой, а не
            //где-то ещё
            EmitTrailingCommaCheck(builder, features, TokenKind + ".EndObject");

            builder.CloseBlock();
            builder.Line();

            builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".CloseBrace);");

            //Проверка стои́т ПОСЛЕ закрывающей скобки, и это поведение эталона,
            //а не наше удобство: позиция в его исключении указывает на конец
            //объекта, то есть отказ случается, когда объект дочитан, а не в
            //тот момент, когда стало ясно, что имени не будет.
            builder.Line();
            EmitRequiredCheck(builder, subject, required);

            if (deferred)
            {
                builder.Line();
                builder.Line("var result = " + Construct(subject, members) + ";");

                //обязательные уже присвоены инициализатором внутри Construct -
                //повторное присваивание было бы лишним, а для init-члена ещё
                //и незаконным
                foreach (var member in members.Where(m => !m.IsConstructorParameter && !m.IsRequired))
                {
                    builder.OpenBlock("if (" + Seen(member) + ")");
                    builder.Line("result." + member.MemberName + " = " + Assigned(member) + ";");
                    builder.CloseBlock();
                }
            }

            builder.Line("return result;");

            //Закрывает try, открытый вместе со счётчиком глубины сразу после
            //Expect(OpenBrace) выше: любой из выходов метода (пустой объект,
            //конец цикла, исключение стража) обязан вернуть глубину, и
            //try/finally - единственный способ не перечислять каждый выход
            //по отдельности. У body-читателя (полиморфная пара) try не
            //открывался - счётчик увеличил вызвавший, а не он сам.
            EmitDepthCheckCloseTry(builder, !body && guardsDepth);

            builder.CloseBlock();
            builder.Line();

            //Помощник печатается один раз на субъект, а не на читатель: у
            //полиморфной пары читателей два, а имя у метода одно.
            if (!body)
            {
                EmitMissingRequired(builder, subject, members);
            }
        }

        //internal, а не private, у помощников ниже: их печатает и потоковый
        //читатель (TryReaderProducer). Скопировать их туда значило бы завести
        //вторую правду о том, как называется локальная и какой бит у какого
        //обязательного члена, - а расходиться этим двоим нельзя: они читают
        //один и тот же документ в один и тот же тип.
        internal static string DupSeen(MemberModel member) => "dup_" + member.MemberName;

        /// <summary>
        /// Маска присутствия обязательных членов. Имя без префикса члена:
        /// она одна на читатель, а не по одной на член.
        /// </summary>
        internal const string RequiredSeen = "required";

        internal static ulong RequiredBit(IReadOnlyList<MemberModel> required, MemberModel member)
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
            //не (1 << N) - 1: при N = 64 сдвиг на 64 в C# берётся по модулю
            //разрядности, то есть даёт 1, а маска - ноль. Ошибка, которая
            //проявилась бы ровно на одном размере типа
            var mask = 0UL;

            for (var i = 0; i < required.Count; i++)
            {
                mask |= 1UL << i;
            }

            return mask;
        }

        /// <summary>
        /// Отказ на документе, в котором не было имени обязательного члена.
        ///
        /// Сообщение повторяет эталонное дословно (проверено пробой,
        /// scratchpad/ReqProbe): <c>JSON deserialization for type 'T' was
        /// missing required properties including: 'a'; 'b'.</c> Повторяет не из
        /// почтения, а из расчёта на compat-слой (§10): там наше исключение
        /// увидит чужой код, написанный под эталон.
        ///
        /// Разделитель в списке - не константа: эталон берёт его у текущей
        /// культуры интерфейса, и <c>'a'; 'b'</c> выше - это вид под ru-RU, а
        /// под en-US будет <c>'a', 'b'</c>. Правило вынесено в
        /// <c>JsonGoddess.Internal.JsonRequiredNames</c>, где и объяснено.
        ///
        /// Перечисляются <b>JSON-имена</b>, а не имена членов: у эталона в
        /// списке стои́т <c>'amt'</c>, когда член назван
        /// <c>[JsonPropertyName("amt")] Amount</c>.
        /// </summary>
        internal static void EmitRequiredCheck(
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
                "throw new " + DocumentException
                + "(\"JSON deserialization for type '" + subject.FullName.Replace("global::", string.Empty)
                + "' was missing required properties including: \" + "
                + MissingName(subject) + "(" + RequiredSeen + ") + \".\", position, "
                + PathAnchor + ".EnclosingObject);"
                );
            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Сборка списка недостающих имён - отдельным методом, а не на месте:
        /// это путь отказа, и ему нечего делать в теле читателя, где каждая
        /// лишняя сотня байт машинного кода мешает JIT'у (§12.6.1).
        /// </summary>
        private static void EmitMissingRequired(
            SourceBuilder builder,
            SubjectModel subject,
            IReadOnlyList<MemberModel> members
            )
        {
            var required = members.Where(m => m.IsRequired).ToList();

            if (required.Count == 0)
            {
                return;
            }

            builder.OpenBlock("private static string " + MissingName(subject) + "(ulong seen)");
            builder.Line(
                "var names = new string[] { "
                + string.Join(", ", required.Select(m => SourceBuilder.Literal(m.JsonName)))
                + ", };"
                );
            builder.Line("var missing = new global::System.Text.StringBuilder();");
            builder.Line();
            builder.OpenBlock("for (var i = 0; i < names.Length; i++)");
            builder.OpenBlock("if ((seen & (1UL << i)) == 0UL)");
            builder.OpenBlock("if (missing.Length > 0)");
            builder.Line("missing.Append(" + RequiredNames + ".Separator);");
            builder.CloseBlock();
            builder.Line();
            builder.Line("missing.Append('\\'').Append(names[i]).Append('\\'');");
            builder.CloseBlock();
            builder.CloseBlock();
            builder.Line();
            builder.Line("return missing.ToString();");
            builder.CloseBlock();
            builder.Line();
        }

        private static string MissingName(SubjectModel subject) => "MissingRequired_" + subject.MethodSuffix;

        /// <summary>
        /// Локальные отложенной формы: по одной на каждый читаемый член, плюс
        /// флаг присутствия на тех, кого присваивает не конструктор.
        ///
        /// Аргумент заводится <b>объявленным умолчанием параметра</b>, и это
        /// заменяет флаг для него целиком: не было имени в документе -
        /// осталось умолчание, ровно как у эталона (проверено прогоном:
        /// <c>beta = 42</c> на документе без <c>beta</c> даёт 42).
        /// </summary>
        internal static void EmitDeferredLocals(
            SourceBuilder builder,
            SubjectModel subject,
            IReadOnlyList<MemberModel> members
            )
        {
            foreach (var parameter in subject.Parameters)
            {
                var member = members.First(m => m.MemberName == parameter.MemberName);
                builder.Line("var " + Argument(member) + " = " + parameter.DefaultExpression + ";");
            }

            foreach (var member in members.Where(m => !m.IsConstructorParameter))
            {
                builder.Line("var " + Assigned(member) + " = default(" + member.Value.Declaration + ");");
                builder.Line("var " + Seen(member) + " = false;");
            }

            builder.Line();
        }

        /// <summary>
        /// Выражение, строящее объект.
        ///
        /// Обязательные члены идут <b>инициализатором</b>, и не по выбору:
        /// <c>new T()</c> у типа с <c>required</c>-членом компилятор не
        /// принимает (CS9035). Условности вроде <c>if (has_X)</c> здесь не
        /// нужно и не может быть - имя обязательного члена в документе было,
        /// иначе досюда бы не дошли.
        /// </summary>
        internal static string Construct(SubjectModel subject, IReadOnlyList<MemberModel> members)
        {
            var arguments = subject.Parameters
                .Select(p => Argument(members.First(m => m.MemberName == p.MemberName)));

            //с фабрикой сюда не приходят: она и конструктор с параметрами -
            //отказ на связывании, а обязательные члены при фабрике
            //присваиваются на месте и отложенной формы не требуют
            var expression = subject.FactoryInvocation
                ?? "new " + subject.FullName + "(" + string.Join(", ", arguments) + ")";

            var initialized = subject.RequiredInitialized;

            if (initialized.Count == 0)
            {
                return expression;
            }

            return expression + " { "
                + string.Join(", ", initialized.Select(m => m.MemberName + " = " + Assigned(m)))
                + ", }";
        }

        internal static string Target(MemberModel member, bool deferred)
        {
            if (!deferred)
            {
                return "result." + member.MemberName;
            }

            return member.IsConstructorParameter ? Argument(member) : Assigned(member);
        }

        //префиксы, а не голые имена члена: в теле читателя уже живут name,
        //position, context, result и локальные, которые печатает читатель
        //значения, - и совпасть с любым из них член вправе
        private static string Argument(MemberModel member) => "arg_" + member.MemberName;

        internal static string Assigned(MemberModel member) => "set_" + member.MemberName;

        internal static string Seen(MemberModel member) => "has_" + member.MemberName;

        /// <summary>
        /// Читатель коллекции. Отличие от объекта не только в скобках: у
        /// массива нет имён, значит нет и диспетчера, - весь цикл сводится к
        /// «прочитать элемент, проверить запятую».
        ///
        /// Массив читается в <c>T[]</c> с удвоением и подрезкой в конце, а не
        /// через <c>List&lt;T&gt;.ToArray()</c>: у второго способа ровно те же
        /// перевыделения плюс лишняя копия и лишний объект.
        /// </summary>
        /// <summary>
        /// Запись enum'а именем. Значение вне набора уходит числом - это не
        /// послабление, а поведение эталона: <c>(Named)77</c> он пишет как
        /// <c>77</c>, а не отказывает.
        /// </summary>
        private static void EmitEnumWriter(
            SourceBuilder builder, EnumModel enumModel, string exhauster, bool escapeNames
            )
        {
            builder.OpenBlock(
                "private static void WriteEnum_" + enumModel.MethodSuffix + "("
                + exhauster + " exhauster, " + enumModel.FullName + " value)"
                );

            builder.OpenBlock("switch (value)");

            foreach (var member in enumModel.Members)
            {
                builder.Line("case " + enumModel.FullName + "." + member.MemberName + ":");
                builder.Indent();
                builder.Line(
                    "exhauster.AppendRaw("
                    + SourceBuilder.Utf8Literal("\"" + Name(member.JsonName, escapeNames) + "\"") + ");"
                    );
                builder.Line("return;");
                builder.Unindent();
                builder.Line();
            }

            builder.CloseBlock();
            builder.Line();

            builder.Line("exhauster.Append((" + BuiltinTypes.GetTypeName(enumModel.Underlying) + ")value);");

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Чтение enum'а именем. Форм на входе три, и все три законны у эталона:
        /// имя, имя в экранированном виде и число - в кавычках или без.
        ///
        /// Регистр сворачивается только у тех членов, чьё имя пришло из C#:
        /// имя из <c>[JsonStringEnumMemberName]</c> эталон принимает лишь в
        /// точности, и это проверено прогоном, а не выведено из его исходников.
        /// </summary>
        private static void EmitEnumReader(
            SourceBuilder builder, EnumModel enumModel, string injector, JsonGuard guards, JsonFeature features
            )
        {
            var underlying = BuiltinTypes.GetTypeName(enumModel.Underlying);

            builder.Line("private static " + enumModel.FullName + " ReadEnum_" + enumModel.MethodSuffix + "(");
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            EmitCommentSkip(builder, features);
            builder.OpenBlock(
                "if (" + Scan + ".Peek(json, ref position) == " + TokenKind + ".String)"
                );

            builder.Line("var raw = " + ValueSourceProducer.StringReadCall(guards) + "(json, ref position, out var rawEscaped);");
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
                        ? Mem + ".SequenceEqual(raw, "
                            + SourceBuilder.Utf8Literal(member.JsonName) + ")"
                        : AsciiName + ".EqualsIgnoreCase(raw, " + SourceBuilder.Utf8Literal(member.JsonName) + ")";

                    builder.OpenBlock("if (" + comparison + ")");
                    builder.Line("return " + enumModel.FullName + "." + member.MemberName + ";");
                    builder.CloseBlock();
                    builder.Line();
                }

                builder.Line("break;");
                builder.CloseBlock();
                builder.Line();
            }

            builder.CloseBlock();
            builder.Line();

            //имя не подошло - остаётся число в кавычках; не число даст отказ
            //разбора, а он и требуется
            builder.Line("injector.Parse(ref context, raw, out " + underlying + " named);");
            builder.Line("return (" + enumModel.FullName + ")named;");

            builder.CloseBlock();
            builder.Line();

            builder.Line("var rawNumber = " + ValueSourceProducer.NumberReadCall(guards) + "(json, ref position);");
            builder.Line("injector.Parse(ref context, rawNumber, out " + underlying + " number);");
            builder.Line("return (" + enumModel.FullName + ")number;");

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Читатель скаляра - метод на пару «вид плюс nullability», общий на
        /// весь хост.
        ///
        /// Печаталось это по месту, и на член приходилось три строки, а на
        /// nullable-член - девять: проверка на null, обе ветки и две локальные.
        /// Умноженное на число членов всех субъектов, это и оказалось самой
        /// крупной повторяющейся частью читателя (§16.2 плана). Сигнатура та
        /// же, что у читателя коллекции и субъекта, - к ветке диспетчера
        /// сводится одно присваивание.
        /// </summary>
        private static void EmitScalarReader(
            SourceBuilder builder, ValueModel scalar, string injector, JsonGuard guards, JsonFeature features
            )
        {
            builder.Line(Inline);
            builder.Line(
                "private static " + scalar.Declaration + " ReadScalar_" + scalar.MethodSuffix + "("
                );
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            EmitCommentSkip(builder, features);
            ValueSourceProducer.ReadScalarBody(builder, scalar, guards, features);

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Писатель коллекции - метод, а не код по месту.
        ///
        /// Раньше цикл печатался в каждом члене, и обоснование стояло прямое:
        /// «выносить его в метод значило бы добавить вызов ради экономии,
        /// которой нет». Экономия померена (§16.2 плана): на графе из двухсот
        /// типов тело коллекции занимало три четверти всего писателя, потому
        /// что один и тот же <c>Dictionary&lt;string,int&gt;</c> печатался
        /// двести раз подряд. Список коллекций уже собран - его печатает
        /// читатель, - так что метод берётся из того же списка, и обе стороны
        /// стали симметричны.
        /// </summary>
        private static void EmitCollectionWriter(
            SourceBuilder builder,
            ValueModel collection,
            string exhauster,
            JsonNamingStyle keyNaming,
            JsonFeature features
            )
        {
            builder.OpenBlock(
                "private static void WriteCollection_" + collection.MethodSuffix + "("
                + exhauster + " exhauster, " + collection.TypeName + "? items)"
                );

            ValueSourceProducer.WriteCollectionBody(builder, collection, keyNaming, features);

            builder.CloseBlock();
            builder.Line();
        }

        /// <summary>
        /// Тело писателя субъекта-коллекции (§9.10 плана). Параметр уже
        /// объявлен сигнатурой обычного писателя субъекта - <c>value</c>, а не
        /// <c>items</c>, - поэтому тело печатается своей копией цикла, а не
        /// вызовом <see cref="ValueSourceProducer.WriteCollectionBody"/>: та
        /// сама печатает проверку на <c>null</c> перед собой, а здесь она уже
        /// напечатана снаружи (и не печатается вовсе для структуры).
        /// </summary>
        private static void EmitCollectionSubjectWriterBody(
            SourceBuilder builder,
            SubjectModel subject,
            JsonNamingStyle keyNaming,
            JsonFeature features
            )
        {
            var shape = subject.CollectionShape!;
            var isDictionary = shape.IsDictionary;

            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isDictionary ? "{" : "[") + ");");
            builder.Line("var i = 0;");
            builder.OpenBlock("foreach (var " + (isDictionary ? "pair" : "element") + " in value)");

            builder.OpenBlock("if (i > 0)");
            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(",") + ");");
            builder.CloseBlock();
            builder.Line();
            builder.Line("i++;");

            if (isDictionary)
            {
                builder.Line("exhauster.Append(" + ValueSourceProducer.Key("pair.Key", keyNaming) + ");");
                builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(":") + ");");
                ValueSourceProducer.WriteValue(builder, shape.Element, "pair.Value", keyNaming, features);
            }
            else
            {
                ValueSourceProducer.WriteValue(builder, shape.Element, "element", keyNaming, features);
            }

            builder.CloseBlock();
            builder.Line();
            builder.Line("exhauster.AppendRaw(" + SourceBuilder.Utf8Literal(isDictionary ? "}" : "]") + ");");
        }

        /// <summary>
        /// Тело читателя субъекта-коллекции (§9.10 плана). Элемент кладётся в
        /// уже построенный <c>result</c> через явное приведение к интерфейсу
        /// (<c>ICollection&lt;T&gt;</c>/<c>IDictionary&lt;string,V&gt;</c>), а
        /// не напрямую: субъект мог реализовать интерфейс явно (без открытого
        /// метода на самом себе), и приведение работает в обоих случаях
        /// одинаково, тогда как прямой вызов - только в одном.
        /// </summary>
        private static void EmitCollectionSubjectReaderBody(
            SourceBuilder builder,
            SubjectModel subject,
            JsonGuard guards,
            int maxDepth,
            JsonFeature features
            )
        {
            var shape = subject.CollectionShape!;
            var element = shape.Element;
            var guardsDepth = (guards & JsonGuard.MaxDepth) != 0;

            var open = Scan + (shape.IsDictionary ? ".OpenBrace" : ".OpenBracket");
            var close = Scan + (shape.IsDictionary ? ".CloseBrace" : ".CloseBracket");

            //комментарий перед всей коллекцией пропущен ещё в EmitReader,
            //перед TryReadNull - между тем пропуском и этим Expect ничего не
            //стоит
            builder.Line(Scan + ".Expect(json, ref position, " + open + ");");
            EmitDepthCheckAndOpenTry(builder, guardsDepth, maxDepth);
            builder.Line();

            //а вот комментарий сразу после открывающей скобки ещё никем не
            //пропущен
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (" + Scan + ".TryConsume(json, ref position, " + close + "))");
            builder.Line("return " + subject.NewExpression + ";");
            builder.CloseBlock();
            builder.Line();

            builder.Line("var result = " + subject.NewExpression + ";");
            builder.Line();
            builder.OpenBlock("while (true)");

            if (shape.IsDictionary)
            {
                //ключ приходится материализовать строкой - положить спан в
                //чужую реализацию IDictionary<string,V> нечем
                EmitCommentSkip(builder, features);
                builder.Line(
                    "var rawKey = " + ValueSourceProducer.StringReadCall(guards)
                    + "(json, ref position, out var keyEscaped);"
                    );

                if ((guards & JsonGuard.InvalidUtf8) != 0)
                {
                    builder.Line(ValueSourceProducer.StringDecoder + ".EnsureValidUtf8(rawKey, keyEscaped);");
                }

                builder.Line("injector.ParseText(ref context, rawKey, keyEscaped, out string key);");
                builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".Colon);");
                builder.Line();
            }

            builder.Line(element.Declaration + " item;");
            ValueSourceProducer.ReadValue(builder, element, "item");
            builder.Line();

            builder.Line(
                shape.IsDictionary
                    ? "((global::System.Collections.Generic.IDictionary<string, " + element.Declaration
                        + ">)result)[key] = item;"
                    : "((global::System.Collections.Generic.ICollection<" + element.Declaration + ">)result).Add(item);"
                );

            builder.Line();
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (!" + Scan + ".TryConsume(json, ref position, " + Scan + ".Comma))");
            builder.Line("break;");
            builder.CloseBlock();

            EmitTrailingCommaCheck(builder, features, TokenKind + (shape.IsDictionary ? ".EndObject" : ".EndArray"));

            builder.CloseBlock();
            builder.Line();

            builder.Line(Scan + ".Expect(json, ref position, " + close + ");");
            builder.Line("return result;");
            EmitDepthCheckCloseTry(builder, guardsDepth);
        }

        /// <summary>
        /// Общая половина <c>JsonGuard.MaxDepth</c> для читателей коллекций:
        /// увеличить счётчик сразу после открывающей скобки, отказать, если
        /// он превысил предел, и открыть <c>try</c> на всё, что дальше -
        /// возвратов из читателя коллекции несколько (пустая коллекция, конец
        /// цикла), и декремент обязан отработать на любом из них.
        /// </summary>
        private static void EmitDepthCheckAndOpenTry(SourceBuilder builder, bool guardsDepth, int maxDepth)
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
            builder.OpenBlock("try");
        }

        private static void EmitDepthCheckCloseTry(SourceBuilder builder, bool guardsDepth)
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

            var open = Scan + (isMap ? ".OpenBrace" : ".OpenBracket");
            var close = Scan + (isMap ? ".CloseBrace" : ".CloseBracket");

            builder.Line(
                "private static " + collection.TypeName + "? ReadCollection_" + collection.MethodSuffix + "("
                );
            builder.Indent();
            builder.Line(injector + " injector,");
            builder.Line("scoped " + Span + " json,");
            builder.Line("scoped ref int position,");
            builder.Line("scoped ref " + Context + " context");
            builder.Line(")");
            builder.Unindent();
            builder.OpenBlock();

            //первое обращение к сканеру в методе - комментарий перед всей
            //коллекцией ("// hi\n[...]") ещё не пропущен никем
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (" + Scan + ".TryReadNull(json, ref position))");
            builder.Line("return null;");
            builder.CloseBlock();
            builder.Line();

            builder.Line(Scan + ".Expect(json, ref position, " + open + ");");
            EmitDepthCheckAndOpenTry(builder, guardsDepth, maxDepth);
            builder.Line();

            //комментарий сразу после открывающей скобки - TryReadNull выше
            //успел пропустить только то, что было ПЕРЕД коллекцией, не после
            //неё открывшейся скобки
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (" + Scan + ".TryConsume(json, ref position, " + close + "))");
            builder.Line(
                isArray
                    ? "return " + ValueSourceProducer.Array + ".Empty<" + element.Declaration + ">();"
                    : "return new " + collection.ConstructTypeName + "();"
                );
            builder.CloseBlock();
            builder.Line();

            if (isArray)
            {
                builder.Line("var result = new " + element.Declaration + "[4];");
                builder.Line("var count = 0;");
            }
            else
            {
                //ConstructTypeName - конкретный тип (List<T>/Dictionary<string,V>),
                //а не объявленный: для List<T>/Dictionary<string,V> самих по
                //себе это одно и то же, а для интерфейсов на месте члена
                //(фаза 6, §9.10) строить нечем, кроме конкретного - ровно то,
                //что подставляет и сам эталон. var ниже забирает этот
                //конкретный тип, и возврат как объявленный (интерфейс)
                //происходит неявным приведением на return result.
                builder.Line("var result = new " + collection.ConstructTypeName + "();");
            }

            builder.Line();
            builder.OpenBlock("while (true)");

            if (isMap)
            {
                //ключ словаря приходится материализовать строкой: с именем
                //члена его не сравнить - членов тут нет, - и положить в словарь
                //спан нельзя
                EmitCommentSkip(builder, features);
                builder.Line(
                    "var rawKey = " + ValueSourceProducer.StringReadCall(guards)
                    + "(json, ref position, out var keyEscaped);"
                    );

                if ((guards & JsonGuard.InvalidUtf8) != 0)
                {
                    builder.Line(ValueSourceProducer.StringDecoder + ".EnsureValidUtf8(rawKey, keyEscaped);");
                }

                builder.Line("injector.ParseText(ref context, rawKey, keyEscaped, out string key);");
                builder.Line(Scan + ".Expect(json, ref position, " + Scan + ".Colon);");
                builder.Line();
            }

            builder.Line(element.Declaration + " item;");
            ValueSourceProducer.ReadValue(builder, element, "item");
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
                //индексатор, а не Add: повторённый ключ у System.Text.Json
                //выигрывает последним вхождением, а Add бросил бы
                builder.Line("result[key] = item;");
            }
            else
            {
                builder.Line("result.Add(item);");
            }

            builder.Line();
            EmitCommentSkip(builder, features);
            builder.OpenBlock("if (!" + Scan + ".TryConsume(json, ref position, " + Scan + ".Comma))");
            builder.Line("break;");
            builder.CloseBlock();

            EmitTrailingCommaCheck(builder, features, TokenKind + (isMap ? ".EndObject" : ".EndArray"));

            builder.CloseBlock();
            builder.Line();

            builder.Line(Scan + ".Expect(json, ref position, " + close + ");");

            if (isArray)
            {
                builder.OpenBlock("if (count != result.Length)");
                builder.Line(ValueSourceProducer.Array + ".Resize(ref result, count);");
                builder.CloseBlock();
                builder.Line();
            }

            builder.Line("return result;");
            EmitDepthCheckCloseTry(builder, guardsDepth);

            builder.CloseBlock();
            builder.Line();
        }
    }
}
