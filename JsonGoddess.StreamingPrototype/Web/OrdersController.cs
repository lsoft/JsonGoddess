using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Mvc;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Ответ возвращает прочитанное целиком, а не число: сравнивать надо
    /// документы, а не счётчики - счётчик совпал бы и при половине
    /// потерянных полей.
    /// </summary>
    [ApiController]
    public sealed class OrdersController : ControllerBase
    {
        [HttpPost("/orders")]
        public Order[] Accept([FromBody] Order[] orders) => orders;

        [HttpPost("/count")]
        public int Count([FromBody] Order[] orders) => orders.Length;

        /// <summary>Корень - один объект, а не массив: тот самый вырожденный случай.</summary>
        [HttpPost("/one")]
        public Order AcceptOne([FromBody] Order order) => order;
    }
}
