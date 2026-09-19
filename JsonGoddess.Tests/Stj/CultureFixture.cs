using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonGoddess.Internal;
using JsonGoddess.Tests.Core;
using Xunit;

namespace JsonGoddess.Tests.Stj
{
    /// <summary>
    /// Культура машины не имеет права влиять ни на один байт документа.
    ///
    /// <para>
    /// Тест написан не из общих соображений: расхождение уже случилось. Список
    /// недостающих обязательных имён эталон склеивает разделителем текущей
    /// культуры интерфейса, порождённый код держал его константой <c>"; "</c>,
    /// и верно это было ровно под ru-RU. Поймал прогон CI под en-US - то есть
    /// чужая машина, а не своя.
    /// </para>
    ///
    /// <para>
    /// Культуры подобраны по причинам, а не по алфавиту, и каждая отвечает за
    /// свой способ сломать (проверено пробой, <c>scratchpad/CultureProbe</c>):
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>de-DE</c>, <c>ru-RU</c> - запятая вместо точки в дробном числе;</item>
    /// <item><c>ar-SA</c>, <c>fa-IR</c> - собственный десятичный разделитель
    /// (U+066B) и метка направления письма перед знаком минуса;</item>
    /// <item><c>sv-SE</c> - минус знаком U+2212 вместо дефиса;</item>
    /// <item><c>tr-TR</c> - <c>'I'.ToLower()</c> даёт <c>'ı'</c>, а
    /// <c>'i'.ToUpper()</c> - <c>'İ'</c>: единственная культура, ломающая
    /// политики именования;</item>
    /// <item><c>ar-SA</c>, <c>th-TH</c>, <c>fa-IR</c> - другой календарь:
    /// дата без явного провайдера уезжает в хиджру или буддийскую эру;</item>
    /// <item><c>tr-TR</c>, <c>sv-SE</c>, <c>th-TH</c> - другой порядок
    /// сортировки строк по сравнителю по умолчанию.</item>
    /// </list>
    ///
    /// <para>
    /// Вторая ось - письменность в самих данных: латиница, кириллица, арабская
    /// вязь (справа налево), иероглифы и кана, плюс символ за пределами BMP.
    /// Оси независимы и проверяются вместе: культура <c>tr-TR</c> на
    /// иероглифах и культура <c>ja-JP</c> на латинице - разные вопросы.
    /// </para>
    /// </summary>
    public class CultureFixture
    {
        /// <summary>
        /// Пустая строка - инвариантная культура; она же эталон сравнения для
        /// всех остальных.
        /// </summary>
        public static readonly string[] Cultures =
        {
            "", "en-US", "ru-RU", "de-DE", "tr-TR", "ar-SA", "fa-IR", "sv-SE", "th-TH", "ja-JP", "zh-CN",
        };

        public static IEnumerable<object[]> EveryCulture => Cultures.Select(name => new object[] { name, });

        /// <summary>
        /// Контроль самого стенда. Все тесты ниже зелёные, и это значит либо
        /// «культура не протекает», либо «подмена культуры не работает» - два
        /// совершенно разных утверждения под одним цветом. Здесь проверяется
        /// второе: под подменённой культурой BCL обязан вести себя иначе.
        ///
        /// Без этого теста достаточно было бы опечатки в
        /// <see cref="Under{T}"/>, чтобы весь набор превратился в
        /// одиннадцать одинаковых прогонов под культурой разработчика.
        /// </summary>
        [Fact]
        public void The_harness_really_changes_the_culture()
        {
            var invariant = Under(CultureInfo.InvariantCulture, () => (-1.5).ToString());
            var german = Under(new CultureInfo("de-DE"), () => (-1.5).ToString());
            var arabic = Under(new CultureInfo("ar-SA"), () => new DateTime(2026, 9, 19).ToString());

            Assert.Equal("-1.5", invariant);
            Assert.Equal("-1,5", german);

            //ar-SA считает годы по хиджре, и 2026 в дате не встретится
            Assert.DoesNotContain("2026", arabic, StringComparison.Ordinal);

            //Знак минуса здесь не проверяется намеренно, хотя ради него и
            //делалась правка в генераторе: под .NET данные о культурах берутся
            //из ICU и sv-SE пишет U+2212, а под .NET Framework - из NLS
            //системы, и там обычный дефис. Это не мелочь, а лишний довод не
            //зависеть от культуры нигде: её данные разные не только по машинам,
            //но и по рантаймам на одной машине.

            //культура интерфейса подменяется вместе с культурой форматирования
            Assert.Equal(", ", Under(CultureInfo.InvariantCulture, () => JsonRequiredNames.Separator));
            Assert.Equal("; ", Under(new CultureInfo("ru-RU"), () => JsonRequiredNames.Separator));

            //и возвращается обратно
            Assert.Equal(CultureInfo.CurrentUICulture.TextInfo.ListSeparator + " ", JsonRequiredNames.Separator);
        }

        /// <summary>
        /// Документ, написанный нами, совпадает с эталонным - под каждой
        /// культурой, и совпадает при этом сам с собой: культура не имеет
        /// права изменить ни одного байта.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void The_document_does_not_depend_on_the_culture(string culture)
        {
            var invariant = Under(CultureInfo.InvariantCulture, () => WriteOurs(Multilingual.CreateSample()));

            Under(
                Culture(culture),
                () =>
                {
                    var sample = Multilingual.CreateSample();

                    var ours = WriteOurs(sample);
                    var theirs = Reference.Write(sample);

                    Assert.Equal(theirs, ours);
                    Assert.Equal(invariant, ours);
                    return 0;
                });
        }

        /// <summary>
        /// Чтение - тоже. Сравнивается не с исходным объектом, а с тем, что
        /// прочёл эталон: симметричная ошибка писателя и читателя иначе
        /// погасила бы сама себя.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void Reading_does_not_depend_on_the_culture(string culture)
        {
            var utf8 = Encoding.UTF8.GetBytes(
                Under(CultureInfo.InvariantCulture, () => Reference.Write(Multilingual.CreateSample()))
                );

            Under(
                Culture(culture),
                () =>
                {
                    var theirs = JsonSerializer.Deserialize<Multilingual>(utf8, Reference.Relaxed);
                    MultilingualSerializer.Deserialize(DefaultInjector.Instance, utf8, out var ours);

                    Assert.Equal(Reference.Write(theirs), Reference.Write(ours));
                    return 0;
                });
        }

        /// <summary>
        /// Символы вне BMP с эталоном сравнивать нельзя - там объявленное
        /// расхождение (строки 1.2-1.3), - но вопрос культуры к ним всё равно
        /// стоит: четыре байта UTF-8 обязаны получиться одни и те же и под
        /// tr-TR, и под ar-SA. Сравнивается наш документ сам с собой.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void Characters_above_the_basic_plane_do_not_depend_on_the_culture(string culture)
        {
            var sample = Multilingual.CreateSample();
            sample.Astral = "😀 𝄞 🈚 𐍈";

            var invariant = Under(CultureInfo.InvariantCulture, () => WriteOurs(sample));

            Under(
                Culture(culture),
                () =>
                {
                    Assert.Equal(invariant, WriteOurs(sample));
                    return 0;
                });
        }

        /// <summary>
        /// Политики именования на всём корпусе имён - под каждой культурой.
        ///
        /// Это место, где ломается <c>tr-TR</c>: <c>ID</c> в camelCase обязан
        /// дать <c>id</c>, а не <c>ıd</c>. Свёртка регистра у нас
        /// инвариантная, и тест требует, чтобы такой же она была у эталона -
        /// утверждение снимается с него, а не пишется рукой.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void Naming_policies_agree_with_the_reference_under_every_culture(string culture)
        {
            Under(
                Culture(culture),
                () =>
                {
                    foreach (var name in JsonNamingFixture.Corpus.Concat(ScriptNames))
                    {
                        foreach (var style in Styles)
                        {
                            Assert.Equal(Theirs(style).ConvertName(name), JsonNaming.Convert(name, style));
                        }
                    }

                    return 0;
                });
        }

        /// <summary>
        /// Политика имён членов применяется на <b>компиляции</b>, а ключей
        /// словаря - в <b>рантайме</b>. Второе и проверяется здесь: имя члена
        /// уже напечатано генератором и культуре недоступно, а ключ
        /// преобразуется тем же кодом, но на машине потребителя.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void Dictionary_keys_are_converted_the_way_the_reference_converts_them(string culture)
        {
            var invariant = Under(CultureInfo.InvariantCulture, () => WriteSnake(Multilingual.CreateSample()));

            Under(
                Culture(culture),
                () =>
                {
                    var sample = Multilingual.CreateSample();

                    var options = new JsonSerializerOptions(Reference.Relaxed)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                    };

                    var ours = WriteSnake(sample);

                    Assert.Equal(JsonSerializer.Serialize(sample, options), ours);
                    Assert.Equal(invariant, ours);
                    return 0;
                });
        }

        /// <summary>
        /// Сообщение об отказе - тоже документ, который увидит чужой код
        /// (§10), и оно обязано совпадать с эталонным под любой культурой.
        /// Именно это и разошлось.
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryCulture))]
        public void The_refusal_wording_agrees_with_the_reference_under_every_culture(string culture)
        {
            Under(
                Culture(culture),
                () =>
                {
                    var utf8 = Encoding.UTF8.GetBytes("{}");

                    var theirs = Assert.Throws<JsonException>(
                        () => JsonSerializer.Deserialize<CultureDemanding>(utf8, Reference.Relaxed)
                        );

                    var ours = Assert.Throws<JsonDocumentException>(
                        () =>
                        {
                            CultureDemandingSerializer.Deserialize(DefaultInjector.Instance, utf8, out _);
                        });

                    var expected = string.Join(
                        JsonRequiredNames.Separator,
                        new[] { "Latin", "Кириллица", "漢字", }.Select(name => "'" + name + "'")
                        );

                    Assert.Contains(expected, theirs.Message, StringComparison.Ordinal);
                    Assert.Contains(expected, ours.Message, StringComparison.Ordinal);
                    return 0;
                });
        }

        private static readonly JsonNamingStyle[] Styles =
        {
            JsonNamingStyle.CamelCase,
            JsonNamingStyle.SnakeCaseLower,
            JsonNamingStyle.SnakeCaseUpper,
            JsonNamingStyle.KebabCaseLower,
            JsonNamingStyle.KebabCaseUpper,
        };

        /// <summary>
        /// Имена в четырёх письменностях - в дополнение к латинскому корпусу
        /// <see cref="JsonNamingFixture.Corpus"/>. Смысл их здесь не в
        /// экзотике: политика именования ищет границы слов по регистру, а у
        /// иероглифов и арабской вязи регистра нет вовсе, у кириллицы он есть,
        /// и вести себя на них эталон обязан одинаково с нами.
        /// </summary>
        private static readonly string[] ScriptNames =
        {
            "Имя", "ИмяДва", "ИМЯ", "ЁжикID",
            "漢字", "漢字Name", "日本語ID", "ひらがな",
            "نص", "نصID", "العربية",
            "MixedКириллица漢字", "IDنص",
        };

        private static JsonNamingPolicy Theirs(JsonNamingStyle style)
        {
            switch (style)
            {
                case JsonNamingStyle.CamelCase:
                    return JsonNamingPolicy.CamelCase;

                case JsonNamingStyle.SnakeCaseLower:
                    return JsonNamingPolicy.SnakeCaseLower;

                case JsonNamingStyle.SnakeCaseUpper:
                    return JsonNamingPolicy.SnakeCaseUpper;

                case JsonNamingStyle.KebabCaseLower:
                    return JsonNamingPolicy.KebabCaseLower;

                default:
                    return JsonNamingPolicy.KebabCaseUpper;
            }
        }

        private static string WriteOurs(Multilingual value)
        {
            using var exhauster = new PooledUtf8Exhauster();
            MultilingualSerializer.Serialize(exhauster, value);
            return Encoding.UTF8.GetString(exhauster.ToArray());
        }

        private static string WriteSnake(Multilingual value)
        {
            using var exhauster = new PooledUtf8Exhauster();
            MultilingualSnakeSerializer.Serialize(exhauster, value);
            return Encoding.UTF8.GetString(exhauster.ToArray());
        }

        private static CultureInfo Culture(string name)
        {
            return name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name);
        }

        /// <summary>
        /// Подменяются <b>обе</b> культуры: форматирование отвечает за числа и
        /// даты, культура интерфейса - за разделитель списка в сообщении об
        /// отказе. Возвращается прежняя пара в <c>finally</c>, иначе один
        /// упавший тест испортил бы все последующие на том же потоке.
        /// </summary>
        private static T Under<T>(CultureInfo culture, Func<T> action)
        {
            var formatting = CultureInfo.CurrentCulture;
            var ui = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                return action();
            }
            finally
            {
                CultureInfo.CurrentCulture = formatting;
                CultureInfo.CurrentUICulture = ui;
            }
        }
    }

    /// <summary>
    /// Данные в четырёх письменностях и числа всех лексических семейств, на
    /// которых культура способна проявиться: отрицательное целое (знак
    /// минуса), дробные <c>double</c> и <c>decimal</c> (десятичный
    /// разделитель), дата и смещение (календарь), интервал и GUID.
    /// </summary>
    public class Multilingual
    {
        public string? Latin { get; set; }

        public string? Кириллица { get; set; }

        public string? 漢字 { get; set; }

        [JsonPropertyName("العربية")]
        public string? Arabic { get; set; }

        /// <summary>
        /// Редкие символы <b>внутри</b> BMP. За его пределы этот образец не
        /// выходит намеренно: символы вне BMP - задокументированное расхождение
        /// с эталоном (<c>docs/stj-divergences.md</c>, строки 1.2-1.3: даже
        /// расслабленный энкодер разворачивает их суррогатной парой, мы пишем
        /// четыре байта UTF-8), и переоткрывать здесь этот спор незачем -
        /// к культуре он отношения не имеет. Культурная независимость самих
        /// астральных символов проверяется отдельно,
        /// <see cref="CultureFixture.Characters_above_the_basic_plane_do_not_depend_on_the_culture"/>.
        /// </summary>
        public string? Astral { get; set; }

        public int Negative { get; set; }

        public long BigNegative { get; set; }

        public double Fraction { get; set; }

        public float Single { get; set; }

        public decimal Money { get; set; }

        public DateTime Moment { get; set; }

        public DateTimeOffset Zoned { get; set; }

        public TimeSpan Duration { get; set; }

        public Guid Reference { get; set; }

        public Script Script { get; set; }

        public Dictionary<string, int>? Counts { get; set; }

        public static Multilingual CreateSample()
        {
            return new Multilingual
            {
                Latin = "Grossenwahn, naive cafe - Ljubljana",
                Кириллица = "Ёжик в тумане, щёлочь, Ъ и Ь",
                漢字 = "日本語と中国語、ひらがな、カタカナ",
                Arabic = "مرحبا بالعالم",
                Astral = "ﬀ ⅷ ℘",
                Negative = -42,
                BigNegative = -9223372036854775808,
                Fraction = -1234.5678,
                Single = -0.5f,
                Money = -9876.5432m,
                Moment = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc),
                Zoned = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(3)),
                Duration = TimeSpan.FromMinutes(-90),
                Reference = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
                Script = Script.Han,
                Counts = new Dictionary<string, int>
                {
                    { "PlainKey", 1 },
                    { "КлючДва", 2 },
                    { "鍵三", 3 },
                    { "مفتاح", 4 },
                    { "IDKey", 5 },
                },
            };
        }
    }

    public enum Script
    {
        Latin = 0,
        Cyrillic = 1,
        Arabic = 2,
        Han = 3,
    }

    /// <summary>
    /// Обязательные члены в трёх письменностях: список недостающих имён и есть
    /// то место, где культура однажды уже развела нас с эталоном.
    /// </summary>
    public class CultureDemanding
    {
        public required string Latin { get; set; }

        public required string Кириллица { get; set; }

        public required string 漢字 { get; set; }
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Multilingual), true)]
    public partial class MultilingualSerializer
    {
    }

    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(Multilingual), true)]
    public partial class MultilingualSnakeSerializer
    {
    }

    [JsonExhauster(typeof(PooledUtf8Exhauster))]
    [JsonInjector(typeof(DefaultInjector))]
    [JsonSubject(typeof(CultureDemanding), true)]
    public partial class CultureDemandingSerializer
    {
    }
}
