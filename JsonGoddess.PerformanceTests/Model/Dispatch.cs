namespace JsonGoddess.PerformanceTests.Model
{
    /// <summary>
    /// PREFIX: тридцать членов одной длины с общим семибайтовым префиксом.
    ///
    /// Форма заведена под вопрос O7 (§15 плана) и существует затем, чтобы
    /// поставить диспетчер имён в его худшее положение. Имя <c>Payload00</c> -
    /// девять байт, первые семь у всех тридцати одинаковы (<c>Payload</c>),
    /// значит ключ (§8.2) у всех тридцати <b>совпадает</b>: длина в ключе
    /// одна, первые семь байт одни. Тридцать членов схлопываются в одну метку
    /// <c>case</c>, и внутри неё остаётся цепочка из тридцати сравнений.
    ///
    /// Ни REGULAR, ни WIDE этого не показывают, и в противоположные стороны:
    /// на REGULAR длины разводят членов по корзинам, на WIDE имена по семь
    /// байт - ключ полон, сравнений нет вовсе. То есть существующие формы
    /// меряют диспетчер там, где он уже хорош.
    ///
    /// Все члены - <c>int</c> нарочно: чем дешевле чтение значения, тем
    /// большую долю времени занимает сопоставление имени, а мерится здесь
    /// именно оно.
    /// </summary>
    public sealed class Prefix
    {
        public int Payload00 { get; set; }

        public int Payload01 { get; set; }

        public int Payload02 { get; set; }

        public int Payload03 { get; set; }

        public int Payload04 { get; set; }

        public int Payload05 { get; set; }

        public int Payload06 { get; set; }

        public int Payload07 { get; set; }

        public int Payload08 { get; set; }

        public int Payload09 { get; set; }

        public int Payload10 { get; set; }

        public int Payload11 { get; set; }

        public int Payload12 { get; set; }

        public int Payload13 { get; set; }

        public int Payload14 { get; set; }

        public int Payload15 { get; set; }

        public int Payload16 { get; set; }

        public int Payload17 { get; set; }

        public int Payload18 { get; set; }

        public int Payload19 { get; set; }

        public int Payload20 { get; set; }

        public int Payload21 { get; set; }

        public int Payload22 { get; set; }

        public int Payload23 { get; set; }

        public int Payload24 { get; set; }

        public int Payload25 { get; set; }

        public int Payload26 { get; set; }

        public int Payload27 { get; set; }

        public int Payload28 { get; set; }

        public int Payload29 { get; set; }

        public static Prefix CreateSample()
        {
            return new Prefix
            {
                Payload00 = 100, Payload01 = 101, Payload02 = 102, Payload03 = 103, Payload04 = 104,
                Payload05 = 105, Payload06 = 106, Payload07 = 107, Payload08 = 108, Payload09 = 109,
                Payload10 = 110, Payload11 = 111, Payload12 = 112, Payload13 = 113, Payload14 = 114,
                Payload15 = 115, Payload16 = 116, Payload17 = 117, Payload18 = 118, Payload19 = 119,
                Payload20 = 120, Payload21 = 121, Payload22 = 122, Payload23 = 123, Payload24 = 124,
                Payload25 = 125, Payload26 = 126, Payload27 = 127, Payload28 = 128, Payload29 = 129,
            };
        }
    }

    /// <summary>
    /// BUCKET-2/4/8: два, четыре и восемь членов в <b>одной</b> корзине по
    /// длине, все имена по семь байт и все различны.
    ///
    /// Форма заведена под порог <c>KeySwitchThreshold</c> (§8.2 плана), он же
    /// единственное неизмеренное место диспетчера: известно, что при двух
    /// членах выигрывает цепочка, при тридцати - ключ, а четвёрка выбрана
    /// между ними наугад. Семь байт здесь не случайность - при такой длине
    /// ключ <b>полон</b>, и форма «по ключу» обходится вовсе без сравнения
    /// байтов, то есть порог меряется в самом чистом виде: таблица переходов
    /// против цепочки, без хвостов.
    /// </summary>
    public sealed class Bucket2
    {
        public int AlphaA1 { get; set; }

        public int AlphaB2 { get; set; }

        public static Bucket2 CreateSample() => new Bucket2 { AlphaA1 = 1, AlphaB2 = 2, };
    }

    public sealed class Bucket4
    {
        public int AlphaA1 { get; set; }

        public int AlphaB2 { get; set; }

        public int AlphaC3 { get; set; }

        public int AlphaD4 { get; set; }

        public static Bucket4 CreateSample() =>
            new Bucket4 { AlphaA1 = 1, AlphaB2 = 2, AlphaC3 = 3, AlphaD4 = 4, };
    }

    /// <summary>
    /// Восьмичленная корзина заодно служит формой для вопроса O5: свёрнутый
    /// ключ против цепочки <c>EqualsIgnoreCase</c> при включённом
    /// <c>JsonFeature.CaseInsensitiveNames</c>.
    /// </summary>
    public sealed class Bucket8
    {
        public int AlphaA1 { get; set; }

        public int AlphaB2 { get; set; }

        public int AlphaC3 { get; set; }

        public int AlphaD4 { get; set; }

        public int AlphaE5 { get; set; }

        public int AlphaF6 { get; set; }

        public int AlphaG7 { get; set; }

        public int AlphaH8 { get; set; }

        public static Bucket8 CreateSample()
        {
            return new Bucket8
            {
                AlphaA1 = 1, AlphaB2 = 2, AlphaC3 = 3, AlphaD4 = 4,
                AlphaE5 = 5, AlphaF6 = 6, AlphaG7 = 7, AlphaH8 = 8,
            };
        }
    }
}
