using System;
using System.Collections.Generic;
using Xunit;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// Лексика каждого скаляра - байт в байт то же, что пишет
    /// <c>System.Text.Json</c>.
    ///
    /// Это первый и главный тест проекта. Формат вывода - обещание, которое
    /// нельзя взять из головы: "STJ пишет DateTime по ISO 8601" звучит
    /// осмысленно и не отвечает на вопрос, что он делает с нулевой дробной
    /// частью. Поэтому ожидание здесь не литерал, а прогон BCL.
    /// </summary>
    public class LexicalParityFixture
    {
        private static string Write(Action<PooledUtf8Exhauster> write)
        {
            using var exhauster = new PooledUtf8Exhauster();
            write(exhauster);
            return exhauster.ToString();
        }

        public static IEnumerable<object[]> Booleans()
        {
            yield return new object[] { true, };
            yield return new object[] { false, };
        }

        [Theory]
        [MemberData(nameof(Booleans))]
        public void Boolean(bool value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Int32s()
        {
            yield return new object[] { 0, };
            yield return new object[] { 1, };
            yield return new object[] { -1, };
            yield return new object[] { int.MaxValue, };
            yield return new object[] { int.MinValue, };
        }

        [Theory]
        [MemberData(nameof(Int32s))]
        public void Int32(int value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Int64s()
        {
            yield return new object[] { 0L, };
            yield return new object[] { long.MaxValue, };
            yield return new object[] { long.MinValue, };
        }

        [Theory]
        [MemberData(nameof(Int64s))]
        public void Int64(long value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        [Fact]
        public void UnsignedEdges()
        {
            Assert.Equal(Reference.Write(ulong.MaxValue), Write(e => e.Append(ulong.MaxValue)));
            Assert.Equal(Reference.Write(uint.MaxValue), Write(e => e.Append(uint.MaxValue)));
            Assert.Equal(Reference.Write(byte.MaxValue), Write(e => e.Append(byte.MaxValue)));
            Assert.Equal(Reference.Write(sbyte.MinValue), Write(e => e.Append(sbyte.MinValue)));
            Assert.Equal(Reference.Write(short.MinValue), Write(e => e.Append(short.MinValue)));
            Assert.Equal(Reference.Write(ushort.MaxValue), Write(e => e.Append(ushort.MaxValue)));
        }

        public static IEnumerable<object[]> Doubles()
        {
            yield return new object[] { 0.0, };
            yield return new object[] { 1.0, };
            yield return new object[] { -1.0, };
            yield return new object[] { 0.1, };
            yield return new object[] { 1.0 / 3.0, };
            yield return new object[] { 1e300, };
            yield return new object[] { 1e-300, };
            yield return new object[] { double.MaxValue, };
            yield return new object[] { double.MinValue, };
            yield return new object[] { double.Epsilon, };
            yield return new object[] { 12345.6789, };
        }

        [Theory]
        [MemberData(nameof(Doubles))]
        public void Double(double value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Singles()
        {
            yield return new object[] { 0f, };
            yield return new object[] { 1f, };
            yield return new object[] { -0.5f, };
            yield return new object[] { float.MaxValue, };
            yield return new object[] { float.MinValue, };
            yield return new object[] { float.Epsilon, };
            yield return new object[] { 1f / 3f, };
        }

        [Theory]
        [MemberData(nameof(Singles))]
        public void Single(float value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Decimals()
        {
            yield return new object[] { 0m, };
            yield return new object[] { 1.5m, };
            yield return new object[] { -1.5m, };
            yield return new object[] { 0.0000001m, };
            yield return new object[] { decimal.MaxValue, };
            yield return new object[] { decimal.MinValue, };
        }

        [Theory]
        [MemberData(nameof(Decimals))]
        public void Decimal(decimal value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Strings()
        {
            yield return new object[] { string.Empty, };
            yield return new object[] { "abc", };
            yield return new object[] { "кириллица", };
            yield return new object[] { "quote\" inside", };
            yield return new object[] { "back\\slash", };
            yield return new object[] { "line1\nline2", };
            yield return new object[] { "tab\there", };
            yield return new object[] { "\b\f\r", };
            yield return new object[] { ((char)1).ToString() + ((char)31).ToString(), };
            yield return new object[] { "a b", };
            yield return new object[] { new string('x', 500), };
            yield return new object[] { new string('\n', 40), };
        }

        /// <summary>
        /// Строки вне BMP вынесены отдельно: байт в байт мы с ними не
        /// совпадаем ни с одним энкодером STJ, и это закреплено в
        /// <see cref="EscapingDivergenceFixture"/>. На чтении они нужны здесь
        /// же, поэтому корпус разделён, а не урезан.
        /// </summary>
        public static IEnumerable<object[]> AstralStrings()
        {
            yield return new object[] { "\U0001F600 emoji", };
            yield return new object[] { "𐍈 gothic", };
        }

        [Theory]
        [MemberData(nameof(Strings))]
        public void String(string value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        [Fact]
        public void NullString()
        {
            Assert.Equal(Reference.Write((string?)null), Write(e => e.Append((string?)null)));
        }

        public static IEnumerable<object[]> Chars()
        {
            yield return new object[] { 'A', };
            yield return new object[] { '"', };
            yield return new object[] { '\\', };
            yield return new object[] { '\n', };
            yield return new object[] { 'я', };
            yield return new object[] { (char)1, };
        }

        [Theory]
        [MemberData(nameof(Chars))]
        public void Char(char value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> Guids()
        {
            yield return new object[] { Guid.Empty, };
            yield return new object[] { new Guid("6F9619FF-8B86-D011-B42D-00CF4FC964FF"), };
            yield return new object[] { new Guid("00000000-0000-0000-0000-000000000001"), };
        }

        [Theory]
        [MemberData(nameof(Guids))]
        public void Guid_(Guid value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> DateTimes()
        {
            yield return new object[] { new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), };
            yield return new object[] { new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), };
            yield return new object[] { new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local), };
            yield return new object[] { new DateTime(2020, 1, 2, 3, 4, 5, 123, DateTimeKind.Utc), };
            yield return new object[] { new DateTime(637000000000000000L, DateTimeKind.Utc), };
            yield return new object[] { DateTime.MinValue, };
            yield return new object[] { DateTime.MaxValue, };
        }

        [Theory]
        [MemberData(nameof(DateTimes))]
        public void DateTime_(DateTime value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> DateTimeOffsets()
        {
            yield return new object[] { new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), };
            yield return new object[] { new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)), };
            yield return new object[] { new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5.5)), };
            yield return new object[] { new DateTimeOffset(637000000000000000L, TimeSpan.Zero), };
        }

        [Theory]
        [MemberData(nameof(DateTimeOffsets))]
        public void DateTimeOffset_(DateTimeOffset value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> TimeSpans()
        {
            yield return new object[] { TimeSpan.Zero, };
            yield return new object[] { TimeSpan.FromDays(1), };
            yield return new object[] { TimeSpan.FromSeconds(-1), };
            yield return new object[] { new TimeSpan(1, 2, 3, 4, 5), };
            yield return new object[] { TimeSpan.MaxValue, };
            yield return new object[] { TimeSpan.MinValue, };
        }

        [Theory]
        [MemberData(nameof(TimeSpans))]
        public void TimeSpan_(TimeSpan value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.Append(value)));
        }

        public static IEnumerable<object[]> ByteArrays()
        {
            yield return new object[] { new byte[0], };
            yield return new object[] { new byte[] { 0, }, };
            yield return new object[] { new byte[] { 1, 2, 3, }, };
            yield return new object[] { new byte[] { 255, 254, 253, 252, }, };
            yield return new object[] { CreateBytes(255), };
        }

        [Theory]
        [MemberData(nameof(ByteArrays))]
        public void Base64(byte[] value)
        {
            Assert.Equal(Reference.Write(value), Write(e => e.AppendBase64(value)));
        }

        [Fact]
        public void NullValues()
        {
            Assert.Equal("null", Write(e => e.AppendNull()));
            Assert.Equal(Reference.Write((int?)null), Write(e => e.Append((int?)null)));
            Assert.Equal(Reference.Write((int?)7), Write(e => e.Append((int?)7)));
            Assert.Equal(Reference.Write((byte[]?)null), Write(e => e.AppendBase64(null)));
        }

        private static byte[] CreateBytes(int count)
        {
            var result = new byte[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = (byte)i;
            }

            return result;
        }
    }
}
