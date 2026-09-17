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

    /// <summary>
    /// Включает строгие проверки чтения на хосте (§6.3 плана). По умолчанию
    /// читатель принимает всё, что смог разобрать - берёт первое подходящее
    /// толкование битого документа и продолжает; этот атрибут просит вместо
    /// этого отказ, там, где RFC 8259 или сам эталон (<c>System.Text.Json</c>
    /// с опциями по умолчанию) отказали бы тоже.
    ///
    /// Выключенный (не упомянутый) страж не стоит ни одной ветки в
    /// порождённом коде - это не обещание, а утверждение, проверенное тестом
    /// на текст (<c>JsonGoddess.GeneratorTests</c>). Атрибут ставится рядом с
    /// <see cref="JsonSubjectAttribute"/>, на хосте, и действует на все его
    /// субъекты разом - ровно как включение <c>JsonSerializerOptions</c> у
    /// эталона действует на весь граф типов одного сериализатора.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class JsonGuardAttribute : Attribute
    {
        public JsonGuard Guards
        {
            get;
        }

        /// <summary>
        /// Предел вложенности для <see cref="JsonGuard.MaxDepth"/>. 64 -
        /// умолчание самого эталона (<c>JsonReaderOptions.MaxDepth</c> и
        /// <c>JsonSerializerOptions.MaxDepth</c> при значении 0 разворачиваются
        /// в те же 64, проверено пробой). Значим, только если в
        /// <see cref="Guards"/> установлен бит <see cref="JsonGuard.MaxDepth"/>;
        /// без него счётчик глубины не появляется в коде вовсе.
        /// </summary>
        public int MaxDepth
        {
            get;
            set;
        } = 64;

        public JsonGuardAttribute(JsonGuard guards)
        {
            Guards = guards;
        }
    }

    /// <summary>
    /// Включает opt-in фичи чтения/записи на хосте (§6.2 плана). Зеркало
    /// <see cref="JsonGuardAttribute"/> по устройству: атрибут ставится рядом
    /// с <see cref="JsonSubjectAttribute"/>, на хосте, и действует на все его
    /// субъекты разом.
    ///
    /// Выключенная (не упомянутая) фича не стоит ни одной ветки в
    /// порождённом коде - утверждение проверено тестом на текст
    /// (<c>JsonGoddess.GeneratorTests</c>), а не обещанием в комментарии.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class JsonFeatureAttribute : Attribute
    {
        public JsonFeature Features
        {
            get;
        }

        public JsonFeatureAttribute(JsonFeature features)
        {
            Features = features;
        }
    }
}
