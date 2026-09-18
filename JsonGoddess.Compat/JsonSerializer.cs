using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JsonGoddess.Compat
{
    /// <summary>
    /// Drop-in для <c>System.Text.Json.JsonSerializer</c> (§10, маршрут A).
    /// Подменяется одной строкой на всю сборку:
    ///
    /// <code>
    /// global using JsonSerializer = JsonGoddess.Compat.JsonSerializer;
    /// </code>
    ///
    /// <para>
    /// <b>Устройство.</b> Каждая перегрузка сначала смотрит, есть ли для
    /// <c>TValue</c> быстрый путь (<see cref="CompatBinding{T}"/>, заполняется
    /// <c>[ModuleInitializer]</c>'ом порождённого кода) и означают ли переданные
    /// опции то же, что их отсутствие (<see cref="CompatOptions"/>). Если хоть
    /// один ответ «нет» - работа уходит настоящему
    /// <c>System.Text.Json.JsonSerializer</c> и продолжает работать, медленнее,
    /// но правильно. Это не запасной выход на случай ошибки, а объявленное
    /// поведение: обслужить всё мы не обещали, а притвориться, что обслужили, -
    /// худший из исходов.
    /// </para>
    ///
    /// <para>
    /// <b>Чего здесь пока нет.</b> Эталон объявляет 103 публичных статических
    /// метода; повторены не все. Не повторённая перегрузка - это ошибка
    /// компиляции у потребителя, то есть громкий отказ, а не тихое расхождение;
    /// список и порядок дописывания - в PLAN.md §10.
    /// </para>
    ///
    /// <para>
    /// <b>Async отдан эталону целиком, и намеренно.</b> Быстрый путь работает
    /// по документу в памяти; чтобы подставить его под <c>Stream</c>-асинхрон,
    /// поток пришлось бы вычитывать в буфер целиком - то есть менять профиль
    /// памяти у того, кто выбрал async ровно чтобы этого не делать. Нативный
    /// путь синхронен, так сказано в §10, и здесь это видно в коде.
    /// </para>
    /// </summary>
    public static class JsonSerializer
    {
        private static bool CanServe<TValue>(JsonSerializerOptions? options)
        {
            return CompatBinding<TValue>.IsBound && CompatOptions.IsDefault(options);
        }

        // ---------- запись ----------

        public static string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            if (CanServe<TValue>(options))
            {
                var utf8 = CompatBinding<TValue>.ToUtf8Bytes!(value);
                return Encoding.UTF8.GetString(utf8, 0, utf8.Length);
            }

            return global::System.Text.Json.JsonSerializer.Serialize(value, options);
        }

        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            if (CanServe<TValue>(options))
            {
                return CompatBinding<TValue>.ToUtf8Bytes!(value);
            }

            return global::System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, options);
        }

        public static void Serialize<TValue>(Stream utf8Json, TValue value, JsonSerializerOptions? options = null)
        {
            if (utf8Json is null)
            {
                throw new ArgumentNullException(nameof(utf8Json));
            }

            if (CanServe<TValue>(options))
            {
                var utf8 = CompatBinding<TValue>.ToUtf8Bytes!(value);
                utf8Json.Write(utf8, 0, utf8.Length);
                return;
            }

            global::System.Text.Json.JsonSerializer.Serialize(utf8Json, value, options);
        }

        public static void Serialize<TValue>(
            Utf8JsonWriter writer, TValue value, JsonSerializerOptions? options = null
            )
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (CanServe<TValue>(options))
            {
                //WriteRawValue, а не побайтовая перекладка: писатель эталона
                //здесь нужен только чтобы соблюсти его же учёт запятых и
                //отступов, а сам документ у нас уже готов
                writer.WriteRawValue(CompatBinding<TValue>.ToUtf8Bytes!(value), skipInputValidation: true);
                return;
            }

            global::System.Text.Json.JsonSerializer.Serialize(writer, value, options);
        }

        // ---------- чтение ----------

        public static TValue? Deserialize<TValue>(string json, JsonSerializerOptions? options = null)
        {
            if (json is null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (CanServe<TValue>(options))
            {
                return CompatBinding<TValue>.FromUtf8!(Encoding.UTF8.GetBytes(json));
            }

            return global::System.Text.Json.JsonSerializer.Deserialize<TValue>(json, options);
        }

        public static TValue? Deserialize<TValue>(
            ReadOnlySpan<byte> utf8Json, JsonSerializerOptions? options = null
            )
        {
            if (CanServe<TValue>(options))
            {
                return CompatBinding<TValue>.FromUtf8!(utf8Json);
            }

            return global::System.Text.Json.JsonSerializer.Deserialize<TValue>(utf8Json, options);
        }

        public static TValue? Deserialize<TValue>(
            ReadOnlySpan<char> json, JsonSerializerOptions? options = null
            )
        {
            if (CanServe<TValue>(options))
            {
                return CompatBinding<TValue>.FromUtf8!(Transcode(json));
            }

            return global::System.Text.Json.JsonSerializer.Deserialize<TValue>(json, options);
        }

        public static TValue? Deserialize<TValue>(Stream utf8Json, JsonSerializerOptions? options = null)
        {
            if (utf8Json is null)
            {
                throw new ArgumentNullException(nameof(utf8Json));
            }

            if (CanServe<TValue>(options))
            {
                //поток вычитывается целиком: быстрый путь работает по документу
                //в памяти. Тому, кому важен профиль памяти на большом потоке,
                //нужен не этот метод, а async-перегрузка - она уходит эталону
                return CompatBinding<TValue>.FromUtf8!(ReadToEnd(utf8Json));
            }

            return global::System.Text.Json.JsonSerializer.Deserialize<TValue>(utf8Json, options);
        }

        // ---------- async: целиком эталону ----------

        public static Task SerializeAsync<TValue>(
            Stream utf8Json,
            TValue value,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            )
        {
            return global::System.Text.Json.JsonSerializer.SerializeAsync(
                utf8Json, value, options, cancellationToken
                );
        }

        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            Stream utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            )
        {
            return global::System.Text.Json.JsonSerializer.DeserializeAsync<TValue>(
                utf8Json, options, cancellationToken
                );
        }

        // ---------- вспомогательное ----------

        /// <summary>
        /// UTF-16 в UTF-8. Через <c>ToArray()</c>, а не span-перегрузкой: этот
        /// путь и так уже потерял всё, ради чего экономят - документ пришёл
        /// шестнадцатибитным, то есть кто-то до нас уже раскодировал байты в
        /// строку.
        /// </summary>
        private static byte[] Transcode(ReadOnlySpan<char> json)
        {
            return Encoding.UTF8.GetBytes(json.ToArray());
        }

        private static byte[] ReadToEnd(Stream stream)
        {
            if (stream is MemoryStream memory && memory.TryGetBuffer(out var segment))
            {
                //у MemoryStream внутренний массив уже есть; копировать его,
                //чтобы тут же прочитать, было бы чистой платой ни за что
                if (segment.Offset == 0 && segment.Count == segment.Array!.Length)
                {
                    return segment.Array;
                }
            }

            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
