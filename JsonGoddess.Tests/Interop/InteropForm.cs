using System;
using System.Text;
using System.Text.Json;
using JsonGoddess.Tests.Stj;

namespace JsonGoddess.Tests.Interop
{
    /// <summary>Наш писатель для одной формы.</summary>
    public delegate void FormWriter<T>(PooledUtf8Exhauster exhauster, T? value);

    /// <summary>
    /// Наш читатель для одной формы. Собственный тип делегата, а не
    /// <c>Func&lt;&gt;</c>: параметром стоит <c>ReadOnlySpan&lt;byte&gt;</c>, и
    /// аргументом обобщённого типа он быть не может - а параметром делегата
    /// может.
    /// </summary>
    public delegate T? FormReader<T>(ReadOnlySpan<byte> json);

    /// <summary>
    /// Одна форма дифференциального харнесса: пара «тип + образец».
    ///
    /// Форма - это именно пара, а не тип: пустая коллекция и заполненная дают
    /// документы разного строения при одном и том же POCO, и проверять надо
    /// оба. В отчёте отдельно сказано, сколько здесь различных типов, чтобы
    /// число форм нельзя было принять за число типов.
    ///
    /// Наружу форма выставляет только строки, и каждая из них - либо наш
    /// документ, либо документ эталона. Ожидание в харнессе не записывается
    /// литералом нигде и никогда: его всегда называет
    /// <c>System.Text.Json</c> внутри теста.
    /// </summary>
    public abstract class InteropForm
    {
        public abstract string Name { get; }

        /// <summary>Что именно эта форма проверяет - одной строкой, для отчёта.</summary>
        public abstract string What { get; }

        /// <summary>
        /// Причина, по которой документы обязаны <b>разойтись</b> - или
        /// <c>null</c>, если они обязаны совпасть.
        ///
        /// Осознанное расхождение закрепляется так же строго, как совпадение:
        /// харнесс требует, чтобы документы действительно различались. Иначе
        /// отмена решения прошла бы незамеченной - а решение о том, что мы
        /// пишем не то же, что эталон, - последнее, что можно отменять молча.
        /// </summary>
        public abstract string? Divergence { get; }

        /// <summary>Наш писатель на образце.</summary>
        public abstract string WriteOurs();

        /// <summary>Эталон на образце.</summary>
        public abstract string WriteTheirs();

        /// <summary>Эталон в режиме по умолчанию: не-ASCII уезжает в <c>\uXXXX</c>.</summary>
        public abstract string WriteTheirsEscaped();

        /// <summary>Эталон читает документ и пишет прочитанное обратно.</summary>
        public abstract string TheirsReadsThenWrites(string json);

        /// <summary>Читаем мы, пишет эталон: так выглядит «мы поняли документ так же».</summary>
        public abstract string OursReadsThenTheirsWrites(string json);

        public override string ToString() => Name;
    }

    public sealed class InteropForm<T> : InteropForm
        where T : class
    {
        private readonly T _sample;
        private readonly FormWriter<T> _write;
        private readonly FormReader<T> _read;

        public InteropForm(
            string name,
            string what,
            T sample,
            FormWriter<T> write,
            FormReader<T> read,
            string? divergence
            )
        {
            Name = name;
            What = what;
            Divergence = divergence;
            _sample = sample;
            _write = write;
            _read = read;
        }

        public override string Name { get; }

        public override string What { get; }

        public override string? Divergence { get; }

        public override string WriteOurs()
        {
            using var exhauster = new PooledUtf8Exhauster();
            _write(exhauster, _sample);
            return Encoding.UTF8.GetString(exhauster.ToArray());
        }

        public override string WriteTheirs() => Reference.Write(_sample);

        public override string WriteTheirsEscaped() => Reference.WriteDefaultEncoder(_sample);

        public override string TheirsReadsThenWrites(string json)
        {
            return Reference.Write(JsonSerializer.Deserialize<T>(json, Reference.Relaxed));
        }

        public override string OursReadsThenTheirsWrites(string json)
        {
            return Reference.Write(_read(Encoding.UTF8.GetBytes(json)));
        }
    }
}
