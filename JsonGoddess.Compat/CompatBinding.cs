using System;

namespace JsonGoddess.Compat
{
    /// <summary>
    /// Читатель UTF-8-документа в объект. Отдельный делегат, а не
    /// <c>Func&lt;ReadOnlySpan&lt;byte&gt;, T&gt;</c>, потому что
    /// <see cref="ReadOnlySpan{T}"/> - ref struct, и аргументом типа для
    /// <c>Func</c> он быть не может.
    /// </summary>
    public delegate T Utf8Reader<out T>(ReadOnlySpan<byte> utf8);

    /// <summary>
    /// Где фасад находит быстрый путь для типа <typeparamref name="T"/>.
    ///
    /// <para>
    /// Статическое поле на <b>закрытом</b> генерике, а не запись в словаре
    /// <c>Type -&gt; делегат</c>, и это принципиально: обращение к
    /// <c>CompatBinding&lt;Order&gt;.FromUtf8</c> JIT превращает в одну
    /// загрузку по фиксированному адресу, тогда как словарь стоил бы хеша,
    /// сравнения и промаха кеша - на каждый вызов, то есть ровно на горячем
    /// пути, ради которого весь проект и затевался.
    /// </para>
    ///
    /// <para>
    /// Заполняется <c>[ModuleInitializer]</c>'ом сборки-потребителя: генератор
    /// печатает регистрацию для каждого типа, который сумел обслужить. Пусто -
    /// значит этот тип уходит настоящему <c>System.Text.Json</c>, и это не
    /// ошибка, а объявленное поведение (§10, диагностика <c>JGD001</c>).
    /// </para>
    ///
    /// <para>
    /// Поля, а не свойства, и без блокировок: запись бывает один раз, в
    /// инициализаторе модуля, до первого обращения пользовательского кода.
    /// </para>
    /// </summary>
    public static class CompatBinding<T>
    {
        /// <summary>Записать значение в новый массив UTF-8-байт.</summary>
        public static Func<T, byte[]>? ToUtf8Bytes;

        /// <summary>Прочитать значение из UTF-8-документа.</summary>
        public static Utf8Reader<T>? FromUtf8;

        /// <summary>
        /// Обе стороны на месте. Одной мало: тип, который умеем только писать,
        /// на чтении молча ушёл бы эталону, и разницу в документах никто бы не
        /// заметил.
        /// </summary>
        public static bool IsBound => ToUtf8Bytes is not null && FromUtf8 is not null;

        /// <summary>
        /// Зарегистрировать быстрый путь. Зовётся порождённым кодом из
        /// <c>[ModuleInitializer]</c>; людям звать её незачем, но и прятать
        /// незачем - тесты фасада регистрируются именно так, руками, и это
        /// единственный способ проверить фасад отдельно от генератора.
        /// </summary>
        public static void Register(Func<T, byte[]> toUtf8Bytes, Utf8Reader<T> fromUtf8)
        {
            ToUtf8Bytes = toUtf8Bytes ?? throw new ArgumentNullException(nameof(toUtf8Bytes));
            FromUtf8 = fromUtf8 ?? throw new ArgumentNullException(nameof(fromUtf8));
        }

        /// <summary>
        /// Снять регистрацию. Нужна тестам: без неё один тест, зарегистрировавший
        /// тип, менял бы поведение всех последующих - а порядок тестов не
        /// определён.
        /// </summary>
        public static void Clear()
        {
            ToUtf8Bytes = null;
            FromUtf8 = null;
        }
    }
}
