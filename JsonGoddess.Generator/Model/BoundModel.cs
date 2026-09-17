using System;
using System.Collections.Generic;
using JsonGoddess.Generator.Shared;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Зеркало <c>JsonGoddess.JsonGuard</c> (§6.3 плана) на стороне генератора.
    ///
    /// Своя копия, а не ссылка на рантайм-тип, - по той же причине, по которой
    /// <c>KnownSymbols</c> заводит числовые константы для
    /// <c>JsonIgnoreCondition</c>: генератор читает аргумент атрибута из
    /// метаданных чужой компиляции числом (<c>TypedConstant.Value is int</c>),
    /// и тащить в компилятор ссылку на сборку потребителя ради одного
    /// перечисления - плохая сделка. Битовая раскладка обязана совпасть с
    /// <c>JsonGoddess.JsonGuard</c> один в один - это и есть контракт между
    /// двумя копиями.
    /// </summary>
    [Flags]
    public enum JsonGuard
    {
        None = 0,
        DuplicateProperties = 1 << 0,
        TrailingContent = 1 << 1,
        ControlCharsInStrings = 1 << 2,
        StrictNumbers = 1 << 3,
        InvalidUtf8 = 1 << 4,
        MaxDepth = 1 << 5,
        UnknownProperties = 1 << 6,
    }

    /// <summary>
    /// Зеркало <c>JsonGoddess.JsonFeature</c> (§6.2 плана) - той же природы,
    /// что и <see cref="JsonGuard"/> выше, и по той же причине своя копия, а
    /// не ссылка на рантайм-тип.
    /// </summary>
    [Flags]
    public enum JsonFeature
    {
        None = 0,
        Comments = 1 << 0,
        TrailingCommas = 1 << 1,
        NamedFloatingPointLiterals = 1 << 2,
        NumbersFromStrings = 1 << 3,
        CaseInsensitiveNames = 1 << 4,
    }

    /// <summary>
    /// Builtin-типы, которые генератор умеет. Набор равен не списку из §7.1
    /// плана, а тому, что <b>есть у sink'ов</b>: <c>IExhauster</c> и
    /// <c>IInjector</c> - единственный источник истины о том, какую лексему
    /// кто-то умеет написать и прочитать. Тип, которого у sink'а нет, генератор
    /// обязан отвергнуть (JGD022), а не обслужить приблизительно.
    /// </summary>
    public enum BuiltinKind
    {
        Boolean,
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        String,
        DateTime,
        DateTimeOffset,
        TimeSpan,
        Guid,
        ByteArray,
    }

    /// <summary>
    /// Чем значение приезжает в документе. Деление то же, что у
    /// <c>IInjector</c>, и оно не по типу, а по лексеме.
    /// </summary>
    public enum LexemeKind
    {
        /// <summary>Число: <c>ReadNumberRaw</c> + <c>Parse</c>.</summary>
        Number,

        /// <summary><c>true</c>/<c>false</c>: <c>ReadLiteralRaw</c> + <c>Parse</c>.</summary>
        Literal,

        /// <summary>JSON-строка: <c>ReadStringContent</c> + <c>ParseText</c>.</summary>
        Text,
    }

    /// <summary>
    /// Чем значение является в дереве типов. Деление ровно по тому, какой код
    /// его обслуживает: лексема, вызов <c>Read_</c>/<c>Write_</c> другого
    /// субъекта или цикл по элементам.
    /// </summary>
    public enum ValueForm
    {
        Builtin,
        Subject,

        /// <summary>
        /// Индексированная запись (<c>for</c> + <c>Count</c>/<c>Length</c> +
        /// индексатор): <c>List&lt;T&gt;</c>, а с фазы 6 - также
        /// <c>IList&lt;T&gt;</c> и <c>IReadOnlyList&lt;T&gt;</c>, у которых
        /// индексатор гарантирован интерфейсом. Различает их только
        /// <see cref="ValueModel.TypeName"/>: тело писателя и читателя общее.
        /// </summary>
        List,
        Array,

        /// <summary>
        /// <c>Dictionary&lt;string, V&gt;</c>, а с фазы 6 - также
        /// <c>IDictionary&lt;string, V&gt;</c> и
        /// <c>IReadOnlyDictionary&lt;string, V&gt;</c>: объект, имена свойств
        /// которого неизвестны на этапе компиляции.
        /// </summary>
        Dictionary,

        /// <summary>
        /// Запись <c>foreach</c>'ем со счётчиком, без индексатора: фаза 6,
        /// <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
        /// <c>IReadOnlyCollection&lt;T&gt;</c> - интерфейсы, у которых
        /// <c>this[int]</c> не гарантирован. Чтение у них то же самое, что у
        /// <see cref="List"/> (<c>List&lt;T&gt;</c> собирается и через
        /// <c>Add</c>, и индексатором не пользуется), различается только
        /// запись.
        /// </summary>
        Enumerable,

        Enum,
    }

    /// <summary>
    /// Член enum'а в строковом режиме.
    /// </summary>
    public sealed class EnumMemberModel
    {
        /// <summary>Имя члена в C#: печатается в <c>case global::Ns.T.Draft</c>.</summary>
        public string MemberName { get; }

        /// <summary>Имя в документе. Уже с учётом <c>[JsonStringEnumMemberName]</c>.</summary>
        public string JsonName { get; }

        /// <summary>
        /// Сравнивать точно, а не сворачивая регистр. Так ведёт себя
        /// <c>System.Text.Json</c>: имя, пришедшее из <c>[JsonStringEnumMemberName]</c>,
        /// он принимает только в точности, а C#-идентификатор - в любом
        /// регистре. Проверено прогоном: <c>"SENT-OUT"</c> он отвергает,
        /// <c>"draft"</c> принимает.
        /// </summary>
        public bool MatchExactly { get; }

        public EnumMemberModel(string memberName, string jsonName, bool matchExactly)
        {
            MemberName = memberName;
            JsonName = jsonName;
            MatchExactly = matchExactly;
        }
    }

    public sealed class EnumModel
    {
        public string FullName { get; }

        public string MethodSuffix { get; }

        /// <summary>Подлежащий тип: им читается и пишется числовая форма.</summary>
        public BuiltinKind Underlying { get; }

        public IReadOnlyList<EnumMemberModel> Members { get; }

        public EnumModel(
            string fullName,
            string methodSuffix,
            BuiltinKind underlying,
            IReadOnlyList<EnumMemberModel> members
            )
        {
            FullName = fullName;
            MethodSuffix = methodSuffix;
            Underlying = underlying;
            Members = members;
        }
    }

    /// <summary>
    /// Значение - узел дерева, а не плоский вид. <c>List&lt;List&lt;int&gt;&gt;</c>
    /// в JSON обычная форма, поэтому вложенность здесь не частный случай, а
    /// строение модели: элемент коллекции описывается тем же самым
    /// <see cref="ValueModel"/>.
    /// </summary>
    public sealed class ValueModel
    {
        public ValueForm Form { get; }

        /// <summary>Значим только при <see cref="ValueForm.Builtin"/>.</summary>
        public BuiltinKind Builtin { get; }

        /// <summary>
        /// Имя типа, каким оно печатается в объявление локальной переменной, в
        /// параметр и в возврат. Без <c>?</c>: nullability приписывается по
        /// <see cref="IsNullable"/>.
        ///
        /// Для коллекции это ровно тот тип, что стоит на месте члена -
        /// <c>List&lt;T&gt;</c>, но с фазы 6 и <c>IList&lt;T&gt;</c>,
        /// <c>IReadOnlyDictionary&lt;string, V&gt;</c> и так далее. Тип,
        /// которым коллекция на самом деле <c>new</c>'ится на чтении, может
        /// быть другим - см. <see cref="ConstructTypeName"/>.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Тип, которым читатель коллекции создаёт результат: <c>new
        /// ConstructTypeName()</c>, присвоенный <c>var</c> - и потому
        /// возвращаемый как <see cref="TypeName"/> без явного приведения, раз
        /// он ему присваиваем.
        ///
        /// Равен <see cref="TypeName"/> для <c>List&lt;T&gt;</c>,
        /// <c>T[]</c> и <c>Dictionary&lt;string, V&gt;</c> - там объявленный
        /// тип и есть конкретный. Расходится он только у интерфейсов на месте
        /// члена (фаза 6): <c>IList&lt;T&gt;</c> и его пять соседей объявляют
        /// тип члена, а строить читателю нечем, кроме конкретного
        /// <c>List&lt;T&gt;</c>/<c>Dictionary&lt;string, V&gt;</c> - тот же
        /// выбор, что делает и сам эталон (проверено пробой).
        /// </summary>
        public string ConstructTypeName { get; }

        /// <summary>
        /// Суффикс имён методов: для субъекта - его же, общий с
        /// <see cref="SubjectModel.MethodSuffix"/>; для коллекции - её
        /// собственный, по которому печатается <c>ReadCollection_</c>.
        /// </summary>
        public string MethodSuffix { get; }

        /// <summary>Элемент коллекции. <c>null</c> для всего остального.</summary>
        public ValueModel? Element { get; }

        /// <summary>
        /// Может ли на месте значения стоять <c>null</c>. От этого зависит,
        /// нужна ли обёртка <c>TryReadNull</c>: для значимого не-nullable члена
        /// её нет, и это не экономия ветки, а отказ принять документ, которого
        /// тип описать не может.
        /// </summary>
        public bool IsNullable { get; }

        /// <summary>Значим только при <see cref="ValueForm.Enum"/>.</summary>
        public EnumModel? Enum { get; }

        /// <summary>
        /// Enum пишется именем, а не числом. Это свойство <b>типа</b>, а не
        /// места: маркером служит <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>
        /// на самом enum'е. Конвертер на члене мы отвергаем, потому что
        /// повторить произвольный конвертер нельзя, а притвориться - худший из
        /// исходов.
        /// </summary>
        public bool IsStringEnum { get; }

        /// <summary>
        /// Значение - структура. Значим при <see cref="ValueForm.Subject"/>, и
        /// вместе с <see cref="IsNullable"/> задаёт все три случая: класс
        /// (<c>null</c> разбирает сам <c>Write_</c>/<c>Read_</c>), структура
        /// (<c>null</c> невозможен) и <c>Nullable&lt;структура&gt;</c>
        /// (<c>null</c> разбирается здесь, потому что <c>Write_</c> принимает
        /// не-nullable).
        /// </summary>
        public bool IsValueType { get; }

        public ValueModel(
            ValueForm form,
            BuiltinKind builtin,
            string typeName,
            string methodSuffix,
            ValueModel? element,
            bool isNullable,
            EnumModel? enumModel = null,
            bool isStringEnum = false,
            bool isValueType = false,
            string? constructTypeName = null
            )
        {
            IsValueType = isValueType;
            Form = form;
            Builtin = builtin;
            TypeName = typeName;
            ConstructTypeName = constructTypeName ?? typeName;
            MethodSuffix = methodSuffix;
            Element = element;
            IsNullable = isNullable;
            Enum = enumModel;
            IsStringEnum = isStringEnum;
        }

        /// <summary>Тип, каким он печатается в объявление: с <c>?</c>, если значение может быть null.</summary>
        public string Declaration => IsNullable ? TypeName + "?" : TypeName;

        /// <summary>
        /// Есть элемент, значит нужен свой метод чтения и рекурсия по
        /// элементу. Словарь здесь тоже коллекция: от списка он отличается
        /// только тем, что перед каждым элементом стоит ключ.
        /// </summary>
        public bool IsCollection =>
            Form == ValueForm.List || Form == ValueForm.Array || Form == ValueForm.Dictionary
            || Form == ValueForm.Enumerable;
    }

    /// <summary>
    /// Когда член попадает в документ. Названо от записи, а не от пропуска
    /// (<c>JsonIgnoreCondition</c> у эталона названо наоборот), потому что
    /// эмиттер печатает условие записи, и двойное отрицание в коде читалось бы
    /// хуже, чем в имени.
    /// </summary>
    public enum WriteCondition
    {
        /// <summary>Всегда. Разделители вокруг такого члена - константы.</summary>
        Always,

        /// <summary><c>WhenWritingNull</c>: только если значение не <c>null</c>.</summary>
        WhenNotNull,

        /// <summary><c>WhenWritingDefault</c>: только если значение не равно <c>default</c>.</summary>
        WhenNotDefault,
    }

    public sealed class MemberModel
    {
        /// <summary>Имя члена в C#: <c>value.Id</c>, <c>result.Id</c>.</summary>
        public string MemberName { get; }

        /// <summary>Имя свойства в документе. Уже с учётом [JsonPropertyName].</summary>
        public string JsonName { get; }

        /// <summary>UTF-8-байты <see cref="JsonName"/>: по ним считается и длина, и ключ.</summary>
        public byte[] JsonNameUtf8 { get; }

        public ValueModel Value { get; }

        /// <summary>Есть публичный getter - член попадает в запись.</summary>
        public bool CanWrite { get; }

        /// <summary>Есть публичный setter - член попадает в чтение.</summary>
        public bool CanRead { get; }

        /// <summary>
        /// Условие записи. На чтении не значит ничего: эталон читает условно
        /// опускаемый член так же, как любой другой.
        /// </summary>
        public WriteCondition Condition { get; }

        /// <summary><c>[JsonPropertyOrder]</c>; по умолчанию 0.</summary>
        public int Order { get; }

        /// <summary>
        /// Член связан с параметром конструктора.
        ///
        /// Присваивается он тогда <b>только</b> конструктором, и setter'а, если
        /// он есть, не касается никто. Проверено прогоном: у члена с setter'ом,
        /// связанного с параметром, из документа со значением 1 выходит 10 -
        /// то есть отработал конструктор, умножающий на десять, а setter после
        /// него не отработал.
        /// </summary>
        public bool IsConstructorParameter { get; }

        /// <summary>
        /// Setter объявлен как <c>init</c>.
        ///
        /// Присвоить его можно только в инициализаторе объекта, то есть уже
        /// после того, как всё прочитано. Эталон такие члены обслуживает
        /// (проверено прогоном), поэтому отказывать нельзя - но и печатать
        /// <c>result.X = ...</c> внутри цикла тоже.
        /// </summary>
        public bool IsInitOnly { get; }

        public MemberModel(
            string memberName,
            string jsonName,
            byte[] jsonNameUtf8,
            ValueModel value,
            bool canWrite,
            bool canRead,
            WriteCondition condition,
            int order,
            bool isConstructorParameter = false,
            bool isInitOnly = false
            )
        {
            IsConstructorParameter = isConstructorParameter;
            IsInitOnly = isInitOnly;
            MemberName = memberName;
            JsonName = jsonName;
            JsonNameUtf8 = jsonNameUtf8;
            Value = value;
            CanWrite = canWrite;
            CanRead = canRead;
            Condition = condition;
            Order = order;
        }
    }

    /// <summary>
    /// Параметр конструктора десериализации.
    ///
    /// Своего значения у него нет: он всегда связан с членом, и эталон
    /// настаивает на этом жёстче нас - параметр, которому не нашлось члена, у
    /// него <c>InvalidOperationException</c>, и не в момент чтения такого
    /// документа, а на любом. Поэтому связь выражена именем члена, а не типом.
    /// </summary>
    public sealed class ParameterModel
    {
        /// <summary>Имя связанного члена: по нему же названа локальная переменная.</summary>
        public string MemberName { get; }

        /// <summary>
        /// Значение параметра, если его имени в документе не было.
        ///
        /// Умолчание, объявленное в конструкторе, а не <c>default(T)</c>:
        /// проверено прогоном - <c>ctor(int alpha, int beta = 42)</c> на
        /// документе без <c>beta</c> даёт 42. Инициализация локальной этим
        /// выражением заменяет отслеживание «было ли имя в документе» целиком.
        /// </summary>
        public string DefaultExpression { get; }

        public ParameterModel(string memberName, string defaultExpression)
        {
            MemberName = memberName;
            DefaultExpression = defaultExpression;
        }
    }

    /// <summary>
    /// Производный тип, объявленный на базе через <c>[JsonDerivedType]</c>.
    ///
    /// Дискриминатор принадлежит <b>паре</b> «база + производный», а не
    /// производному типу: один и тот же класс может стоять под двумя базами с
    /// разными значениями, и писаться в каждом случае по-своему. Отсюда и имена
    /// порождаемых методов - с обоими суффиксами.
    /// </summary>
    public sealed class DerivedTypeModel
    {
        public string FullName { get; }

        public string MethodSuffix { get; }

        /// <summary>
        /// Значение дискриминатора, уже готовое для печати в документ:
        /// <c>"dog"</c> с кавычками или <c>7</c> без них. <c>null</c> - у
        /// <c>[JsonDerivedType(typeof(D))]</c> без значения: эталон такой тип
        /// пишет <b>без</b> дискриминатора (проверено прогоном), то есть
        /// односторонне - прочитать документ обратно производным он уже не
        /// сможет.
        /// </summary>
        public string? DiscriminatorLiteral { get; }

        public DerivedTypeModel(string fullName, string methodSuffix, string? discriminatorLiteral)
        {
            FullName = fullName;
            MethodSuffix = methodSuffix;
            DiscriminatorLiteral = discriminatorLiteral;
        }
    }

    /// <summary>
    /// Субъект, который сам является коллекцией (§9.10 плана): у него нет
    /// обычных членов вовсе, а тело <c>Write_</c>/<c>Read_</c> - это цикл по
    /// элементам, а не по <see cref="SubjectModel.Members"/> (он у такого
    /// субъекта всегда пуст).
    ///
    /// Собственные свойства такого типа теряются - молча, но <b>так же</b>,
    /// как их теряет эталон (проверено пробой: <c>ICollection&lt;string&gt;</c>
    /// с property-членом он пишет как массив без единого свойства). Это не
    /// недосмотр, а решение §9.10: терять их одинаково не значит выдавать
    /// другой документ.
    /// </summary>
    public sealed class CollectionShapeModel
    {
        /// <summary>Элемент (список) или значение (словарь) - без имени ключа, оно у словаря не типизировано.</summary>
        public ValueModel Element { get; }

        /// <summary>
        /// <c>true</c> - субъект реализует <c>IDictionary&lt;string, V&gt;</c>
        /// и пишется объектом. Проверка стоит раньше <c>ICollection&lt;T&gt;</c>
        /// нарочно: <c>Dictionary&lt;TKey,TValue&gt;</c> реализует оба, а
        /// эталон смотрит на словарь первым (проверено пробой на
        /// <c>class X : Dictionary&lt;string,int&gt;</c> - пишется <c>{}</c>,
        /// а не <c>[]</c>).
        /// </summary>
        public bool IsDictionary { get; }

        public CollectionShapeModel(ValueModel element, bool isDictionary)
        {
            Element = element;
            IsDictionary = isDictionary;
        }
    }

    public sealed class SubjectModel
    {
        /// <summary>Полное имя с <c>global::</c>: печатается в код как есть.</summary>
        public string FullName { get; }

        /// <summary>Суффикс имён порождаемых методов: <c>Read_Ns_Order</c>.</summary>
        public string MethodSuffix { get; }

        public bool IsRoot { get; }

        /// <summary>
        /// Субъект - структура.
        ///
        /// Меняет три места, и каждое - по своей причине. Писателю не нужна
        /// проверка на <c>null</c>: значение её описать не может. Читателю не
        /// нужна ветка <c>TryReadNull</c>, и это не экономия - <c>null</c> на
        /// месте структуры обязан кончиться отказом, а он и кончается им сам,
        /// на <c>Expect(OpenBrace)</c>. Точке входа не нужен <c>?</c> у типа:
        /// у структуры он означал бы <c>Nullable&lt;T&gt;</c>, то есть другую
        /// сигнатуру, чем <c>Serialize&lt;T&gt;</c> у эталона.
        /// </summary>
        public bool IsValueType { get; }

        public IReadOnlyList<MemberModel> Members { get; }

        /// <summary>
        /// Параметры конструктора десериализации. Пусто - конструктор без
        /// параметров, то есть обычный случай.
        /// </summary>
        public IReadOnlyList<ParameterModel> Parameters { get; }

        /// <summary>
        /// Производные типы, объявленные на этом. Пусто - тип неполиморфен, и
        /// ни одной лишней строки в порождаемом коде у него не появляется.
        /// </summary>
        public IReadOnlyList<DerivedTypeModel> Derived { get; }

        /// <summary>
        /// Имя свойства-дискриминатора: <c>$type</c>, если не сказано иное
        /// через <c>[JsonPolymorphic(TypeDiscriminatorPropertyName = ...)]</c>.
        /// </summary>
        public string DiscriminatorName { get; }

        /// <summary>
        /// Не <c>null</c> - субъект сам является коллекцией (§9.10), и
        /// <see cref="Members"/> у него пуст, а <see cref="Parameters"/> -
        /// тоже: конструктор без параметров обязателен, класть в него нечего.
        /// </summary>
        public CollectionShapeModel? CollectionShape { get; }

        public SubjectModel(
            string fullName,
            string methodSuffix,
            bool isRoot,
            bool isValueType,
            IReadOnlyList<MemberModel> members,
            IReadOnlyList<ParameterModel> parameters,
            IReadOnlyList<DerivedTypeModel> derived,
            string discriminatorName,
            CollectionShapeModel? collectionShape = null
            )
        {
            FullName = fullName;
            MethodSuffix = methodSuffix;
            IsRoot = isRoot;
            IsValueType = isValueType;
            Members = members;
            Parameters = parameters;
            Derived = derived;
            DiscriminatorName = discriminatorName;
            CollectionShape = collectionShape;
        }

        public bool IsPolymorphic => Derived.Count > 0;

        /// <summary>
        /// Читатель обязан сложить всё в локальные и собрать объект в конце:
        /// аргумент конструктора нельзя передать объекту, которого ещё нет.
        ///
        /// Форма эта дороже - локальная на член, плюс флаг присутствия на
        /// каждый член, который конструктору не аргумент, - и потому не
        /// навязывается никому лишнему. У типа с конструктором без параметров
        /// остаётся прямое присваивание в <c>result.X</c> в ветке диспетчера,
        /// без единой лишней переменной.
        ///
        /// Флаги присутствия не перестраховка: инициализатор члена
        /// <b>переживает</b> отсутствие имени в документе - проверено прогоном
        /// эталона, - а значит просто присвоить прочитанное нельзя, иначе
        /// <c>Beta = 5</c> превратится в ноль на документе, в котором
        /// <c>Beta</c> не было.
        /// </summary>
        public bool NeedsDeferredConstruction => Parameters.Count > 0;

        /// <summary>Тип, каким он печатается в сигнатуру точки входа и писателя.</summary>
        public string Declaration => IsValueType ? FullName : FullName + "?";
    }

    public sealed class HostModel
    {
        public string? Namespace { get; }

        public string TypeName { get; }

        /// <summary>Имя выходного файла строится по нему: два хоста с одинаковым простым именем в разных namespace не должны столкнуться.</summary>
        public string FullName { get; }

        /// <summary>
        /// Типы sink'ов, под которые печатаются перегрузки. Пусто - значит
        /// печатается одна перегрузка под <c>ExhausterBase</c>/<c>InjectorBase</c>.
        /// </summary>
        public IReadOnlyList<string> ExhausterTypes { get; }

        public IReadOnlyList<string> InjectorTypes { get; }

        public IReadOnlyList<SubjectModel> Subjects { get; }

        /// <summary>
        /// Различные коллекции, встретившиеся в членах - включая вложенные.
        /// И чтение, и запись вынесены в метод (§16.2 плана: на графе из
        /// двухсот типов один и тот же <c>Dictionary&lt;string,int&gt;</c>
        /// печатался двести раз подряд) - иначе вложенность пришлось бы ещё и
        /// разворачивать в цикл внутри цикла прямо в ветке диспетчера.
        ///
        /// С фазы 6 сюда же попадают и коллекционные интерфейсы на месте
        /// члена (<c>IList&lt;T&gt;</c>, <c>IReadOnlyDictionary&lt;string,
        /// V&gt;</c> и так далее, §9.10) - каждый со своим суффиксом, потому
        /// что объявленный тип у них другой, хотя тело читателя может
        /// совпадать с <c>List&lt;T&gt;</c>/<c>Dictionary&lt;string,V&gt;</c>
        /// дословно.
        /// </summary>
        public IReadOnlyList<ValueModel> Collections { get; }

        /// <summary>
        /// Enum'ы, встретившиеся в строковом режиме. Числовой режим методов не
        /// требует: он читается читателем подлежащего типа и приводится к
        /// enum'у по месту.
        /// </summary>
        public IReadOnlyList<EnumModel> StringEnums { get; }

        /// <summary>
        /// Различные скаляры, встретившиеся в членах, - пара «вид плюс
        /// nullability».
        ///
        /// Чтение скаляра не зависит ни от имени члена, ни от типа, которому
        /// он принадлежит, поэтому <c>int</c> всего хоста читается одним
        /// методом. Печаталось это по месту и стоило три строки на член -
        /// девять, если член nullable, - и на графе из двухсот типов
        /// повторялось тысячами (§16.2 плана). Запись так не выносится: у неё
        /// на скаляр приходится одна строка, и выносить нечего.
        /// </summary>
        public IReadOnlyList<ValueModel> Scalars { get; }

        /// <summary>
        /// Политика для ключей словарей. Имена членов преобразуются на
        /// компиляции и приезжают сюда уже готовыми; ключ - не константа, и его
        /// приходится преобразовывать на записи, поэтому политика доживает до
        /// эмиттера.
        /// </summary>
        public JsonNamingStyle DictionaryKeyNaming { get; }

        /// <summary>
        /// Стражи, включённые <c>[JsonGuard]</c> на хосте (§6.3 плана).
        /// <c>JsonGuard.None</c> - хост не упомянул атрибут вовсе, и это
        /// главный инвариант всей конструкции: текст порождаемого кода тогда
        /// обязан остаться ровно таким же, как до JsonGuard, - ни одна ветка
        /// эмиттера не смотрит на <see cref="Guards"/>, если он <c>None</c>.
        /// </summary>
        public JsonGuard Guards { get; }

        /// <summary>
        /// Предел вложенности для <see cref="JsonGuard.MaxDepth"/>. Значим,
        /// только если бит установлен; 64 - умолчание на случай, если его нет
        /// (тот же смысл, что у <c>JsonGuardAttribute.MaxDepth</c> в рантайме).
        /// </summary>
        public int MaxDepth { get; }

        /// <summary>
        /// Фичи, включённые <c>[JsonFeature]</c> на хосте (§6.2 плана).
        /// <c>JsonFeature.None</c> - хост не упомянул атрибут вовсе, и это
        /// главный инвариант всей конструкции: текст порождаемого кода тогда
        /// обязан остаться ровно таким же, как до <c>JsonFeature</c>.
        /// </summary>
        public JsonFeature Features { get; }

        public HostModel(
            string? ns,
            string typeName,
            string fullName,
            IReadOnlyList<string> exhausterTypes,
            IReadOnlyList<string> injectorTypes,
            IReadOnlyList<SubjectModel> subjects,
            IReadOnlyList<ValueModel> collections,
            IReadOnlyList<EnumModel> stringEnums,
            IReadOnlyList<ValueModel> scalars,
            JsonNamingStyle dictionaryKeyNaming,
            JsonGuard guards = JsonGuard.None,
            int maxDepth = 64,
            JsonFeature features = JsonFeature.None
            )
        {
            Scalars = scalars;
            StringEnums = stringEnums;
            DictionaryKeyNaming = dictionaryKeyNaming;
            Namespace = ns;
            TypeName = typeName;
            FullName = fullName;
            ExhausterTypes = exhausterTypes;
            InjectorTypes = injectorTypes;
            Subjects = subjects;
            Collections = collections;
            Guards = guards;
            MaxDepth = maxDepth;
            Features = features;
        }
    }
}
