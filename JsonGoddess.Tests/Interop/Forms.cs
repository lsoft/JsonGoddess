using System;
using System.Collections.Generic;
using JsonGoddess.Tests.Generated;

namespace JsonGoddess.Tests.Interop
{
    /// <summary>
    /// Каталог форм. Матрица строится по тому, <b>что в документе может пойти
    /// не так</b>, а не по тому, какие POCO уже написаны: скаляры и их границы,
    /// строки и экранирование, лексика дат, строение (вложенность, коллекции,
    /// словари, рекурсия, наследование), состав (переименование, пропуск,
    /// условная запись, порядок, поля) и enum'ы в обоих режимах.
    ///
    /// Большинство форм переиспользует субъектов, написанных под отдельные
    /// утверждения: каждый из них уже описывает одну особенность, и прогнать их
    /// ещё и во всех направлениях стоит дёшево. Новые POCO добавлены только под
    /// дыры матрицы.
    /// </summary>
    public static class Forms
    {
        public static IReadOnlyList<InteropForm> All { get; } = Build();

        private static IReadOnlyList<InteropForm> Build()
        {
            return new InteropForm[]
            {
                //скаляры
                Form("flat/all-builtins", "все builtin-типы разом, значения заданы",
                    Flat.CreateSample(), FlatSerializer.Serialize, ReadFlat),
                Form("flat/defaults", "все builtin-типы на умолчаниях",
                    new Flat(), FlatSerializer.Serialize, ReadFlat),
                Form("flat/nulls", "ссылочные и nullable-члены равны null",
                    new Flat { Customer = null, Payload = null, MaybeCount = null, MaybeReference = null, MaybeMoment = null, },
                    FlatSerializer.Serialize, ReadFlat),
                Form("extremes/numeric", "границы целых, decimal и обратимость float/double",
                    Extremes.CreateSample(), InteropSerializer.Serialize, ReadExtremes),

                //строки
                Form("escapes/strings", "кавычка, обратная косая, управляющие, HTML-значимые, не-ASCII",
                    Escapes.CreateSample(), InteropSerializer.Serialize, ReadEscapes),
                Form("escapes/outside-bmp", "символ вне BMP в значении",
                    new Escapes { Astral = "смайл \U0001F600 и ещё \U0001F4A9", },
                    InteropSerializer.Serialize, ReadEscapes, OutsideBmp),
                Form("escapes/empty", "пустые строки и null рядом",
                    new Escapes { Empty = string.Empty, Quote = string.Empty, }, InteropSerializer.Serialize, ReadEscapes),

                //лексика дат
                Form("moments/kinds", "DateTimeKind, смещения, границы TimeSpan и Guid",
                    Moments.CreateSample(), InteropSerializer.Serialize, ReadMoments),
                Form("moments/defaults", "даты и длительности на умолчаниях",
                    new Moments(), InteropSerializer.Serialize, ReadMoments),

                //диспетчер имён
                Form("wide/thirty-names", "имена одной длины: switch по ключу",
                    Wide.CreateSample(), WideSerializer.Serialize, ReadWide),
                Form("colliding/shared-key", "имена с общим ключом: одна ветка, разделение сравнением",
                    Colliding.CreateSample(), CollidingSerializer.Serialize, ReadColliding),
                Form("unicode/non-ascii-name", "имя свойства вне ASCII",
                    Unicode.CreateSample(), UnicodeSerializer.Serialize, ReadUnicode),
                Form("astral/name-outside-bmp", "имя свойства вне BMP",
                    new Astral { Name = "Ada", }, AstralSerializer.Serialize, ReadAstral, OutsideBmp),

                //строение
                Form("basket/full", "вложенный субъект, коллекции субъектов, массив, вложенная коллекция",
                    Basket.CreateSample(), BasketSerializer.Serialize, ReadBasket),
                Form("basket/empty-collections", "пустые коллекции всех видов",
                    new Basket
                    {
                        Lines = new List<Item>(),
                        Numbers = new int[0],
                        Tags = new List<string>(),
                        Matrix = new List<List<int>>(),
                    },
                    BasketSerializer.Serialize, ReadBasket),
                Form("basket/null-collections", "null на месте коллекций и вложенного субъекта",
                    new Basket(), BasketSerializer.Serialize, ReadBasket),
                Form("basket/null-elements", "null элементом коллекции",
                    new Basket
                    {
                        Lines = new List<Item> { null!, },
                        Tags = new List<string> { null!, },
                        Matrix = new List<List<int>> { null!, new List<int>(), },
                    },
                    BasketSerializer.Serialize, ReadBasket),
                Form("node/recursive", "тип, ссылающийся сам на себя через коллекцию",
                    Node.CreateSample(), NodeSerializer.Serialize, ReadNode),
                Form("node/leaf", "тот же тип без единого потомка",
                    new Node { Id = 1, }, NodeSerializer.Serialize, ReadNode),
                Form("derived/three-levels", "три уровня наследования: порядок членов",
                    DerivedEntity.CreateSample(), DerivedEntitySerializer.Serialize, ReadDerived),

                //словари
                Form("catalogue/full", "словари скаляров, субъектов, коллекций и словарей",
                    Catalogue.CreateSample(), CatalogueSerializer.Serialize, ReadCatalogue),
                Form("catalogue/empty-maps", "пустые словари",
                    new Catalogue
                    {
                        Counts = new Dictionary<string, int>(),
                        Items = new Dictionary<string, Item>(),
                        Series = new Dictionary<string, List<int>>(),
                        Nested = new Dictionary<string, Dictionary<string, string>>(),
                    },
                    CatalogueSerializer.Serialize, ReadCatalogue),
                Form("catalogue/null-maps", "null на месте словарей",
                    new Catalogue(), CatalogueSerializer.Serialize, ReadCatalogue),
                Form("catalogue/awkward-keys", "ключ с кавычкой, с табуляцией, кириллический и пустой",
                    new Catalogue
                    {
                        Counts = new Dictionary<string, int>
                        {
                            { "a\"b", 1 }, { "tab\there", 2 }, { "имя", 3 }, { string.Empty, 4 },
                        },
                    },
                    CatalogueSerializer.Serialize, ReadCatalogue),

                //состав
                Form("renamed/property-names", "[JsonPropertyName] и [JsonIgnore]",
                    Renamed.CreateSample(), RenamedSerializer.Serialize, ReadRenamed),
                Form("fixed/getter-only", "член без setter'а: пишется, но не читается",
                    new Fixed(), FixedSerializer.Serialize, ReadFixed),
                Form("fields/json-include", "поля попадают в документ только с [JsonInclude]",
                    Fields.CreateSample(), InteropSerializer.Serialize, ReadFields),
                Form("sparse/all-default", "условные члены на умолчаниях: остаются только безусловные",
                    Sparse.CreateEmpty(), SparseSerializer.Serialize, ReadSparse),
                Form("sparse/all-set", "условные члены заполнены: пишутся все",
                    Sparse.CreateFull(), SparseSerializer.Serialize, ReadSparse),
                Form("all-sparse/empty", "все члены условные и все опущены: пустой объект",
                    new AllSparse(), SparseSerializer.Serialize, ReadAllSparse),
                Form("all-sparse/partial", "все члены условные, написан один",
                    new AllSparse { B = "b", }, SparseSerializer.Serialize, ReadAllSparse),
                Form("ordered/explicit", "[JsonPropertyOrder] поверх наследования",
                    new OrderedDerived { BaseA = 1, BaseEarly = 2, DerivedA = 3, Late = 4, LateToo = 5, },
                    SparseSerializer.Serialize, ReadOrdered),

                //политика именования
                Form("naming/camel-case", "имена членов по camelCase, [JsonPropertyName] сильнее политики",
                    Named.CreateSample(), CamelSerializer.Serialize, ReadCamel,
                    naming: System.Text.Json.JsonNamingPolicy.CamelCase),
                Form("naming/snake-case", "имена членов и ключи словаря по snake_case",
                    Named.CreateSample(), SnakeSerializer.Serialize, ReadSnake,
                    naming: System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                    dictionaryKeys: System.Text.Json.JsonNamingPolicy.SnakeCaseLower),

                //enum'ы
                Form("marks/enums-full", "enum числом и именем, в коллекции и в словаре",
                    Marks.CreateSample(), MarksSerializer.Serialize, ReadMarks),
                Form("marks/enums-default", "enum'ы на умолчаниях",
                    new Marks(), MarksSerializer.Serialize, ReadMarks),
                Form("marks/enum-out-of-range", "значение вне набора: числом в обоих режимах",
                    new Marks { Plain = (Status)999, Mode = (Mode)77, }, MarksSerializer.Serialize, ReadMarks),
            };
        }

        private static InteropForm Form<T>(
            string name,
            string what,
            T sample,
            FormWriter<T> write,
            FormReader<T> read,
            string? divergence = null,
            System.Text.Json.JsonNamingPolicy? naming = null,
            System.Text.Json.JsonNamingPolicy? dictionaryKeys = null
            )
            where T : class
        {
            return new InteropForm<T>(name, what, sample, write, read, divergence, naming, dictionaryKeys);
        }

        /// <summary>
        /// Расхождение, закреплённое <c>EscapingDivergenceFixture</c>: список
        /// разрешённого даже у расслабленного энкодера эталона - это
        /// <c>UnicodeRanges.All</c>, то есть базовая плоскость, до U+FFFF.
        /// Эмодзи у него уезжает двенадцатью байтами ASCII, у нас - четырьмя
        /// байтами UTF-8. Значение при этом то же самое, и остальные три
        /// направления обязаны остаться зелёными.
        /// </summary>
        private const string OutsideBmp =
            "эталон экранирует символы вне BMP суррогатной парой даже расслабленным энкодером";

        //Читатели расписаны по одному на тип, а не сведены в обобщённый: точка
        //входа отдаёт результат через out, и обобщить её, не заведя рефлексию,
        //нельзя - а рефлексия здесь ровно то, чего вся библиотека избегает.
        private static Flat? ReadFlat(ReadOnlySpan<byte> json)
        {
            FlatSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Wide? ReadWide(ReadOnlySpan<byte> json)
        {
            WideSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Colliding? ReadColliding(ReadOnlySpan<byte> json)
        {
            CollidingSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Unicode? ReadUnicode(ReadOnlySpan<byte> json)
        {
            UnicodeSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Astral? ReadAstral(ReadOnlySpan<byte> json)
        {
            AstralSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Renamed? ReadRenamed(ReadOnlySpan<byte> json)
        {
            RenamedSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Basket? ReadBasket(ReadOnlySpan<byte> json)
        {
            BasketSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Node? ReadNode(ReadOnlySpan<byte> json)
        {
            NodeSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static DerivedEntity? ReadDerived(ReadOnlySpan<byte> json)
        {
            DerivedEntitySerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Catalogue? ReadCatalogue(ReadOnlySpan<byte> json)
        {
            CatalogueSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Fixed? ReadFixed(ReadOnlySpan<byte> json)
        {
            FixedSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Marks? ReadMarks(ReadOnlySpan<byte> json)
        {
            MarksSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Sparse? ReadSparse(ReadOnlySpan<byte> json)
        {
            SparseSerializer.Deserialize(DefaultInjector.Instance, json, out Sparse? result);
            return result;
        }

        private static AllSparse? ReadAllSparse(ReadOnlySpan<byte> json)
        {
            SparseSerializer.Deserialize(DefaultInjector.Instance, json, out AllSparse? result);
            return result;
        }

        private static OrderedDerived? ReadOrdered(ReadOnlySpan<byte> json)
        {
            SparseSerializer.Deserialize(DefaultInjector.Instance, json, out OrderedDerived? result);
            return result;
        }

        private static Extremes? ReadExtremes(ReadOnlySpan<byte> json)
        {
            InteropSerializer.Deserialize(DefaultInjector.Instance, json, out Extremes? result);
            return result;
        }

        private static Escapes? ReadEscapes(ReadOnlySpan<byte> json)
        {
            InteropSerializer.Deserialize(DefaultInjector.Instance, json, out Escapes? result);
            return result;
        }

        private static Moments? ReadMoments(ReadOnlySpan<byte> json)
        {
            InteropSerializer.Deserialize(DefaultInjector.Instance, json, out Moments? result);
            return result;
        }

        private static Named? ReadCamel(ReadOnlySpan<byte> json)
        {
            CamelSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Named? ReadSnake(ReadOnlySpan<byte> json)
        {
            SnakeSerializer.Deserialize(DefaultInjector.Instance, json, out var result);
            return result;
        }

        private static Fields? ReadFields(ReadOnlySpan<byte> json)
        {
            InteropSerializer.Deserialize(DefaultInjector.Instance, json, out Fields? result);
            return result;
        }
    }
}
