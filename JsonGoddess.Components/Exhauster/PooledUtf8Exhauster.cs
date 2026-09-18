namespace JsonGoddess
{
    /// <summary>
    /// Пишет в арендованный <c>byte[]</c>. Основной sink для случая "нужен
    /// документ целиком в памяти": результат отдаётся спаном без копии, а
    /// <see cref="PooledUtf8ExhausterBase.ToArray"/> копирует ровно записанный
    /// префикс, а не весь арендованный буфер.
    ///
    /// <para>
    /// Экранирует минимум RFC 8259 §7 - кавычку, обратный слэш и управляющие
    /// символы. Это осознанное расхождение с энкодером
    /// <c>System.Text.Json</c> по умолчанию (PLAN.md §8.4,
    /// <c>docs/stj-divergences.md</c> 1.1), и оно верно для того, кто позвал
    /// JsonGoddess по имени. Для того, кто подменил <c>JsonSerializer</c>
    /// фасадом, есть <see cref="CompatUtf8Exhauster"/>.
    /// </para>
    ///
    /// <para>
    /// Не потокобезопасен. <c>sealed</c> не для красоты: генератор зовёт sink по
    /// конкретному типу, и запечатанный девиртуализуется в обычный вызов.
    /// </para>
    /// </summary>
    public sealed class PooledUtf8Exhauster : PooledUtf8ExhausterBase
    {
        public PooledUtf8Exhauster()
            : base(DefaultCapacity)
        {
        }

        public PooledUtf8Exhauster(int capacity)
            : base(capacity)
        {
        }
    }
}
