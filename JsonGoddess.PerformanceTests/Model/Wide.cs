namespace JsonGoddess.PerformanceTests.Model
{
    /// <summary>
    /// WIDE: объект на 30 членов. Формы, на которой видно сопоставление
    /// имён, в бенчмарках XmlSerDe не было вовсе - в XML вокруг имени
    /// много другой работы, и квадрат по числу членов там тонет в фоне.
    ///
    /// Имена одинаковой длины намеренно: switch по длине на таком объекте
    /// не отсекает ничего. Ровно семь байт - тоже намеренно: это верхняя
    /// граница, на которой ключ имени ещё полон и сравнение байтов не
    /// нужно вовсе.
    /// </summary>
    public sealed class Wide
    {
        public int Field00 { get; set; }

        public string? Field01 { get; set; }

        public int Field02 { get; set; }

        public string? Field03 { get; set; }

        public int Field04 { get; set; }

        public string? Field05 { get; set; }

        public int Field06 { get; set; }

        public string? Field07 { get; set; }

        public int Field08 { get; set; }

        public string? Field09 { get; set; }

        public int Field10 { get; set; }

        public string? Field11 { get; set; }

        public int Field12 { get; set; }

        public string? Field13 { get; set; }

        public int Field14 { get; set; }

        public string? Field15 { get; set; }

        public int Field16 { get; set; }

        public string? Field17 { get; set; }

        public int Field18 { get; set; }

        public string? Field19 { get; set; }

        public int Field20 { get; set; }

        public string? Field21 { get; set; }

        public int Field22 { get; set; }

        public string? Field23 { get; set; }

        public int Field24 { get; set; }

        public string? Field25 { get; set; }

        public int Field26 { get; set; }

        public string? Field27 { get; set; }

        public int Field28 { get; set; }

        public string? Field29 { get; set; }

        public static Wide CreateSample()
        {
            return new Wide
            {
                Field00 = 1000,
                Field01 = "value of field 01",
                Field02 = 1074,
                Field03 = "value of field 03",
                Field04 = 1148,
                Field05 = "value of field 05",
                Field06 = 1222,
                Field07 = "value of field 07",
                Field08 = 1296,
                Field09 = "value of field 09",
                Field10 = 1370,
                Field11 = "value of field 11",
                Field12 = 1444,
                Field13 = "value of field 13",
                Field14 = 1518,
                Field15 = "value of field 15",
                Field16 = 1592,
                Field17 = "value of field 17",
                Field18 = 1666,
                Field19 = "value of field 19",
                Field20 = 1740,
                Field21 = "value of field 21",
                Field22 = 1814,
                Field23 = "value of field 23",
                Field24 = 1888,
                Field25 = "value of field 25",
                Field26 = 1962,
                Field27 = "value of field 27",
                Field28 = 2036,
                Field29 = "value of field 29",
            };
        }
    }
}
