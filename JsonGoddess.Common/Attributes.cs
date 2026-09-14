using System;

namespace JsonGoddess
{
    /// <summary>
    /// Регистрирует тип для сериализации и десериализации на классе-хосте.
    /// Хост обязан быть <c>partial</c>: генератор дописывает в него методы.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class JsonSubjectAttribute : Attribute
    {
        public Type SubjectType
        {
            get;
        }

        /// <summary>
        /// Корневой тип: для него порождается публичная точка входа
        /// <c>Serialize</c>/<c>Deserialize</c>. Обычно ровно один на хост.
        /// </summary>
        public bool IsRoot
        {
            get;
        }

        public JsonSubjectAttribute(Type subjectType, bool isRoot)
        {
            SubjectType = subjectType ?? throw new ArgumentNullException(nameof(subjectType));
            IsRoot = isRoot;
        }
    }

    /// <summary>
    /// Регистрирует sink записи. Тип обязан наследовать
    /// <see cref="ExhausterBase"/>; реализации одного лишь
    /// <see cref="IExhauster"/> недостаточно - контракт генератора это класс,
    /// потому что в класс новый член добавляется virtual и никого не ломает.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class JsonExhausterAttribute : Attribute
    {
        public Type ExhausterType
        {
            get;
        }

        public JsonExhausterAttribute(Type exhausterType)
        {
            ExhausterType = exhausterType ?? throw new ArgumentNullException(nameof(exhausterType));
        }
    }

    /// <summary>
    /// Регистрирует sink чтения. Тип обязан наследовать
    /// <see cref="InjectorBase"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class JsonInjectorAttribute : Attribute
    {
        public Type InjectorType
        {
            get;
        }

        public JsonInjectorAttribute(Type injectorType)
        {
            InjectorType = injectorType ?? throw new ArgumentNullException(nameof(injectorType));
        }
    }

    /// <summary>
    /// Заменяет <c>new T()</c> при десериализации на произвольное выражение C#.
    /// Нужно для пулов и переиспользования уже размещённых объектов.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class JsonFactoryAttribute : Attribute
    {
        public Type SubjectType
        {
            get;
        }

        public string InvocationStatement
        {
            get;
        }

        public JsonFactoryAttribute(Type subjectType, string invocationStatement)
        {
            SubjectType = subjectType ?? throw new ArgumentNullException(nameof(subjectType));
            InvocationStatement = invocationStatement ?? throw new ArgumentNullException(nameof(invocationStatement));
        }
    }
}
