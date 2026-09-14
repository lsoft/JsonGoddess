using System.Text.Encodings.Web;
using System.Text.Json;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// Эталон. Нигде в тестах ожидание не записывается литералом, если его
    /// может назвать BCL: литерал фиксирует то, что я думаю про
    /// <c>System.Text.Json</c>, а прогон - то, что он делает.
    /// </summary>
    public static class Reference
    {
        /// <summary>
        /// Опции, в которых набор экранируемого совпадает с нашим - минимум
        /// RFC 8259 §7. Энкодер по умолчанию у STJ дополнительно экранирует
        /// HTML-значимые символы и весь не-ASCII; это осознанное расхождение,
        /// и оно закреплено отдельным тестом
        /// (<see cref="EscapingDivergenceFixture"/>), а не спрятано здесь.
        /// </summary>
        public static readonly JsonSerializerOptions Relaxed = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Опции по умолчанию - то, что получит человек, написавший
        /// <c>JsonSerializer.Serialize(x)</c> без аргументов.
        /// </summary>
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions();

        public static string Write<T>(T value)
        {
            return JsonSerializer.Serialize(value, Relaxed);
        }

        public static string WriteDefaultEncoder<T>(T value)
        {
            return JsonSerializer.Serialize(value, Default);
        }
    }
}
