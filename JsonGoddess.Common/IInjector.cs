using System;

namespace JsonGoddess
{
    /// <summary>
    /// Sink чтения: превращает сырую лексему в значение. Зеркало
    /// <see cref="IExhauster"/>.
    ///
    /// Две группы методов, и деление не по типу, а по тому, <b>чем значение
    /// приехало в документе</b>:
    /// <list type="bullet">
    /// <item><see cref="Parse(ref JsonParseContext, ReadOnlySpan{byte}, out int)"/>
    /// и соседи - лексема числа или литерала, байты как есть;</item>
    /// <item><c>ParseText</c> - значение приехало JSON-строкой, и на входе
    /// содержимое <b>без кавычек</b> плюс признак наличия обратного слэша.</item>
    /// </list>
    /// <c>null</c> сюда не доезжает: литерал <c>null</c> распознаёт
    /// сгенерированный код, потому что решение "положить null или оставить
    /// значение по умолчанию" принадлежит члену, а не лексике.
    ///
    /// Реализовывать следует <see cref="InjectorBase"/>, а не этот интерфейс -
    /// по той же причине, что и с <see cref="ExhausterBase"/>.
    /// </summary>
    public interface IInjector
    {
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out bool value);

        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out sbyte value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out byte value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out short value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ushort value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out int value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out uint value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out long value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out ulong value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out float value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out double value);
        void Parse(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, out decimal value);

        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out string value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out char value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTime value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out DateTimeOffset value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out TimeSpan value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out Guid value);
        void ParseText(ref JsonParseContext context, scoped ReadOnlySpan<byte> raw, bool hasEscape, out byte[] value);
    }
}
