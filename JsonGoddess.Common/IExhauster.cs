using System;

namespace JsonGoddess
{
    /// <summary>
    /// Sink записи. Один метод на один builtin, и метод пишет <b>законченную
    /// лексему JSON</b> вместе с кавычками там, где они полагаются: <c>Guid</c>
    /// уходит как <c>"a1b2..."</c>, <c>int</c> - как <c>123</c>.
    ///
    /// Структуру - скобки, имена свойств, запятые - sink не знает вовсе: она
    /// известна на этапе компиляции и приезжает сюда готовыми UTF-8-литералами
    /// через <see cref="AppendRaw"/>. Поэтому здесь нет ни стека глубины, ни
    /// автомата состояний, ни проверки парности: всё это есть у
    /// <c>Utf8JsonWriter</c> ровно потому, что он не знает, что ему напишут
    /// следующим.
    ///
    /// Реализовывать следует не этот интерфейс, а <see cref="ExhausterBase"/>:
    /// в класс новый член добавляется virtual с рабочим телом и никого не
    /// ломает, в интерфейс - ломает всех сразу.
    /// </summary>
    public interface IExhauster
    {
        /// <summary>
        /// Готовый кусок документа: скобка, имя свойства с двоеточием,
        /// запятая, либо всё это слитым в один литерал. Байты пишутся как есть.
        /// </summary>
        void AppendRaw(ReadOnlySpan<byte> utf8);

        void AppendNull();

        void Append(bool value);
        void Append(bool? value);

        void Append(sbyte value);
        void Append(sbyte? value);

        void Append(byte value);
        void Append(byte? value);

        void Append(short value);
        void Append(short? value);

        void Append(ushort value);
        void Append(ushort? value);

        void Append(int value);
        void Append(int? value);

        void Append(uint value);
        void Append(uint? value);

        void Append(long value);
        void Append(long? value);

        void Append(ulong value);
        void Append(ulong? value);

        void Append(float value);
        void Append(float? value);

        void Append(double value);
        void Append(double? value);

        void Append(decimal value);
        void Append(decimal? value);

        /// <summary>
        /// Односимвольная строка - так пишет System.Text.Json. Это расхождение
        /// с XmlSerDe, где char уходит кодовой точкой, и причина простая:
        /// в JSON нет xsd, и односимвольная строка здесь единственная
        /// естественная форма.
        /// </summary>
        void Append(char value);
        void Append(char? value);

        /// <summary>
        /// Строка в кавычках с экранированием (<see cref="Internal.JsonStringEncoder"/>),
        /// либо <c>null</c>, если значение <c>null</c>.
        /// </summary>
        void Append(string? value);

        void Append(DateTime value);
        void Append(DateTime? value);

        void Append(DateTimeOffset value);
        void Append(DateTimeOffset? value);

        void Append(TimeSpan value);
        void Append(TimeSpan? value);

        void Append(Guid value);
        void Append(Guid? value);

        /// <summary>
        /// Массив байтов одной base64-строкой. Экранирования не требует: в
        /// алфавите base64 нет ни кавычки, ни обратного слэша.
        /// </summary>
        void AppendBase64(byte[]? value);
    }
}
