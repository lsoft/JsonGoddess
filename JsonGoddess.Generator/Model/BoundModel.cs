using System.Collections.Generic;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Builtin-типы, которые фаза 2 умеет. Набор равен не списку из §7.1 плана,
    /// а тому, что <b>есть у sink'ов</b>: <c>IExhauster</c> и <c>IInjector</c>
    /// - единственный источник истины о том, какую лексему кто-то умеет
    /// написать и прочитать. Тип, которого у sink'а нет, генератор обязан
    /// отвергнуть (JGD022), а не обслужить приблизительно.
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

    public sealed class MemberModel
    {
        /// <summary>Имя члена в C#: <c>value.Id</c>, <c>result.Id</c>.</summary>
        public string MemberName { get; }

        /// <summary>Имя свойства в документе. Уже с учётом [JsonPropertyName].</summary>
        public string JsonName { get; }

        /// <summary>UTF-8-байты <see cref="JsonName"/>: по ним считается и длина, и ключ.</summary>
        public byte[] JsonNameUtf8 { get; }

        public BuiltinKind Kind { get; }

        /// <summary><c>Nullable&lt;T&gt;</c> над значимым типом.</summary>
        public bool IsNullableValueType { get; }

        /// <summary>Ссылочный тип: <c>string</c> или <c>byte[]</c>.</summary>
        public bool IsReferenceType { get; }

        /// <summary>Есть публичный getter - член попадает в запись.</summary>
        public bool CanWrite { get; }

        /// <summary>Есть публичный setter - член попадает в чтение.</summary>
        public bool CanRead { get; }

        public MemberModel(
            string memberName,
            string jsonName,
            byte[] jsonNameUtf8,
            BuiltinKind kind,
            bool isNullableValueType,
            bool isReferenceType,
            bool canWrite,
            bool canRead
            )
        {
            MemberName = memberName;
            JsonName = jsonName;
            JsonNameUtf8 = jsonNameUtf8;
            Kind = kind;
            IsNullableValueType = isNullableValueType;
            IsReferenceType = isReferenceType;
            CanWrite = canWrite;
            CanRead = canRead;
        }

        /// <summary>
        /// Может ли на месте значения стоять <c>null</c>. От этого зависит,
        /// нужна ли в чтении обёртка <c>TryReadNull</c>: для значимого
        /// не-nullable члена её нет, и это не экономия ветки, а отказ принять
        /// документ, которого тип описать не может.
        /// </summary>
        public bool IsNullable => IsNullableValueType || IsReferenceType;
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

        public HostModel(
            string? ns,
            string typeName,
            string fullName,
            IReadOnlyList<string> exhausterTypes,
            IReadOnlyList<string> injectorTypes,
            IReadOnlyList<SubjectModel> subjects
            )
        {
            Namespace = ns;
            TypeName = typeName;
            FullName = fullName;
            ExhausterTypes = exhausterTypes;
            InjectorTypes = injectorTypes;
            Subjects = subjects;
        }
    }
}
