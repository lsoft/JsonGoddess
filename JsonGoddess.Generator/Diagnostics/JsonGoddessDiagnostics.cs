using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Diagnostics
{
    /// <summary>
    /// Идентификаторы диагностик - часть публичного контракта: по ним пишут
    /// подавления в чужих проектах, и разъезжаться им нельзя. Поэтому номера
    /// назначены раз и навсегда, с пропусками под то, что появится позже:
    /// <c>JGD001</c>/<c>JGD002</c> зарезервированы за Compat-слоем (§10 плана),
    /// <c>JGD01x</c> - за регистрацией sink'ов, <c>JGD02x</c> - за связыванием
    /// типов. Переиспользовать освободившийся номер нельзя никогда.
    /// </summary>
    public static class JsonGoddessDiagnostics
    {
        public const string Category = "JsonGoddess";

        public const string CompatTypeFallsBackId = "JGD001";
        public const string CompatGenerationFailedId = "JGD002";
        public const string CompatStrictValueIsNotRecognizedId = "JGD003";
        public const string CompatWebProfileFailedId = "JGD004";
        public const string StreamingTypeIsNotServedId = "JGD005";
        public const string BridgeTypeIsNotServedId = "JGD006";
        public const string SinkIsNotSealedId = "JGD010";
        public const string HostIsNotPartialId = "JGD020";
        public const string SubjectIsNotSupportedId = "JGD021";
        public const string MemberTypeIsNotSupportedId = "JGD022";
        public const string DuplicateJsonNameId = "JGD023";
        public const string SinkTypeIsWrongId = "JGD024";
        public const string HostShapeIsNotSupportedId = "JGD025";
        public const string LanguageVersionIsTooLowId = "JGD026";
        public const string JsonNameRequiresEscapingId = "JGD027";
        public const string SerializationOptionIsNotSupportedId = "JGD028";
        public const string InvalidMaxDepthId = "JGD029";
        public const string CaseInsensitiveNameIsNotSupportedId = "JGD030";
        public const string InvalidFactoryId = "JGD031";

        public static readonly DiagnosticDescriptor SinkIsNotSealed = new DiagnosticDescriptor(
            SinkIsNotSealedId,
            "Sink type is not sealed",
            "Type '{0}' is registered as a JsonGoddess sink but is not sealed, so calls to it cannot be devirtualized; seal it to get the benefit of registering a concrete sink type",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Регистрация конкретного типа sink'а имеет ровно один смысл - дать JIT'у "
                + "девиртуализовать вызовы. У несоставного (не sealed) типа этого не происходит, "
                + "и перегрузка получается такой же, как под базовым классом, только объёмнее."
            );

        public static readonly DiagnosticDescriptor HostIsNotPartial = new DiagnosticDescriptor(
            HostIsNotPartialId,
            "JsonGoddess host must be partial",
            "Type '{0}' carries [JsonSubject] but is not declared partial; the generator has nowhere to put the generated methods",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
            );

        public static readonly DiagnosticDescriptor SubjectIsNotSupported = new DiagnosticDescriptor(
            SubjectIsNotSupportedId,
            "Subject type is not supported",
            "Type '{0}' cannot be served: {1}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Отказ целиком, а не почти правильный код: молча выданный валидный код, "
                + "дающий другой документ, - худший из возможных исходов."
            );

        public static readonly DiagnosticDescriptor MemberTypeIsNotSupported = new DiagnosticDescriptor(
            MemberTypeIsNotSupportedId,
            "Member type is not supported",
            "Member '{0}.{1}' has type '{2}', which JsonGoddess cannot serve: {3}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Пропустить член нельзя: документ без него - это другой документ. "
                + "Если член не нужен в JSON, его надо пометить [JsonIgnore]."
            );

        public static readonly DiagnosticDescriptor DuplicateJsonName = new DiagnosticDescriptor(
            DuplicateJsonNameId,
            "Duplicate JSON property name",
            "Members '{0}' and '{1}' of type '{2}' both map to JSON property name '{3}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
            );

        public static readonly DiagnosticDescriptor SinkTypeIsWrong = new DiagnosticDescriptor(
            SinkTypeIsWrongId,
            "Registered sink type has a wrong base",
            "Type '{0}' is registered as {1} but does not derive from '{2}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Контракт генератора - базовый класс, а не интерфейс: в класс новый член "
                + "добавляется virtual с рабочим телом и никого не ломает, в интерфейс - ломает всех."
            );

        public static readonly DiagnosticDescriptor HostShapeIsNotSupported = new DiagnosticDescriptor(
            HostShapeIsNotSupportedId,
            "JsonGoddess host shape is not supported",
            "Type '{0}' cannot be a JsonGoddess host: {1}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
            );

        public static readonly DiagnosticDescriptor LanguageVersionIsTooLow = new DiagnosticDescriptor(
            LanguageVersionIsTooLowId,
            "JsonGoddess requires C# 11 or later",
            "JsonGoddess generates code that uses UTF-8 string literals and the 'scoped' modifier; the project is compiled with {0}, so raise <LangVersion> to 11 or later",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Требование к версии языка, а не к таргету: netstandard2.0 и net472 остаются "
                + "поддержанными, потому что и u8-литералы, и scoped - это про компилятор, а не про рантайм."
            );

        public static readonly DiagnosticDescriptor JsonNameRequiresEscaping = new DiagnosticDescriptor(
            JsonNameRequiresEscapingId,
            "JSON property name requires escaping",
            "Member '{0}.{1}' maps to JSON property name '{2}', which contains a character that has to be escaped in JSON; JsonGoddess does not support such names",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Имя свойства сравнивается с сырыми байтами документа, до разэкранирования. "
                + "Имя, требующее escape'а, приезжало бы в двух разных видах, и совпадение "
                + "зависело бы от того, как его написал отправитель."
            );

        public static readonly DiagnosticDescriptor SerializationOptionIsNotSupported = new DiagnosticDescriptor(
            SerializationOptionIsNotSupportedId,
            "Serialization option is not supported",
            "Option {0} on '{1}' cannot be honoured: {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "JsonGoddess читает настройки из [JsonSourceGenerationOptions] - того же атрибута, "
                + "которым настраивается source-генератор System.Text.Json. Свойство, которое мы не "
                + "исполняем, обязано быть отказом, а не пропуском: иначе человек написал бы "
                + "настройку, увидел бы зелёную сборку и получил бы документ, которого не просил."
            );

        public static readonly DiagnosticDescriptor InvalidMaxDepth = new DiagnosticDescriptor(
            InvalidMaxDepthId,
            "Invalid JsonGuard.MaxDepth value",
            "JsonGuardAttribute.MaxDepth is {0} on '{1}', but the limit must be at least 1",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "MaxDepth считает вложенность как эталон (System.Text.Json): корень уже глубина 1, "
                + "поэтому предел меньше единицы не может принять ни один документ вовсе."
            );

        public static readonly DiagnosticDescriptor CaseInsensitiveNameIsNotSupported = new DiagnosticDescriptor(
            CaseInsensitiveNameIsNotSupportedId,
            "JsonFeature.CaseInsensitiveNames cannot serve this name",
            "Type '{0}' cannot serve JsonFeature.CaseInsensitiveNames for member '{1}': {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "CaseInsensitiveNames сворачивает регистр по ASCII (см. JsonAsciiName), а эталон - по "
                + "Unicode; на не-ASCII имени совпадение зависело бы от алфавита, а не от документа. "
                + "Честнее отказать на компиляции, чем обслужить документ, который эталон прочитал бы "
                + "иначе."
            );

        public static readonly DiagnosticDescriptor InvalidFactory = new DiagnosticDescriptor(
            InvalidFactoryId,
            "JsonFactory cannot be used this way",
            "[JsonFactory] on '{0}' for type '{1}' cannot be used: {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Выражение фабрики печатается в порождённый код дословно, на место new T(). "
                + "Поэтому всё, что генератор может проверить про него на компиляции, он обязан "
                + "проверить здесь: молча напечатать код, который не собирается или собирает не тот "
                + "объект, - худший исход по §1 плана."
            );

        public static readonly DiagnosticDescriptor CompatTypeFallsBack = new DiagnosticDescriptor(
            CompatTypeFallsBackId,
            "Type falls back to System.Text.Json",
            "Call to the JsonGoddess facade with type '{0}' keeps working through System.Text.Json instead of generated code: {1}",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description:
                "Фасад обслужить этот тип не смог и отдал работу эталону - документ тот же, скорость "
                + "прежняя. Это объявленное поведение, а не поломка, поэтому severity Info; "
                + "MSBuild-свойство JsonGoddessCompatStrict = warning/error поднимает громкость, "
                + "не меняя решения."
            );

        public static readonly DiagnosticDescriptor CompatGenerationFailed = new DiagnosticDescriptor(
            CompatGenerationFailedId,
            "Compat generation failed after the type graph was accepted",
            "Type '{0}' passed the JsonGoddess type-graph walk but generation failed afterwards: {1}",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Это дыра в генераторе, а не в коде пользователя: обход графа типов сказал «обслужим», "
                + "а связывание потом отказало. Работа уходит эталону и продолжается, но случай стоит "
                + "багрепорта: два наших же шага разошлись во мнении об одном и том же типе."
            );

        public static readonly DiagnosticDescriptor CompatStrictValueIsNotRecognized = new DiagnosticDescriptor(
            CompatStrictValueIsNotRecognizedId,
            "JsonGoddessCompatStrict has an unrecognized value",
            "JsonGoddessCompatStrict is set to '{0}', which JsonGoddess does not understand; expected 'info', 'warning' or 'error'. The property is ignored and JGD001/JGD002 keep their declared severity.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Молчать об опечатке нельзя: человек попросил строгости, не получил её и не узнал "
                + "об этом - а просят её ровно затем, чтобы отступление к эталону не проходило "
                + "незамеченным."
            );

        public static readonly DiagnosticDescriptor CompatWebProfileFailed = new DiagnosticDescriptor(
            CompatWebProfileFailedId,
            "The ASP.NET Core web profile was asked for but could not be generated",
            "JsonGoddessCompatWeb is enabled, but the type graph could not be bound under the ASP.NET Core web profile: {0}. Serialization under web options goes through System.Text.Json.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description:
                "Причин две. Веб-профиль включает регистронезависимый матч имён, а он отвергает "
                + "не-ASCII имя члена и пару имён, совпадающих после ASCII-свёртки (JGD030) - у эталона "
                + "свёртка по Unicode, и молча разойтись с ним здесь нельзя. Вторая: имя, которое разные "
                + "энкодеры пишут по-разному (например, с '&' или '+'), - имена печатаются константами, "
                + "а ASP.NET Core пишет ответ релаксированным энкодером, и одна константа не обслужит "
                + "оба набора. Под умолчаниями тот же граф при этом обслуживается: отказ относится "
                + "только к веб-профилю."
            );

        public static readonly DiagnosticDescriptor StreamingTypeIsNotServed = new DiagnosticDescriptor(
            StreamingTypeIsNotServedId,
            "The streaming input formatter does not serve this type",
            "'{0}' is served by JsonGoddess, but the streaming input formatter does not read it: {1}. Request bodies of this type are read by System.Text.Json as before.",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description:
                "Потоковый читатель (фаза 10) обслуживает не всякий тип: полиморфный субъект и "
                + "субъект-коллекция пока не печатаются, и неспособность заразна - тип, у которого "
                + "такой тип членом, тоже не обслуживается. Это не поломка: тело запроса такого типа "
                + "читает System.Text.Json ровно как читал, и ответ от этого не меняется. Сказано об "
                + "этом затем, чтобы ускорение, которого не случилось, не пришлось искать замером."
            );

        public static readonly DiagnosticDescriptor BridgeTypeIsNotServed = new DiagnosticDescriptor(
            BridgeTypeIsNotServedId,
            "The System.Text.Json bridge does not serve this type",
            "'{0}' is served by JsonGoddess, but the bridge does not hand it to System.Text.Json: {1}. Values of this type keep going through System.Text.Json as before.",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description:
                "Мост (§10, маршрут B) обслуживает не всякий тип: полиморфный субъект и "
                + "субъект-коллекция пока не печатаются, и неспособность заразна - тип, у которого "
                + "такой тип членом, тоже не обслуживается. Это не поломка: значение такого типа "
                + "System.Text.Json пишет и читает ровно как писал и читал. Сказано об этом затем, "
                + "чтобы ускорение, которого не случилось, не пришлось искать замером."
            );

        private static readonly Dictionary<string, DiagnosticDescriptor> _all =
            new Dictionary<string, DiagnosticDescriptor>
            {
                { SinkIsNotSealedId, SinkIsNotSealed },
                { HostIsNotPartialId, HostIsNotPartial },
                { SubjectIsNotSupportedId, SubjectIsNotSupported },
                { MemberTypeIsNotSupportedId, MemberTypeIsNotSupported },
                { DuplicateJsonNameId, DuplicateJsonName },
                { SinkTypeIsWrongId, SinkTypeIsWrong },
                { HostShapeIsNotSupportedId, HostShapeIsNotSupported },
                { LanguageVersionIsTooLowId, LanguageVersionIsTooLow },
                { JsonNameRequiresEscapingId, JsonNameRequiresEscaping },
                { SerializationOptionIsNotSupportedId, SerializationOptionIsNotSupported },
                { InvalidMaxDepthId, InvalidMaxDepth },
                { CaseInsensitiveNameIsNotSupportedId, CaseInsensitiveNameIsNotSupported },
                { InvalidFactoryId, InvalidFactory },
                { CompatTypeFallsBackId, CompatTypeFallsBack },
                { CompatGenerationFailedId, CompatGenerationFailed },
                { CompatStrictValueIsNotRecognizedId, CompatStrictValueIsNotRecognized },
                { CompatWebProfileFailedId, CompatWebProfileFailed },
                { StreamingTypeIsNotServedId, StreamingTypeIsNotServed },
                { BridgeTypeIsNotServedId, BridgeTypeIsNotServed },
            };

        public static DiagnosticDescriptor Get(string id) => _all[id];
    }
}
