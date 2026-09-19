using System;
using System.Text.Json;

namespace JsonGoddess.Compat.Interop
{
    /// <summary>
    /// Запись значения в нашу раковину. Раковину даёт мост, а не порождённый
    /// код: пул у неё один на поток, и владеть им должен тот, кто знает про
    /// время жизни, - то есть конвертер.
    /// </summary>
    public delegate void BridgeWriter<in T>(CompatUtf8Exhauster exhauster, T value);

    /// <summary>
    /// То же для веб-профиля - и раковина другая.
    ///
    /// <para>
    /// ASP.NET Core пишет ответ <c>UnsafeRelaxedJsonEscaping</c>, а не
    /// умолчательным энкодером, и набор его задан таблицей Unicode, которую
    /// повторить нечем. <see cref="EncoderUtf8Exhauster"/> её и не повторяет -
    /// он зовёт тот самый энкодер, что лежит в опциях. Отдельный делегат, а не
    /// общий с <see cref="BridgeWriter{T}"/>, потому что раковины запечатаны
    /// обе: конкретный тип в сигнатуре - это девиртуализованный вызов в
    /// порождённом коде, и терять его ради одного делегата незачем.
    /// </para>
    /// </summary>
    public delegate void BridgeWebWriter<in T>(EncoderUtf8Exhauster exhauster, T value);

    /// <summary>
    /// Чтение значения прямо из читателя эталона.
    ///
    /// <para>
    /// <c>ref</c>, а не по значению: <see cref="Utf8JsonReader"/> - структура
    /// с позицией внутри, и копия продвинулась бы вхолостую, оставив
    /// настоящий читатель стоять на месте. Это же и причина, по которой здесь
    /// делегат, а не <c>Func</c>: ref-структуру аргументом типа не передать.
    /// </para>
    /// </summary>
    public delegate T BridgeReader<out T>(ref Utf8JsonReader reader);

    /// <summary>
    /// Где мост находит порождённый код для типа <typeparamref name="T"/>.
    ///
    /// <para>
    /// Устроено как <see cref="CompatBinding{T}"/> и по той же причине:
    /// статическое поле на закрытом генерике JIT превращает в загрузку по
    /// фиксированному адресу. Разница в том, <b>что</b> здесь лежит. У
    /// маршрута A - функции «байты в объект и обратно», потому что там мы
    /// владеем всем вызовом целиком. У моста - функции, работающие через
    /// писателя и читателя эталона, потому что там мы вызваны изнутри его
    /// конвейера и обязаны вернуть управление в нужной точке.
    /// </para>
    ///
    /// <para>
    /// Пар две, по числу профилей (<see cref="BridgeProfile"/>): имена
    /// свойств печатаются на компиляции, и один и тот же код не может
    /// обслужить и <c>Id</c>, и <c>id</c>.
    /// </para>
    /// </summary>
    public static class BridgeBinding<T>
    {
        public static BridgeWriter<T>? Write;

        public static BridgeReader<T>? Read;

        public static BridgeWebWriter<T>? WebWrite;

        public static BridgeReader<T>? WebRead;

        /// <summary>
        /// Обе стороны на месте. Одной мало ровно по той же причине, что и у
        /// маршрута A: тип, который умеем только писать, на чтении молча ушёл
        /// бы эталону.
        /// </summary>
        public static bool IsBound => Write is not null && Read is not null;

        public static bool IsBoundForTheWeb => WebWrite is not null && WebRead is not null;

        public static void Register(BridgeWriter<T> write, BridgeReader<T> read)
        {
            Write = write ?? throw new ArgumentNullException(nameof(write));
            Read = read ?? throw new ArgumentNullException(nameof(read));

            BridgeRegistry.Add<T>(BridgeProfile.Default);
        }

        public static void RegisterForTheWeb(BridgeWebWriter<T> write, BridgeReader<T> read)
        {
            WebWrite = write ?? throw new ArgumentNullException(nameof(write));
            WebRead = read ?? throw new ArgumentNullException(nameof(read));

            BridgeRegistry.Add<T>(BridgeProfile.Web);
        }

        /// <summary>
        /// Снять регистрацию. Нужна тестам: без неё один тест менял бы
        /// поведение всех последующих, а порядок их не определён.
        /// </summary>
        public static void Clear()
        {
            Write = null;
            Read = null;
            WebWrite = null;
            WebRead = null;

            BridgeRegistry.Remove(typeof(T));
        }
    }
}
