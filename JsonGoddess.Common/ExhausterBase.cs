using System;

namespace JsonGoddess
{
    /// <summary>
    /// База для всякого sink'а записи и <b>единственный</b> контракт, который
    /// проверяет генератор: реализации одного лишь <see cref="IExhauster"/>
    /// недостаточно.
    ///
    /// Правило, ради которого это класс, а не интерфейс: <b>новый член сюда
    /// добавляется только virtual и только с рабочим телом по умолчанию</b>.
    /// На интерфейсе любое добавление ломает всех реализаторов разом, и
    /// по исходникам, и по бинарю; на классе - никого.
    ///
    /// Перегрузки для <see cref="Nullable{T}"/> уже реализованы здесь и
    /// абстрактными не являются: писать их руками в каждом sink'е - работа без
    /// содержания, а <c>null</c> в JSON пишется одинаково всегда.
    /// </summary>
    public abstract class ExhausterBase : IExhauster
    {
        public abstract void AppendRaw(ReadOnlySpan<byte> utf8);

        public abstract void AppendNull();

        public abstract void Append(bool value);

        public abstract void Append(sbyte value);

        public abstract void Append(byte value);

        public abstract void Append(short value);

        public abstract void Append(ushort value);

        public abstract void Append(int value);

        public abstract void Append(uint value);

        public abstract void Append(long value);

        public abstract void Append(ulong value);

        public abstract void Append(float value);

        public abstract void Append(double value);

        public abstract void Append(decimal value);

        public abstract void Append(char value);

        public abstract void Append(string? value);

        public abstract void Append(DateTime value);

        public abstract void Append(DateTimeOffset value);

        public abstract void Append(TimeSpan value);

        public abstract void Append(Guid value);

        public abstract void AppendBase64(byte[]? value);

        public virtual void Append(bool? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(sbyte? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(byte? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(short? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(ushort? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(int? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(uint? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(long? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(ulong? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(float? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(double? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(decimal? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(char? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(DateTime? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(DateTimeOffset? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(TimeSpan? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }

        public virtual void Append(Guid? value)
        {
            if (value.HasValue) { Append(value.Value); } else { AppendNull(); }
        }
    }
}
