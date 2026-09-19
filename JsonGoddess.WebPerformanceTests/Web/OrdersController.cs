using JsonGoddess.PerformanceTests.Model;
using Microsoft.AspNetCore.Mvc;

namespace JsonGoddess.WebPerformanceTests.Web
{
    /// <summary>
    /// Контроллер настолько пустой, насколько это возможно: вернуть готовый
    /// объект и ничего больше.
    ///
    /// <para>
    /// Пустота здесь - не лень, а условие измеримости. Всё, что контроллер
    /// делал бы сверх этого - поход в базу, маппинг, проверка прав, - шло бы
    /// в знаменатель доли JSON и двигало ответ в одну и ту же сторону:
    /// «разницы не видно». Настоящее приложение таково, и об этом надо
    /// сказать словами, а не получить вместо числа.
    /// </para>
    /// </summary>
    [ApiController]
    public sealed class OrdersController : ControllerBase
    {
        [HttpGet("/mvc/order")]
        public Order One()
        {
            return Payload.One;
        }

        [HttpGet("/mvc/orders")]
        public Order[] Many()
        {
            return Payload.Many;
        }

        /// <summary>
        /// Обратная сторона: тело запроса <b>читается</b>.
        ///
        /// <para>
        /// Стои́т здесь потому, что чтение обслуживается другой раковиной
        /// опций - настроенной, без подменённого энкодера, - и это разные
        /// пути внутри одного приложения. Утверждать «мост работает в
        /// ASP.NET», померив только запись, значило бы проверить половину и
        /// назвать её целым.
        /// </para>
        ///
        /// <para>
        /// Ответ короткий нарочно: замер этого метода обязан мерить разбор
        /// тела, а не сериализацию ответа, иначе в одном числе смешаются обе
        /// работы и понять будет нечего.
        /// </para>
        /// </summary>
        [HttpPost("/mvc/orders")]
        public int Accept([FromBody] Order[] orders)
        {
            return orders.Length;
        }

        [HttpPost("/mvc/order")]
        public int AcceptOne([FromBody] Order order)
        {
            return order.Id;
        }
    }
}
