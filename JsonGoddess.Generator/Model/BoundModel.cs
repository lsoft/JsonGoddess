using System.Collections.Generic;
using JsonGoddess.Generator.Shared;

namespace JsonGoddess.Generator.Model
{
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
        List,
        Array,

        /// <summary><c>Dictionary&lt;string, V&gt;</c>: объект, имена свойств которого неизвестны на этапе компиляции.</summary>
        Dictionary,

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
        /// Имя типа, каким оно печатается в объявление локальной переменной и в
        /// <c>new</c>. Без <c>?</c>: nullability приписывается по
        /// <see cref="IsNullable"/>.
        /// </summary>
        public string TypeName { get; }

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

        public ValueModel(
            ValueForm form,
            BuiltinKind builtin,
            string typeName,
            string methodSuffix,
            ValueModel? element,
            bool isNullable,
            EnumModel? enumModel = null,
            bool isStringEnum = false
            )
        {
            Form = form;
            Builtin = builtin;
            TypeName = typeName;
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
            Form == ValueForm.List || Form == ValueForm.Array || Form == ValueForm.Dictionary;
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

        public MemberModel(
            string memberName,
            string jsonName,
            byte[] jsonNameUtf8,
            ValueModel value,
            bool canWrite,
            bool canRead,
            WriteCondition condition,
            int order
            )
        {
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

    public sealed class SubjectModel
    {
        /// <summary>Полное имя с <c>global::</c>: печатается в код как есть.</summary>
        public string FullName { get; }

        /// <summary>Суффикс имён порождаемых методов: <c>Read_Ns_Order</c>.</summary>
        public string MethodSuffix { get; }

        public bool IsRoot { get; }

        public IReadOnlyList<MemberModel> Members { get; }

        public SubjectModel(string fullName, string methodSuffix, bool isRoot, IReadOnlyList<MemberModel> members)
        {
            FullName = fullName;
            MethodSuffix = methodSuffix;
            IsRoot = isRoot;
            Members = members;
        }
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
        /// Чтение коллекции вынесено в метод, потому что иначе вложенность
        /// пришлось бы разворачивать в цикл внутри цикла прямо в ветке
        /// диспетчера; запись, наоборот, печатается по месту.
        /// </summary>
        public IReadOnlyList<ValueModel> Collections { get; }

        /// <summary>
        /// Enum'ы, встретившиеся в строковом режиме. Числовой режим методов не
        /// требует: он печатается по месту одной строкой.
        /// </summary>
        public IReadOnlyList<EnumModel> StringEnums { get; }

        /// <summary>
        /// Политика для ключей словарей. Имена членов преобразуются на
        /// компиляции и приезжают сюда уже готовыми; ключ - не константа, и его
        /// приходится преобразовывать на записи, поэтому политика доживает до
        /// эмиттера.
        /// </summary>
        public JsonNamingStyle DictionaryKeyNaming { get; }

        public HostModel(
            string? ns,
            string typeName,
            string fullName,
            IReadOnlyList<string> exhausterTypes,
            IReadOnlyList<string> injectorTypes,
            IReadOnlyList<SubjectModel> subjects,
            IReadOnlyList<ValueModel> collections,
            IReadOnlyList<EnumModel> stringEnums,
            JsonNamingStyle dictionaryKeyNaming
            )
        {
            StringEnums = stringEnums;
            DictionaryKeyNaming = dictionaryKeyNaming;
            Namespace = ns;
            TypeName = typeName;
            FullName = fullName;
            ExhausterTypes = exhausterTypes;
            InjectorTypes = injectorTypes;
            Subjects = subjects;
            Collections = collections;
        }
    }
}
