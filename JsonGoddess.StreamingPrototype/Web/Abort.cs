using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JsonGoddess.StreamingPrototype.Web
{
    /// <summary>
    /// Тело, которое доезжает наполовину и дальше не едет никогда.
    ///
    /// <para>
    /// Нужно ради того, чего не проверяет ни один другой тест: драйвер ждёт
    /// добавки в цикле <c>ReadAsync</c>, и ушедший клиент - единственный
    /// случай, когда добавки не будет. Вести себя это обязано так же, как у
    /// штатного форматтера: запрос завершается отменой, а приложение живо.
    /// </para>
    ///
    /// <para>
    /// Бросить <see cref="IOException"/> из середины записи не годится: под
    /// <c>TestServer</c> так виснут оба - и мы, и эталон, - потому что обрыв
    /// внутрипроцессной трубы этим не выражается. Поэтому здесь запись просто
    /// не заканчивается, а клиент отменяет запрос своим токеном - то самое,
    /// что в жизни делает ушедший клиент и что доходит до
    /// <c>HttpContext.RequestAborted</c>.
    /// </para>
    /// </summary>
    internal sealed class Abort : HttpContent
    {
        private readonly byte[] _body;

        private readonly CancellationToken _token;

        internal Abort(byte[] body, CancellationToken token)
        {
            _body = body;
            _token = token;

            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync(_body.AsMemory(0, _body.Length / 2), _token);
            await stream.FlushAsync(_token);

            await Task.Delay(Timeout.Infinite, _token);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
