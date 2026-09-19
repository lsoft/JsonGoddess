using System;
using System.Collections.Generic;
using System.Text.Json;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests.Bridge
{
    /// <summary>
    /// Второй способ читать в мосте: не добывать сырой кусок документа, а
    /// разбирать прямо из <c>Utf8JsonReader</c> - одним проходом, переключателем
    /// по именам, с прямым присваиванием.
    ///
    /// <para>
    /// Написано руками намеренно. Вопрос, ради которого код существует, стоит
    /// перед генератором: у эталона на этой форме 1218 ns, из которых лишь
    /// 342 ns - токенизация; остальные 876 ns уходят на его собственную
    /// машинерию (<c>JsonPropertyInfo</c>, словарь имён, виртуальные
    /// конвертеры). Здесь токенизация та же самая - чужая и неизбежная, - а
    /// машинерии нет. Замер показывает, сколько из этих 876 ns можно не
    /// платить.
    /// </para>
    ///
    /// <para>
    /// Это <b>не</b> макет будущего генератора: настоящий его вариант обязан
    /// проверять дубликаты имён, соблюдать <c>JsonGuard</c>, уметь
    /// <c>[JsonFactory]</c> и отвергать лишнее. Здесь всего этого нет, и число
    /// поэтому - <b>верхняя граница</b>, недостижимый потолок. Если даже она
    /// не обгоняет эталон, обгонять нечем.
    /// </para>
    /// </summary>
    public static class OrderReaderOverUtf8JsonReader
    {
        public static Order? Read(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("expected an object");
            }

            var order = new Order();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return order;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("expected a property name");
                }

                //Имя сверяется ЦЕЛИКОМ, а не по длине.
                //
                //Первая редакция этого макета разводила члены по длине имени и
                //байты не сравнивала вовсе - у Order длина разводит пять членов
                //из семи, у OrderLine все четыре. Она была на 8% быстрее
                //порождённого читателя, и восемь этих процентов я чуть не
                //принял за накладные расходы генератора. Профиль по видам
                //значений (ValueReadFixture) показал, что помощники чтения
                //бесплатны, и остаться разница могла только здесь.
                //
                //Разница настоящая, но это не overhead, а цена правильности:
                //макет принимал за Id всякое двухбайтовое имя. Верхней границей
                //неправильная программа быть не может, и теперь её тут нет.
                var name = reader.ValueSpan;
                reader.Read();

                switch (name.Length)
                {
                    case 2:
                        if (name.SequenceEqual("Id"u8))
                        {
                            order.Id = reader.GetInt32();
                            continue;
                        }

                        break;

                    case 4:
                        if (name.SequenceEqual("Paid"u8))
                        {
                            order.Paid = reader.GetBoolean();
                            continue;
                        }

                        break;

                    case 5:
                        if (name.SequenceEqual("Total"u8))
                        {
                            order.Total = reader.GetDecimal();
                            continue;
                        }

                        if (name.SequenceEqual("Lines"u8))
                        {
                            order.Lines = ReadLines(ref reader);
                            continue;
                        }

                        break;

                    case 7:
                        if (name.SequenceEqual("Created"u8))
                        {
                            order.Created = reader.GetDateTime();
                            continue;
                        }

                        break;

                    case 8:
                        if (name.SequenceEqual("Customer"u8))
                        {
                            order.Customer = reader.GetString();
                            continue;
                        }

                        break;

                    case 9:
                        if (name.SequenceEqual("Reference"u8))
                        {
                            order.Reference = reader.GetGuid();
                            continue;
                        }

                        break;
                }

                reader.Skip();
            }

            throw new JsonException("the object never ended");
        }

        private static List<OrderLine>? ReadLines(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            var lines = new List<OrderLine>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return lines;
                }

                lines.Add(ReadLine(ref reader));
            }

            throw new JsonException("the array never ended");
        }

        private static OrderLine ReadLine(ref Utf8JsonReader reader)
        {
            var line = new OrderLine();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return line;
                }

                //имя сверяется целиком - по той же причине, что и у Order выше
                var name = reader.ValueSpan;
                reader.Read();

                switch (name.Length)
                {
                    case 3:
                        if (name.SequenceEqual("Sku"u8))
                        {
                            line.Sku = reader.GetString();
                            continue;
                        }

                        break;

                    case 4:
                        if (name.SequenceEqual("Note"u8))
                        {
                            line.Note = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                            continue;
                        }

                        break;

                    case 5:
                        if (name.SequenceEqual("Price"u8))
                        {
                            line.Price = reader.GetDecimal();
                            continue;
                        }

                        break;

                    case 8:
                        if (name.SequenceEqual("Quantity"u8))
                        {
                            line.Quantity = reader.GetInt32();
                            continue;
                        }

                        break;
                }

                reader.Skip();
            }

            throw new JsonException("the object never ended");
        }
    }

    /// <summary>
    /// Мост, читающий вторым способом. Запись у него та же, что у
    /// <see cref="OrderBridgeConverter"/>: сравнивать надо одно отличие, а не
    /// два.
    /// </summary>
    public sealed class OrderStreamingBridgeConverter : System.Text.Json.Serialization.JsonConverter<Order?>
    {
        [ThreadStatic]
        private static CompatUtf8Exhauster? _exhauster;

        public override void Write(Utf8JsonWriter writer, Order? value, JsonSerializerOptions options)
        {
            var exhauster = _exhauster ??= new CompatUtf8Exhauster(4096);
            exhauster.Reset();
            Generated.OrderBridgeHost.Serialize(exhauster, value);
            writer.WriteRawValue(exhauster.WrittenSpan, skipInputValidation: true);
        }

        public override Order? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return OrderReaderOverUtf8JsonReader.Read(ref reader);
        }
    }

    public sealed class OrderStreamingBridgeResolver : System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
    {
        private readonly System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver _fallback =
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();

        public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(Order))
            {
                return System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<Order?>(
                    options,
                    new OrderStreamingBridgeConverter()
                    );
            }

            return _fallback.GetTypeInfo(type, options);
        }
    }
}
