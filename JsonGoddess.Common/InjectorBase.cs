using System;

namespace JsonGoddess
{
    /// <summary>
    /// База для всякого sink'а чтения и единственный контракт, который
    /// проверяет генератор. Правило то же, что у <see cref="ExhausterBase"/>:
    /// новый член добавляется virtual с рабочим телом.
    /// </summary>
    public abstract class InjectorBase : IInjector
    {
        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out bool value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out sbyte value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out byte value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out short value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ushort value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out int value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out uint value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out long value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ulong value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out float value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out double value);

        public abstract void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out decimal value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out string value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out char value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTime value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTimeOffset value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out TimeSpan value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out Guid value);

        public abstract void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out byte[] value);
    }
}
