using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Generated
{
    /// <summary>
    /// LINK на настоящем генераторе. Рядом лежит <see cref="LinkSerializer"/>
    /// с четырьмя рукописными формами: две пары, и внутри каждой пары обе
    /// половины совпадают байт в байт. Пары - это контроль раскладки (§12.5
    /// плана), порождённый хост - пятая точка, показывающая, где в этом ряду
    /// стоит то, что печатает эмиттер сегодня.
    /// </summary>
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Link), true)]
    public partial class LinkGenerated
    {
    }
}
