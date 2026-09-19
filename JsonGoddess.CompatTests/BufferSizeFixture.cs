using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using JsonGoddess.Compat.Interop;
using Xunit;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// <c>DefaultBufferSize</c> не виден в документе - и потому не повод
    /// отказываться от быстрого пути.
    ///
    /// <para>
    /// Допущение это внесено в <c>CompatOptions</c>, и цена ошибки в нём та
    /// же, что у всякого пропущенного свойства: не отказ, а тихо взятый
    /// быстрый путь на чужих настройках. Поэтому оно закрепляется <b>пробой у
    /// эталона</b>, а не рассуждением о том, что такое буфер: если эталон
    /// однажды начнёт писать по-разному при разных размерах, первый же
    /// <c>Assert</c> покраснеет раньше, чем послабление пропустит чужое
    /// поведение.
    /// </para>
    /// </summary>
    public class BufferSizeFixture
    {
        /// <summary>
        /// Единица - законный минимум, и он же самый злой: буфер меньше любого
        /// токена. Дальше - размеры вокруг тела запроса и заведомо больше его.
        /// </summary>
        private static readonly int[] Sizes = { 1, 16, 1024, 16 * 1024, 1 << 20, };

        private static JsonSerializerOptions Web(int? bufferSize, bool goddess)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            if (bufferSize is not null)
            {
                options.DefaultBufferSize = bufferSize.Value;
            }

            return goddess ? options.UseJsonGoddess() : options;
        }

        [Fact]
        public void The_reference_writes_the_same_document_whatever_the_buffer_size()
        {
            var order = Order.CreateSample();
            var expected = Reference.Serialize(order, Web(null, false));

            foreach (var size in Sizes)
            {
                Assert.Equal(expected, Reference.Serialize(order, Web(size, false)));
            }
        }

        [Fact]
        public async Task The_reference_writes_the_same_document_to_a_stream_whatever_the_buffer_size()
        {
            var order = Order.CreateSample();
            var expected = Reference.SerializeToUtf8Bytes(order, Web(null, false));

            foreach (var size in Sizes)
            {
                using (var stream = new MemoryStream())
                {
                    await Reference.SerializeAsync(stream, order, Web(size, false));
                    Assert.Equal(expected, stream.ToArray());
                }
            }
        }

        [Fact]
        public async Task The_reference_reads_the_same_values_whatever_the_buffer_size()
        {
            var json = Reference.SerializeToUtf8Bytes(Order.CreateSample(), Web(null, false));
            var expected = Reference.Serialize(Reference.Deserialize<Order>(json, Web(null, false)), Web(null, false));

            foreach (var size in Sizes)
            {
                using (var stream = new MemoryStream(json))
                {
                    var read = await Reference.DeserializeAsync<Order>(stream, Web(size, false));
                    Assert.Equal(expected, Reference.Serialize(read, Web(null, false)));
                }
            }
        }

        /// <summary>
        /// И только теперь - что мост за такие опции берётся. Порядок здесь
        /// существенный: сперва проверено, что брать безопасно, потом что
        /// берём.
        /// </summary>
        [Fact]
        public void The_bridge_serves_the_web_profile_whatever_the_buffer_size()
        {
            foreach (var size in Sizes)
            {
                var options = Web(size, true);

                Assert.Contains(
                    "web profile",
                    JsonGoddess.Compat.Interop.JsonGoddess.Explain(typeof(Order), options),
                    StringComparison.Ordinal
                    );

                Assert.Equal(
                    Reference.Serialize(Order.CreateSample(), Web(size, false)),
                    Reference.Serialize(Order.CreateSample(), options)
                    );
            }
        }

        /// <summary>
        /// Чтение через мост - тоже на всех размерах, включая единицу, где
        /// читатель эталона оказывается многосегментным на каждом токене.
        /// </summary>
        [Fact]
        public async Task The_bridge_reads_the_same_values_whatever_the_buffer_size()
        {
            var json = Reference.SerializeToUtf8Bytes(Order.CreateSample(), Web(null, false));
            var expected = Reference.Serialize(Reference.Deserialize<Order>(json, Web(null, false)), Web(null, false));

            foreach (var size in Sizes)
            {
                using (var stream = new MemoryStream(json))
                {
                    var read = await Reference.DeserializeAsync<Order>(stream, Web(size, true));
                    Assert.Equal(expected, Reference.Serialize(read, Web(null, false)));
                }
            }
        }
    }
}
