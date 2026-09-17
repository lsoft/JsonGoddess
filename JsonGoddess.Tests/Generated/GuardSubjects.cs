namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Субъект для ladder-фикстур <c>JsonGuard</c> (§6.3 плана, §11 - "тот же
    /// документ идёт хосту без атрибута и хосту ровно с одним флагом").
    /// Он один на все стражи, кроме <see cref="Node"/>: составные типы,
    /// имена, числа и строки нужны всем стражам сразу, а вот вложенность,
    /// нужная только <c>MaxDepth</c>, - типу, который умеет рекурсию.
    /// </summary>
    public class GuardSubject
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>Хост без единого стража - "как сейчас".</summary>
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class PlainGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.DuplicateProperties)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class DuplicatePropertiesGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.TrailingContent)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class TrailingContentGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.ControlCharsInStrings)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class ControlCharsGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.StrictNumbers)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class StrictNumbersGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.InvalidUtf8)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class InvalidUtf8GuardSerializer
    {
    }

    [JsonGuard(JsonGuard.UnknownProperties)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class UnknownPropertiesGuardSerializer
    {
    }

    [JsonGuard(JsonGuard.SystemTextJsonCompatible)]
    [JsonSubject(typeof(GuardSubject), true)]
    public partial class CompatibleGuardSerializer
    {
    }

    /// <summary>
    /// Рекурсивный тип для <c>MaxDepth</c>: только он способен проверить
    /// счётчик глубины на <b>известной</b> цепочке вложенности, а не только
    /// на пропуске чужого поддерева.
    /// </summary>
    public class GuardChainNode
    {
        public int Value { get; set; }

        public GuardChainNode? Child { get; set; }
    }

    [JsonSubject(typeof(GuardChainNode), true)]
    public partial class PlainNodeSerializer
    {
    }

    [JsonGuard(JsonGuard.MaxDepth, MaxDepth = 3)]
    [JsonSubject(typeof(GuardChainNode), true)]
    public partial class MaxDepthNodeSerializer
    {
    }
}
