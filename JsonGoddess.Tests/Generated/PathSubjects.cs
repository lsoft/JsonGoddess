using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JsonGoddess.Tests.Generated
{
    /// <summary>
    /// Граф для дифференциальных проверок пути в исключении (§6.4): объект в
    /// массиве в объекте - минимум, на котором путь вообще бывает длиннее
    /// корня, плюс третий уровень вложенности, чтобы сегменты нельзя было
    /// угадать перестановкой двух.
    /// </summary>
    public class PathRoot
    {
        public List<PathOrder>? Orders
        {
            get;
            set;
        }

        public PathItem? Head
        {
            get;
            set;
        }
    }

    public class PathOrder
    {
        public int Id
        {
            get;
            set;
        }

        public string? Name
        {
            get;
            set;
        }

        public List<PathItem>? Items
        {
            get;
            set;
        }
    }

    public class PathItem
    {
        public int Qty
        {
            get;
            set;
        }

        public PathInner? Inner
        {
            get;
            set;
        }
    }

    public class PathInner
    {
        public int Deep
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Набор стражей выбран так, чтобы отказы совпадали с отказами эталона на
    /// опциях по умолчанию: всё из <c>SystemTextJsonCompatible</c>, <b>кроме</b>
    /// <c>UnknownProperties</c> и <c>DuplicateProperties</c>. Эти два эталон по
    /// умолчанию не включает, и с ними документы расходились бы не путём, а
    /// самим фактом отказа - то есть тест перестал бы проверять то, ради чего
    /// он написан.
    /// </summary>
    [JsonGuard(
        JsonGuard.TrailingContent
        | JsonGuard.ControlCharsInStrings
        | JsonGuard.StrictNumbers
        | JsonGuard.InvalidUtf8
        | JsonGuard.MaxDepth
        )]
    [JsonSubject(typeof(PathRoot), true)]
    [JsonSubject(typeof(PathOrder), false)]
    [JsonSubject(typeof(PathItem), false)]
    [JsonSubject(typeof(PathInner), false)]
    public partial class PathRootSerializer
    {
    }

    /// <summary>Тот же граф у хоста без стражей - путь ему не строится.</summary>
    [JsonSubject(typeof(PathRoot), true)]
    [JsonSubject(typeof(PathOrder), false)]
    [JsonSubject(typeof(PathItem), false)]
    [JsonSubject(typeof(PathInner), false)]
    public partial class PathRootPlainSerializer
    {
    }

    /// <summary>
    /// Имена, из-за которых сегмент пути берётся в скобки, и соседние с ними
    /// безобидные: набор особых символов установлен пробой, и проверяется он
    /// тоже прогоном эталона.
    /// </summary>
    public class PathOdd
    {
        [JsonPropertyName("na.me")]
        public int Dotted
        {
            get;
            set;
        }

        [JsonPropertyName("has space")]
        public int Spaced
        {
            get;
            set;
        }

        [JsonPropertyName("has'quote")]
        public int Quoted
        {
            get;
            set;
        }

        [JsonPropertyName("plain_name-1")]
        public int Plain
        {
            get;
            set;
        }

        [JsonPropertyName("имя")]
        public int Cyrillic
        {
            get;
            set;
        }
    }

    [JsonGuard(JsonGuard.TrailingContent)]
    [JsonSubject(typeof(PathOdd), true)]
    public partial class PathOddSerializer
    {
    }

    /// <summary>
    /// Обязательные члены в графе: отказ приходится не на лексему, а на
    /// объект целиком, и якорь у него свой (<c>EnclosingObject</c>).
    /// </summary>
    public class PathDemandingRoot
    {
        public List<PathDemanding>? Orders
        {
            get;
            set;
        }
    }

    public class PathDemanding
    {
        public required int Id
        {
            get;
            set;
        }

        public string? Name
        {
            get;
            set;
        }
    }

    [JsonGuard(JsonGuard.TrailingContent)]
    [JsonSubject(typeof(PathDemandingRoot), true)]
    [JsonSubject(typeof(PathDemanding), false)]
    public partial class PathDemandingSerializer
    {
    }
}
