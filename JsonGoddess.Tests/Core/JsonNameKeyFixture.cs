using System;
using System.Collections.Generic;
using System.Text;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// Ключ имени. Тесты здесь проверяют не «работает», а два свойства, на
    /// которые опирается форма порождаемого кода: полноту ключа для коротких
    /// имён и независимость от порядка байтов машины.
    /// </summary>
    public class JsonNameKeyFixture
    {
        private static ulong Key(string name)
        {
            return JsonNameKey.Compute(Encoding.UTF8.GetBytes(name));
        }

        /// <summary>
        /// Эталон, написанный максимально тупо и заведомо little-endian.
        /// Совпадение с ним и есть проверка того, что генератор может
        /// напечатать константу на одной машине, а сравнить её на другой.
        /// </summary>
        private static ulong ReferenceKey(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            var key = (ulong)(byte)bytes.Length << 56;
            for (var i = 0; i < bytes.Length && i < 7; i++)
            {
                key |= (ulong)bytes[i] << (i * 8);
            }

            return key;
        }

        public static IEnumerable<object[]> Names()
        {
            yield return new object[] { "", };
            yield return new object[] { "a", };
            yield return new object[] { "Id", };
            yield return new object[] { "Sku", };
            yield return new object[] { "Paid", };
            yield return new object[] { "Total", };
            yield return new object[] { "Lines", };
            yield return new object[] { "abcdef", };
            yield return new object[] { "Created", };
            yield return new object[] { "Field00", };
            yield return new object[] { "Customer", };
            yield return new object[] { "Quantity", };
            yield return new object[] { "Reference", };
            yield return new object[] { "a_very_long_property_name_indeed", };
            yield return new object[] { "имя", };
            yield return new object[] { new string('x', 300), };
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void MatchesTheLittleEndianReference(string name)
        {
            Assert.Equal(ReferenceKey(name), Key(name));
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void LengthLivesInTheTopByte(string name)
        {
            var expected = (byte)Encoding.UTF8.GetByteCount(name);

            Assert.Equal(expected, (byte)(Key(name) >> 56));
        }

        /// <summary>
        /// Свойство, на котором держится вся форма диспетчера: до семи байт
        /// ключ полон, поэтому разные имена не могут дать один ключ, и
        /// сравнение байтов в сгенерированном коде не нужно.
        /// </summary>
        [Fact]
        public void ShortNamesNeverCollide()
        {
            var seen = new Dictionary<ulong, string>();

            //все имена длиной 1..3 из небольшого алфавита плюс реальные имена
            const string Alphabet = "abzAZ_09";
            foreach (var name in EnumerateShortNames(Alphabet))
            {
                var key = Key(name);
                if (seen.TryGetValue(key, out var previous))
                {
                    Assert.Fail("keys collided for '" + previous + "' and '" + name + "'");
                }

                seen.Add(key, name);
            }
        }

        private static IEnumerable<string> EnumerateShortNames(string alphabet)
        {
            foreach (var a in alphabet)
            {
                yield return a.ToString();
                foreach (var b in alphabet)
                {
                    yield return new string(new[] { a, b, });
                    foreach (var c in alphabet)
                    {
                        yield return new string(new[] { a, b, c, });
                    }
                }
            }
        }

        [Fact]
        public void SevenByteNamesAreStillExact()
        {
            Assert.NotEqual(Key("Field00"), Key("Field01"));
            Assert.NotEqual(Key("Field00"), Key("Field10"));
            Assert.NotEqual(Key("abcdefg"), Key("abcdefh"));
            Assert.NotEqual(Key("abcdefg"), Key("abcdef"));
        }

        /// <summary>
        /// Обратная сторона той же раскладки, и её генератор обязан знать: от
        /// восьми байт ключ - только префильтр. Здесь это закреплено как
        /// поведение, а не обнаружено потом на чужом типе.
        /// </summary>
        [Fact]
        public void LongNamesSharingTheFirstSevenBytesCollideOnPurpose()
        {
            Assert.Equal(Key("Customer1"), Key("Customer2"));
            Assert.Equal(Key("abcdefgX"), Key("abcdefgY"));

            //и потому же разная длина их всё-таки разводит
            Assert.NotEqual(Key("Customer1"), Key("Customer12"));
        }

        [Fact]
        public void KeyOfANameReadFromADocumentMatchesTheCompiledConstant()
        {
            //так это будет выглядеть в сгенерированном коде: константа слева,
            //имя из документа справа
            var json = Encoding.UTF8.GetBytes("{\"Created\":1}");
            var position = 1;
            var name = JsonScan.ReadStringContent(json, ref position, out _);

            Assert.Equal(Key("Created"), JsonNameKey.Compute(name));
        }
    }
}
