using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Bridge
{
    /// <summary>
    /// Макет маршрута B (PLAN.md §10): порождённый код, выданный эталону под
    /// видом <c>JsonTypeInfo&lt;T&gt;</c>. Рукописный - потому что вопрос,
    /// ради которого он написан, стоит <b>перед</b> генератором, а не после:
    /// остаётся ли выигрыш, когда между нами и байтами лежит
    /// <c>Utf8JsonWriter</c>/<c>Utf8JsonReader</c>. Если не остаётся, писать
    /// генератор незачем.
    ///
    /// <para>
    /// Запись проста: пишем свой кусок в свою раковину и отдаём его целиком
    /// через <c>WriteRawValue</c>. Чтение сложнее - публичного способа взять у
    /// читателя сырой кусок документа нет (<c>JsonMarshal.GetRawUtf8Value</c>
    /// умеет только <c>JsonElement</c>), поэтому кусок восстанавливается по
    /// указателю открывающего токена и длине <c>BytesConsumed -
    /// TokenStartIndex</c>. Приём верен ровно тогда, когда байты значения
    /// лежат подряд в одной памяти; это проверяется, а не предполагается.
    /// </para>
    /// </summary>
    public sealed unsafe class OrderBridgeConverter : JsonConverter<Order?>
    {
        [ThreadStatic]
        private static CompatUtf8Exhauster? _exhauster;

        /// <summary>
        /// Сколько раз сырьё оказалось неразрывным, и сколько раз пришлось
        /// откатываться. Считается затем, чтобы замер не выдал за скорость
        /// моста скорость отката, случившегося на каждом вызове.
        /// </summary>
        public static long Contiguous;

        public static long FellBack;

        public override void Write(Utf8JsonWriter writer, Order? value, JsonSerializerOptions options)
        {
            var exhauster = _exhauster ??= new CompatUtf8Exhauster(4096);
            exhauster.Reset();
            OrderBridgeHost.Serialize(exhauster, value);
            writer.WriteRawValue(exhauster.WrittenSpan, skipInputValidation: true);
        }

        public override Order? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            //копия читателя - это откат: после Skip вернуться назад нельзя, а
            //узнать, годится ли сырьё, можно только после него
            var snapshot = reader;

            var opening = reader.ValueSpan;
            if (opening.Length == 1)
            {
                ref var anchor = ref MemoryMarshal.GetReference(opening);
                var startIndex = reader.TokenStartIndex;
                reader.Skip();
                var length = (int)(reader.BytesConsumed - startIndex);

                var closing = reader.ValueSpan;
                if (closing.Length == 1
                    && Unsafe.ByteOffset(ref anchor, ref MemoryMarshal.GetReference(closing)) == (IntPtr)(length - 1))
                {
                    Contiguous++;

                    //Unsafe.AsPointer, а не MemoryMarshal.CreateReadOnlySpan:
                    //последнего нет в System.Memory для net472
                    var raw = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref anchor), length);
                    OrderBridgeHost.Deserialize(DefaultInjector.Instance, raw, out var direct);
                    return direct;
                }
            }
            else
            {
                reader.Skip();
            }

            FellBack++;
            return ReadThroughDocument(ref snapshot);
        }

        /// <summary>
        /// Откат: значение приехало кусками (async-стрим, <c>PipeReader</c>) -
        /// подряд его в памяти нет. Собираем через <c>JsonDocument</c> и
        /// читаем уже из непрерывного куска.
        /// </summary>
        private static Order? ReadThroughDocument(ref Utf8JsonReader reader)
        {
            using (var document = JsonDocument.ParseValue(ref reader))
            {
#if NET9_0_OR_GREATER
                var raw = System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(document.RootElement);
#else
                //JsonMarshal появился только в .NET 9; ниже приходится платить
                //ещё и строкой - лишний довод в пользу того, чтобы откат
                //случался редко
                var raw = System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetRawText());
#endif
                OrderBridgeHost.Deserialize(DefaultInjector.Instance, raw, out var result);
                return result;
            }
        }
    }

    /// <summary>
    /// Резолвер, который подменяет один тип и отдаёт остальные эталону.
    /// В настоящем мосте его напишет генератор - по тому же списку типов,
    /// который он уже обходит для маршрута A.
    /// </summary>
    public sealed class OrderBridgeResolver : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver _fallback = new DefaultJsonTypeInfoResolver();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(Order))
            {
                return JsonMetadataServices.CreateValueInfo<Order?>(options, new OrderBridgeConverter());
            }

            return _fallback.GetTypeInfo(type, options);
        }
    }
}
