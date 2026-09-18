using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

using Reference = System.Text.Json.JsonSerializer;

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
    /// <b>Устройство.</b> Перегрузка, у которой быстрый путь есть, сначала
    /// смотрит, связан ли <c>TValue</c> (<see cref="CompatBinding{T}"/>,
    /// заполняется <c>[ModuleInitializer]</c>'ом порождённого кода) и означают
    /// ли переданные опции то же, что их отсутствие
    /// (<see cref="CompatOptions"/>). Если хоть один ответ «нет» - работа
    /// уходит настоящему <c>System.Text.Json.JsonSerializer</c> и продолжает
    /// работать, медленнее, но правильно. Это не запасной выход на случай
    /// ошибки, а объявленное поведение: обслужить всё мы не обещали, а
    /// притвориться, что обслужили, - худший из исходов.
    /// </para>
    ///
    /// <para>
    /// <b>Поверхность повторена целиком</b> - все 103 публичных статических
    /// метода эталона. Спрошено у эталона рефлексией и рефлексией же
    /// проверяется (<c>SurfaceFixture</c>), потому что drop-in проверяется не
    /// тем, что документ совпал, а тем, что чужой код вообще собрался.
    /// </para>
    ///
    /// <para>
    /// <b>Одно исключение - <c>this</c>.</b> Пятнадцать методов эталона
    /// объявлены расширениями над <c>JsonDocument</c>, <c>JsonElement</c> и
    /// <c>JsonNode</c>; наши одноимённые - нет, и это решение, а не недосмотр.
    /// Псевдоним типа расширения не подменяет: они ищутся по импортированным
    /// пространствам имён. Пометь мы первый параметр <c>this</c> - у
    /// потребителя, импортировавшего и <c>System.Text.Json</c>, и
    /// <c>JsonGoddess.Compat</c>, вызов <c>document.Deserialize&lt;T&gt;()</c>
    /// стал бы неоднозначным (CS0121), то есть drop-in ломал бы ровно ту
    /// строку, которую обязан был сохранить. Без <c>this</c> эта строка
    /// связывается с эталоном - и это не потеря: все пятнадцать всё равно
    /// уходят эталону целиком, то есть делают ровно то же самое.
    /// </para>
    ///
    /// <para>
    /// <b>Быстрый путь есть далеко не у всех.</b> Он требует, чтобы тип был
    /// известен статически, - значит, перегрузки с <c>Type inputType</c>
    /// уходят эталону всегда. Перегрузки с <c>JsonTypeInfo</c> и
    /// <c>JsonSerializerContext</c> уходят эталону тоже, и это не пробел:
    /// вызывающий назвал контракт явно, и подменить его нашим значило бы
    /// сделать ровно то, чего он просил не делать.
    /// </para>
    ///
    /// <para>
    /// <b>Async отдан эталону целиком, и намеренно.</b> Быстрый путь работает
    /// по документу в памяти; чтобы подставить его под <c>Stream</c>-асинхрон,
    /// поток пришлось бы вычитывать в буфер целиком - то есть менять профиль
    /// памяти у того, кто выбрал async ровно чтобы этого не делать. Нативный
    /// путь синхронен, так сказано в §10, и здесь это видно в коде.
    /// </para>
    ///
    /// <para>
    /// <b>Атрибуты обрезки повторены.</b> 42 метода из 103 помечены у эталона
    /// <c>[RequiresUnreferencedCode]</c> и <c>[RequiresDynamicCode]</c> - ровно
    /// те, у которых контракт задан через <c>JsonSerializerOptions</c>.
    /// Потерять их значило бы отобрать у потребителя, публикующегося в AOT,
    /// предупреждение на его собственной строке и отдать взамен падение в
    /// рантайме.
    /// </para>
    /// </summary>
    public static class JsonSerializer
    {
        //тексты взяты у эталона дословно: предупреждение увидит потребитель, и
        //оно не должно отличаться от того, которое он увидел бы без нас
        private const string Unreferenced =
            "JSON serialization and deserialization might require types that cannot be statically analyzed."
            + " Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the"
            + " required types are preserved.";

        private const string DynamicCode =
            "JSON serialization and deserialization might require types that cannot be statically analyzed"
            + " and might need runtime code generation. Use System.Text.Json source generation for native"
            + " AOT applications.";

        private static bool CanServe<TValue>(JsonSerializerOptions? options)
        {
            return CompatBinding<TValue>.IsBound && CompatOptions.IsDefault(options);
        }

        /// <summary>
        /// Единственная точка, через которую быстрый путь читает, - и она же
        /// переодевает отказ.
        ///
        /// <para>
        /// <c>JsonException</c> эталона наследует прямо <c>Exception</c>
        /// (спрошено рефлексией, а не вычитано), а наш
        /// <c>JsonDocumentException</c> - <c>InvalidOperationException</c>.
        /// Общего предка, кроме <c>Exception</c>, у них нет. Значит
        /// <c>catch (JsonException)</c>, стоявший у потребителя до подмены,
        /// после неё перестал бы ловить - и отказ, который он обрабатывал,
        /// вышел бы наружу. Тише поломки не бывает: документ не изменился,
        /// скорость выросла, а обработчик молча исчез.
        /// </para>
        ///
        /// <para>
        /// Поэтому здесь, в фасаде, а не в <c>JsonGoddess.Common</c>: только
        /// фасад имеет право зависеть от <c>System.Text.Json</c>. Исходный
        /// отказ уезжает внутренним - он точнее, у него есть <c>Anchor</c> и
        /// смещение в байтах от начала документа.
        /// </para>
        /// </summary>
        private static TValue? Read<TValue>(ReadOnlySpan<byte> utf8Json)
        {
            try
            {
                return CompatBinding<TValue>.FromUtf8!(utf8Json);
            }
            catch (JsonDocumentException failure)
            {
                throw new JsonException(
                    failure.Message,
                    failure.Path,
                    failure.LineNumber < 0 ? null : (long?)failure.LineNumber,
                    failure.BytePositionInLine < 0 ? null : (long?)failure.BytePositionInLine,
                    failure
                    );
            }
        }

        // ================= запись: в строку =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            if (CanServe<TValue>(options))
            {
                var utf8 = CompatBinding<TValue>.ToUtf8Bytes!(value);
                return Encoding.UTF8.GetString(utf8, 0, utf8.Length);
            }

            return Reference.Serialize(value, options);
        }

        public static string Serialize<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Serialize(value, jsonTypeInfo);

        public static string Serialize(object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.Serialize(value, jsonTypeInfo);

        public static string Serialize(object? value, Type inputType, JsonSerializerContext context) =>
            Reference.Serialize(value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static string Serialize(object? value, Type inputType, JsonSerializerOptions? options = null) =>
            Reference.Serialize(value, inputType, options);

        // ================= запись: в байты =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            if (CanServe<TValue>(options))
            {
                return CompatBinding<TValue>.ToUtf8Bytes!(value);
            }

            return Reference.SerializeToUtf8Bytes(value, options);
        }

        public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.SerializeToUtf8Bytes(value, jsonTypeInfo);

        public static byte[] SerializeToUtf8Bytes(object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.SerializeToUtf8Bytes(value, jsonTypeInfo);

        public static byte[] SerializeToUtf8Bytes(object? value, Type inputType, JsonSerializerContext context) =>
            Reference.SerializeToUtf8Bytes(value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static byte[] SerializeToUtf8Bytes(
            object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.SerializeToUtf8Bytes(value, inputType, options);

        // ================= запись: в поток =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
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

            Reference.Serialize(utf8Json, value, options);
        }

        public static void Serialize<TValue>(Stream utf8Json, TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Serialize(utf8Json, value, jsonTypeInfo);

        public static void Serialize(Stream utf8Json, object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.Serialize(utf8Json, value, jsonTypeInfo);

        public static void Serialize(
            Stream utf8Json, object? value, Type inputType, JsonSerializerContext context
            ) =>
            Reference.Serialize(utf8Json, value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static void Serialize(
            Stream utf8Json, object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.Serialize(utf8Json, value, inputType, options);

        // ================= запись: в чужой писатель =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
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

            Reference.Serialize(writer, value, options);
        }

        public static void Serialize<TValue>(
            Utf8JsonWriter writer, TValue value, JsonTypeInfo<TValue> jsonTypeInfo
            ) =>
            Reference.Serialize(writer, value, jsonTypeInfo);

        public static void Serialize(Utf8JsonWriter writer, object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.Serialize(writer, value, jsonTypeInfo);

        public static void Serialize(
            Utf8JsonWriter writer, object? value, Type inputType, JsonSerializerContext context
            ) =>
            Reference.Serialize(writer, value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static void Serialize(
            Utf8JsonWriter writer, object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.Serialize(writer, value, inputType, options);

        // ================= запись: в модель эталона =================
        //
        // JsonDocument, JsonElement и JsonNode - структуры самого эталона.
        // Быстрый путь здесь возможен (написать байты нашим кодом и отдать их
        // на разбор эталону), но не очевиден: выигрыш будет только в половине
        // работы, а лишний byte[] появится точно. Пока отдаём целиком, вопрос
        // записан в §15 - его решает замер, а не рассуждение.

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonDocument SerializeToDocument<TValue>(
            TValue value, JsonSerializerOptions? options = null
            ) =>
            Reference.SerializeToDocument(value, options);

        public static JsonDocument SerializeToDocument<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.SerializeToDocument(value, jsonTypeInfo);

        public static JsonDocument SerializeToDocument(object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.SerializeToDocument(value, jsonTypeInfo);

        public static JsonDocument SerializeToDocument(
            object? value, Type inputType, JsonSerializerContext context
            ) =>
            Reference.SerializeToDocument(value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonDocument SerializeToDocument(
            object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.SerializeToDocument(value, inputType, options);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonElement SerializeToElement<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            Reference.SerializeToElement(value, options);

        public static JsonElement SerializeToElement<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.SerializeToElement(value, jsonTypeInfo);

        public static JsonElement SerializeToElement(object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.SerializeToElement(value, jsonTypeInfo);

        public static JsonElement SerializeToElement(
            object? value, Type inputType, JsonSerializerContext context
            ) =>
            Reference.SerializeToElement(value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonElement SerializeToElement(
            object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.SerializeToElement(value, inputType, options);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonNode? SerializeToNode<TValue>(TValue value, JsonSerializerOptions? options = null) =>
            Reference.SerializeToNode(value, options);

        public static JsonNode? SerializeToNode<TValue>(TValue value, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.SerializeToNode(value, jsonTypeInfo);

        public static JsonNode? SerializeToNode(object? value, JsonTypeInfo jsonTypeInfo) =>
            Reference.SerializeToNode(value, jsonTypeInfo);

        public static JsonNode? SerializeToNode(object? value, Type inputType, JsonSerializerContext context) =>
            Reference.SerializeToNode(value, inputType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static JsonNode? SerializeToNode(
            object? value, Type inputType, JsonSerializerOptions? options = null
            ) =>
            Reference.SerializeToNode(value, inputType, options);

        // ================= запись: async, целиком эталону =================

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static Task SerializeAsync<TValue>(
            Stream utf8Json,
            TValue value,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, options, cancellationToken);

        public static Task SerializeAsync<TValue>(
            Stream utf8Json,
            TValue value,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, jsonTypeInfo, cancellationToken);

        public static Task SerializeAsync(
            Stream utf8Json,
            object? value,
            JsonTypeInfo jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, jsonTypeInfo, cancellationToken);

        public static Task SerializeAsync(
            Stream utf8Json,
            object? value,
            Type inputType,
            JsonSerializerContext context,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, inputType, context, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static Task SerializeAsync(
            Stream utf8Json,
            object? value,
            Type inputType,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, inputType, options, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static Task SerializeAsync<TValue>(
            PipeWriter utf8Json,
            TValue value,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, options, cancellationToken);

        public static Task SerializeAsync<TValue>(
            PipeWriter utf8Json,
            TValue value,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, jsonTypeInfo, cancellationToken);

        public static Task SerializeAsync(
            PipeWriter utf8Json,
            object? value,
            JsonTypeInfo jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, jsonTypeInfo, cancellationToken);

        public static Task SerializeAsync(
            PipeWriter utf8Json,
            object? value,
            Type inputType,
            JsonSerializerContext context,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, inputType, context, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static Task SerializeAsync(
            PipeWriter utf8Json,
            object? value,
            Type inputType,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.SerializeAsync(utf8Json, value, inputType, options, cancellationToken);

        // ================= чтение: из строки =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(string json, JsonSerializerOptions? options = null)
        {
            if (json is null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (CanServe<TValue>(options))
            {
                return Read<TValue>(Encoding.UTF8.GetBytes(json));
            }

            return Reference.Deserialize<TValue>(json, options);
        }

        public static TValue? Deserialize<TValue>(string json, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Deserialize(json, jsonTypeInfo);

        public static object? Deserialize(string json, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(json, jsonTypeInfo);

        public static object? Deserialize(string json, Type returnType, JsonSerializerContext context) =>
            Reference.Deserialize(json, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(string json, Type returnType, JsonSerializerOptions? options = null) =>
            Reference.Deserialize(json, returnType, options);

        // ================= чтение: из байтов =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(
            ReadOnlySpan<byte> utf8Json, JsonSerializerOptions? options = null
            )
        {
            if (CanServe<TValue>(options))
            {
                return Read<TValue>(utf8Json);
            }

            return Reference.Deserialize<TValue>(utf8Json, options);
        }

        public static TValue? Deserialize<TValue>(
            ReadOnlySpan<byte> utf8Json, JsonTypeInfo<TValue> jsonTypeInfo
            ) =>
            Reference.Deserialize(utf8Json, jsonTypeInfo);

        public static object? Deserialize(ReadOnlySpan<byte> utf8Json, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(utf8Json, jsonTypeInfo);

        public static object? Deserialize(
            ReadOnlySpan<byte> utf8Json, Type returnType, JsonSerializerContext context
            ) =>
            Reference.Deserialize(utf8Json, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            ReadOnlySpan<byte> utf8Json, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(utf8Json, returnType, options);

        // ================= чтение: из символов =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(ReadOnlySpan<char> json, JsonSerializerOptions? options = null)
        {
            if (CanServe<TValue>(options))
            {
                return Read<TValue>(Transcode(json));
            }

            return Reference.Deserialize<TValue>(json, options);
        }

        public static TValue? Deserialize<TValue>(ReadOnlySpan<char> json, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Deserialize(json, jsonTypeInfo);

        public static object? Deserialize(ReadOnlySpan<char> json, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(json, jsonTypeInfo);

        public static object? Deserialize(
            ReadOnlySpan<char> json, Type returnType, JsonSerializerContext context
            ) =>
            Reference.Deserialize(json, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            ReadOnlySpan<char> json, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(json, returnType, options);

        // ================= чтение: из потока =================

        [CompatFastPath]
        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
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
                return Read<TValue>(ReadToEnd(utf8Json));
            }

            return Reference.Deserialize<TValue>(utf8Json, options);
        }

        public static TValue? Deserialize<TValue>(Stream utf8Json, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Deserialize(utf8Json, jsonTypeInfo);

        public static object? Deserialize(Stream utf8Json, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(utf8Json, jsonTypeInfo);

        public static object? Deserialize(Stream utf8Json, Type returnType, JsonSerializerContext context) =>
            Reference.Deserialize(utf8Json, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            Stream utf8Json, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(utf8Json, returnType, options);

        // ================= чтение: из чужого читателя =================
        //
        // Быстрого пути здесь нет и он не бесплатен: чтобы отдать нашему коду
        // документ, надо сперва узнать, где кончается значение, на котором
        // стоит читатель, - то есть пройти его читателем эталона. Ровно ту
        // работу, ради обхода которой всё и затевалось.

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(
            ref Utf8JsonReader reader, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize<TValue>(ref reader, options);

        public static TValue? Deserialize<TValue>(
            ref Utf8JsonReader reader, JsonTypeInfo<TValue> jsonTypeInfo
            ) =>
            Reference.Deserialize(ref reader, jsonTypeInfo);

        public static object? Deserialize(ref Utf8JsonReader reader, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(ref reader, jsonTypeInfo);

        public static object? Deserialize(
            ref Utf8JsonReader reader, Type returnType, JsonSerializerContext context
            ) =>
            Reference.Deserialize(ref reader, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            ref Utf8JsonReader reader, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(ref reader, returnType, options);

        // ================= чтение: из модели эталона =================
        //
        // У эталона эти пятнадцать - расширения; у нас намеренно нет (почему -
        // в комментарии к классу). Вызов document.Deserialize<T>() у
        // потребителя связывается с эталоном и делает то же самое: все
        // пятнадцать всё равно уходят туда.

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(
            JsonDocument document, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize<TValue>(document, options);

        public static TValue? Deserialize<TValue>(
            JsonDocument document, JsonTypeInfo<TValue> jsonTypeInfo
            ) =>
            Reference.Deserialize(document, jsonTypeInfo);

        public static object? Deserialize(JsonDocument document, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(document, jsonTypeInfo);

        public static object? Deserialize(
            JsonDocument document, Type returnType, JsonSerializerContext context
            ) =>
            Reference.Deserialize(document, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            JsonDocument document, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(document, returnType, options);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(
            JsonElement element, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize<TValue>(element, options);

        public static TValue? Deserialize<TValue>(
            JsonElement element, JsonTypeInfo<TValue> jsonTypeInfo
            ) =>
            Reference.Deserialize(element, jsonTypeInfo);

        public static object? Deserialize(JsonElement element, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(element, jsonTypeInfo);

        public static object? Deserialize(
            JsonElement element, Type returnType, JsonSerializerContext context
            ) =>
            Reference.Deserialize(element, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            JsonElement element, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(element, returnType, options);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static TValue? Deserialize<TValue>(JsonNode? node, JsonSerializerOptions? options = null) =>
            Reference.Deserialize<TValue>(node, options);

        public static TValue? Deserialize<TValue>(JsonNode? node, JsonTypeInfo<TValue> jsonTypeInfo) =>
            Reference.Deserialize(node, jsonTypeInfo);

        public static object? Deserialize(JsonNode? node, JsonTypeInfo jsonTypeInfo) =>
            Reference.Deserialize(node, jsonTypeInfo);

        public static object? Deserialize(JsonNode? node, Type returnType, JsonSerializerContext context) =>
            Reference.Deserialize(node, returnType, context);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static object? Deserialize(
            JsonNode? node, Type returnType, JsonSerializerOptions? options = null
            ) =>
            Reference.Deserialize(node, returnType, options);

        // ================= чтение: async, целиком эталону =================

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            Stream utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync<TValue>(utf8Json, options, cancellationToken);

        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            Stream utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, jsonTypeInfo, cancellationToken);

        public static ValueTask<object?> DeserializeAsync(
            Stream utf8Json,
            JsonTypeInfo jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, jsonTypeInfo, cancellationToken);

        public static ValueTask<object?> DeserializeAsync(
            Stream utf8Json,
            Type returnType,
            JsonSerializerContext context,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, returnType, context, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static ValueTask<object?> DeserializeAsync(
            Stream utf8Json,
            Type returnType,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, returnType, options, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            Stream utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable<TValue>(utf8Json, options, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            Stream utf8Json,
            bool topLevelValues,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable<TValue>(utf8Json, topLevelValues, options, cancellationToken);

        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            Stream utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable(utf8Json, jsonTypeInfo, cancellationToken);

        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            Stream utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            bool topLevelValues,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable(utf8Json, jsonTypeInfo, topLevelValues, cancellationToken);

#if NET10_0_OR_GREATER
        // PipeReader-перегрузки чтения появились в 10.0 - у эталона, не у нас.
        // На netstandard2.0 и net8.0 мы собираемся против закреплённого пакета
        // System.Text.Json 9.0.0, где их нет, и добавить их значило бы обещать
        // то, чего в той сборке не существует. Что условие стоит именно здесь и
        // именно такое - проверяет SurfaceFixture на каждом таргете отдельно.

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            PipeReader utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync<TValue>(utf8Json, options, cancellationToken);

        public static ValueTask<TValue?> DeserializeAsync<TValue>(
            PipeReader utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, jsonTypeInfo, cancellationToken);

        public static ValueTask<object?> DeserializeAsync(
            PipeReader utf8Json,
            JsonTypeInfo jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, jsonTypeInfo, cancellationToken);

        public static ValueTask<object?> DeserializeAsync(
            PipeReader utf8Json,
            Type returnType,
            JsonSerializerContext context,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, returnType, context, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static ValueTask<object?> DeserializeAsync(
            PipeReader utf8Json,
            Type returnType,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsync(utf8Json, returnType, options, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            PipeReader utf8Json,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable<TValue>(utf8Json, options, cancellationToken);

        [RequiresUnreferencedCode(Unreferenced), RequiresDynamicCode(DynamicCode)]
        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            PipeReader utf8Json,
            bool topLevelValues,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable<TValue>(utf8Json, topLevelValues, options, cancellationToken);

        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            PipeReader utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable(utf8Json, jsonTypeInfo, cancellationToken);

        public static IAsyncEnumerable<TValue?> DeserializeAsyncEnumerable<TValue>(
            PipeReader utf8Json,
            JsonTypeInfo<TValue> jsonTypeInfo,
            bool topLevelValues,
            CancellationToken cancellationToken = default
            ) =>
            Reference.DeserializeAsyncEnumerable(utf8Json, jsonTypeInfo, topLevelValues, cancellationToken);
#endif

        // ================= вспомогательное =================

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
