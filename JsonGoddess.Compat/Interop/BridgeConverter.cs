using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsonGoddess.Compat.Interop
{
    /// <summary>
    /// Конвертер, через который эталон зовёт порождённый код.
    ///
    /// <para>
    /// Стороны устроены по-разному, и это не непоследовательность, а вывод из
    /// замера (PLAN.md §12.8).
    /// </para>
    ///
    /// <para>
    /// <b>Запись</b> идёт куском. Мы пишем документ в свою раковину целиком и
    /// отдаём его одним <c>WriteRawValue</c>. Прослойка эталона здесь стои́т
    /// одно копирование, и из выигрыша маршрута A мост сохраняет 84%.
    /// </para>
    ///
    /// <para>
    /// <b>Чтение</b> идёт по токенам. Способ, симметричный записи, - добыть
    /// сырой кусок документа и разобрать его своим сканером - был написан и
    /// <b>отвергнут замером</b>: чтобы узнать, где кончается значение, читатель
    /// эталона обязан пройти его целиком (<c>Skip</c>, 367 ns на REGULAR), и
    /// документ читается дважды. Мост тогда ровно так же быстр, как эталон, то
    /// есть бесполезен. Разбор прямо из <see cref="Utf8JsonReader"/> платит за
    /// токенизацию один раз и обгоняет эталон почти вдвое на массиве.
    /// </para>
    ///
    /// <para>
    /// У второго способа есть побочная выгода, которой не было у первого:
    /// лексику проверяет сам читатель эталона. Управляющий символ в строке,
    /// негодный UTF-8, число не по грамматике, превышенная глубина - всё это
    /// отвергает он, и отвергает по определению так же, как отверг бы без нас.
    /// Стражам порождённого кода здесь делать нечего.
    /// </para>
    /// </summary>
    internal sealed class BridgeConverter<T> : JsonConverter<T>
    {
        /// <summary>
        /// Раковина на поток. Конвертер живёт столько же, сколько
        /// <see cref="JsonSerializerOptions"/>, то есть обычно всё время
        /// работы приложения, и звать его будут из скольких угодно потоков
        /// сразу.
        /// </summary>
        [ThreadStatic]
        private static CompatUtf8Exhauster? _exhauster;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return BridgeBinding<T>.Read!(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var exhauster = _exhauster ??= new CompatUtf8Exhauster();
            exhauster.Reset();

            BridgeBinding<T>.Write!(exhauster, value);

            //skipInputValidation: проверять нечего. Байты написал порождённый
            //код, а не чужой ввод, и второй проход по ним был бы платой за
            //недоверие к самим себе.
            writer.WriteRawValue(exhauster.WrittenSpan, skipInputValidation: true);
        }

        /// <summary>
        /// <c>null</c> обрабатываем сами, а не отдаём эталону.
        ///
        /// По умолчанию эталон не зовёт конвертер на <c>null</c> для
        /// ссылочного типа: он пишет <c>null</c> и читает его сам. Нам это
        /// подходит на записи, но не на чтении: значение <c>null</c> на месте
        /// структуры обязано кончиться отказом, а эталон вернул бы умолчание.
        /// Поэтому конвертер объявляет, что справится сам, и порождённый
        /// читатель первым делом смотрит на токен.
        /// </summary>
        public override bool HandleNull => true;
    }
}
