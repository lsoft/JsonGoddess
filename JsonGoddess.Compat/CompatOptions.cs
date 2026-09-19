using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JsonGoddess.Compat
{
    /// <summary>
    /// «Эти <see cref="JsonSerializerOptions"/> означают то же, что их
    /// отсутствие?» - единственный вопрос, который фасад задаёт про опции.
    ///
    /// <para>
    /// Ответ нужен потому, что быстрый путь настроек не читает вовсе: он
    /// порождён под <b>одно</b> поведение, и подсунуть ему чужие опции значит
    /// выдать валидный документ, отличающийся от эталонного. Это худший исход
    /// по принципу 1 плана, поэтому правило здесь одностороннее: <b>сомнение -
    /// это «нет»</b>, и работа уходит настоящему <c>System.Text.Json</c>.
    /// </para>
    ///
    /// <para>
    /// Сравнение идёт <b>рефлексией по всем свойствам</b>, а не списком тех,
    /// которые я счёл важными, и это осознанный размен. Список пришлось бы
    /// дописывать руками при каждом новом свойстве эталона, а <b>забытое</b>
    /// свойство означает не отказ, а тихо взятый быстрый путь на чужих
    /// настройках - то самое расхождение документов. Рефлексия ошибается в
    /// безопасную сторону: незнакомое свойство, отличающееся от умолчания,
    /// само собой даёт «нет».
    /// </para>
    ///
    /// <para>
    /// Цена рефлексии платится один раз на экземпляр: у эталона опции после
    /// первого использования становятся <see cref="JsonSerializerOptions.IsReadOnly"/>,
    /// и с этого момента вердикт про них неизменен - его и кешируем. Самый
    /// частый случай drop-in'а - <c>options == null</c> - не стоит ничего
    /// вовсе.
    /// </para>
    /// </summary>
    public static class CompatOptions
    {
        /// <summary>
        /// Свежий экземпляр - эталон умолчаний. Он никогда не используется для
        /// разбора, поэтому так и остаётся не-readonly и не обзаводится
        /// резолвером.
        /// </summary>
        private static readonly JsonSerializerOptions Fresh = new JsonSerializerOptions();

        /// <summary>
        /// Свойства, которые сравниваются «в лоб». Три исключены не по
        /// снисходительности, а потому что у них нет одного правильного
        /// значения:
        ///
        /// <list type="bullet">
        /// <item><c>IsReadOnly</c> - меняется от самого факта использования;</item>
        /// <item><c>TypeInfoResolver</c>/<c>TypeInfoResolverChain</c> - у свежего
        /// экземпляра пусты, а у использованного заполняются резолвером по
        /// умолчанию, и «пусто» с «умолчанием» сравнивать нечем;</item>
        /// <item><c>Converters</c> - список, и <c>Equals</c> на нём отвечает
        /// про ссылку, а не про содержимое.</item>
        /// </list>
        ///
        /// Все четыре проверяются отдельно, ниже.
        /// </summary>
        private static readonly PropertyInfo[] Compared = typeof(JsonSerializerOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !IsHandledSeparately(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        private static readonly ConditionalWeakTable<JsonSerializerOptions, object> Verdicts =
            new ConditionalWeakTable<JsonSerializerOptions, object>();

        private static readonly object Yes = true;
        private static readonly object No = false;

        private static bool IsHandledSeparately(string name)
        {
            return name == nameof(JsonSerializerOptions.IsReadOnly)
                || name == nameof(JsonSerializerOptions.TypeInfoResolver)
                || name == "TypeInfoResolverChain"
                || name == nameof(JsonSerializerOptions.Converters)
                || name == nameof(JsonSerializerOptions.Encoder);
        }

        /// <summary>
        /// Энкодер: <c>null</c> либо тот самый, который эталон подставляет
        /// вместо <c>null</c> сам.
        ///
        /// <para>
        /// Сравнение «в лоб» здесь давало ложный отказ, и не в теории:
        /// minimal API выставляет <c>Encoder</c> явно в
        /// <c>JavaScriptEncoder.Default</c> - то есть ровно в умолчание, - а
        /// «объект против <c>null</c>» даёт «не равно». Поведение при этом
        /// одно и то же, и <c>CompatUtf8Exhauster</c> писался именно под него.
        /// </para>
        ///
        /// <para>
        /// Послабление узкое нарочно: засчитывается <b>только</b> этот
        /// экземпляр. Любой другой энкодер - включая
        /// <c>UnsafeRelaxedJsonEscaping</c>, который отличается от умолчания
        /// набором экранируемого, - по-прежнему означает «нет».
        /// </para>
        /// </summary>
        private static bool EncoderIsDefault(JsonSerializerOptions options)
        {
            return options.Encoder is null || ReferenceEquals(options.Encoder, JavaScriptEncoder.Default);
        }

        /// <summary>
        /// Сколько свойств участвует в сравнении. Существует ради теста: он
        /// сверяет это число с тем, что видит у эталона сам, - иначе
        /// «сравниваем всё» осталось бы обещанием в комментарии.
        /// </summary>
        public static int ComparedPropertyCount => Compared.Length;

        public static bool IsDefault(JsonSerializerOptions? options)
        {
            //самый частый случай drop-in'а, и он обязан быть бесплатным
            if (options is null)
            {
                return true;
            }

            if (ReferenceEquals(options, Fresh) || ReferenceEquals(options, JsonSerializerOptions.Default))
            {
                return true;
            }

            if (Verdicts.TryGetValue(options, out var cached))
            {
                return (bool)cached;
            }

            var verdict = Compute(options);

            //кешируем только то, что больше не изменится: у не-readonly
            //экземпляра владелец вправе переставить свойство между вызовами, и
            //запомненный вердикт стал бы враньём
            if (options.IsReadOnly)
            {
                try
                {
                    Verdicts.Add(options, verdict ? Yes : No);
                }
                catch (ArgumentException)
                {
                    //кто-то успел раньше; вердикт у него тот же
                }
            }

            return verdict;
        }

        /// <summary>
        /// То же самое, но без вопроса о резолвере: мост (§10, маршрут B)
        /// свой резолвер в эти опции и положил, и требовать от них
        /// умолчательного значило бы требовать, чтобы мост не был установлен.
        ///
        /// <para>
        /// Всё остальное спрашивается ровно как у фасада, и по той же причине:
        /// порождённый код читает не опции, а одно поведение, под которое
        /// напечатан. Конвертеры при этом по-прежнему запрещены - чужой
        /// конвертер мог бы отвечать за тип <b>внутри</b> нашего объекта,
        /// который мы пишем целиком и про который его не спросим.
        /// </para>
        ///
        /// <para>
        /// Вердикт кешируется отдельным столбцом: у одних и тех же опций два
        /// вопроса дают разные ответы, и путать их нельзя.
        /// </para>
        /// </summary>
        public static bool IsDefaultApartFromTheResolver(JsonSerializerOptions? options)
        {
            if (options is null)
            {
                return true;
            }

            if (BridgeVerdicts.TryGetValue(options, out var cached))
            {
                return (bool)cached;
            }

            var verdict = ComputeApartFromTheResolver(options);

            if (options.IsReadOnly)
            {
                try
                {
                    BridgeVerdicts.Add(options, verdict ? Yes : No);
                }
                catch (ArgumentException)
                {
                    //кто-то успел раньше; вердикт у него тот же
                }
            }

            return verdict;
        }

        private static readonly ConditionalWeakTable<JsonSerializerOptions, object> BridgeVerdicts =
            new ConditionalWeakTable<JsonSerializerOptions, object>();

        /// <summary>
        /// Эталон веб-профиля: то, что строят ASP.NET Core MVC и minimal API.
        /// </summary>
        private static readonly JsonSerializerOptions FreshWeb =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private static readonly ConditionalWeakTable<JsonSerializerOptions, object> WebVerdicts =
            new ConditionalWeakTable<JsonSerializerOptions, object>();

        /// <summary>
        /// Эти опции - веб-профиль и ничего сверх него?
        ///
        /// <para>
        /// Вопрос тот же, что и у <see cref="IsDefaultApartFromTheResolver"/>,
        /// только образцом служит <c>JsonSerializerDefaults.Web</c>. Нужен он
        /// затем, что ASP.NET Core строит опции именно так, и без этой ветки
        /// мост в вебе не включался бы вовсе - то есть молчал бы ровно там,
        /// ради чего строился.
        /// </para>
        ///
        /// <para>
        /// <c>MaxDepth</c> из сравнения исключён намеренно, и это не
        /// послабление. MVC выставляет 32, minimal API оставляет умолчание, и
        /// требуй мы совпадения - пришлось бы держать два веб-варианта
        /// порождённого кода. Держать их незачем: глубину проверяет
        /// <c>Utf8JsonReader</c>, который создаёт эталон по своим же опциям, а
        /// не мы. Это ровно то же основание, по которому в мосте нет ни одного
        /// нашего стража.
        /// </para>
        ///
        /// <para>
        /// <c>Encoder</c> не спрашивается <b>вовсе</b>, и это не послабление
        /// тоже. Раковина веб-профиля - <c>EncoderUtf8Exhauster</c> - не несёт
        /// своего набора экранируемого: она зовёт тот самый энкодер, который
        /// лежит в этих опциях. Спрашивать «тот ли у вас энкодер» значило бы
        /// спрашивать у себя же. Именно этот вопрос и делал мост бесполезным в
        /// вебе: MVC отдаёт форматтеру <b>копию</b> опций с подменённым
        /// <c>UnsafeRelaxedJsonEscaping</c>, и на записи ответа мост отступал
        /// всегда (§12.9).
        /// </para>
        ///
        /// <para>
        /// <c>DefaultBufferSize</c> тоже не спрашивается, и по тому же
        /// правилу, что <c>MaxDepth</c>: это подсказка о размере временных
        /// буферов, и в документе её не видно ни одним байтом. Порождённый код
        /// её не читает - он пишет в свою раковину и читает из чужого
        /// читателя. Отказывать из-за неё значило бы отказывать тому, кто
        /// всего лишь настроил размер буфера под свою нагрузку. Что документ
        /// от неё не зависит - закреплено пробой, а не объявлено
        /// (<c>CompatTests/BufferSizeFixture</c>).
        /// </para>
        ///
        /// <para>
        /// Имена свойств при этом остаются константами и через энкодер не
        /// проходят - на них у профиля отдельное условие: генератор печатает
        /// веб-вариант только для тех типов, чьи имена <b>одинаковы при любом
        /// энкодере</b>, а про остальные говорит <c>JGD004</c>. Условие
        /// проверяется на компиляции, потому что в рантайме менять уже нечего.
        /// </para>
        /// </summary>
        public static bool IsWebApartFromTheResolver(JsonSerializerOptions? options)
        {
            if (options is null)
            {
                return false;
            }

            if (WebVerdicts.TryGetValue(options, out var cached))
            {
                return (bool)cached;
            }

            var verdict = ComputeWeb(options);

            if (options.IsReadOnly)
            {
                try
                {
                    WebVerdicts.Add(options, verdict ? Yes : No);
                }
                catch (ArgumentException)
                {
                    //кто-то успел раньше; вердикт у него тот же
                }
            }

            return verdict;
        }

        private static bool ComputeWeb(JsonSerializerOptions options)
        {
            if (options.Converters.Count > 0)
            {
                return false;
            }

            foreach (var property in Compared)
            {
                if (property.Name == nameof(JsonSerializerOptions.MaxDepth)
                    || property.Name == nameof(JsonSerializerOptions.DefaultBufferSize))
                {
                    continue;
                }

                object? mine;
                object? theirs;

                try
                {
                    mine = property.GetValue(options);
                    theirs = property.GetValue(FreshWeb);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!Equals(mine, theirs))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ComputeApartFromTheResolver(JsonSerializerOptions options)
        {
            return options.Converters.Count == 0 && ComparedPropertiesAreDefault(options);
        }

        private static bool Compute(JsonSerializerOptions options)
        {
            if (options.Converters.Count > 0)
            {
                return false;
            }

            //резолвер: либо его ещё нет, либо это тот самый, которым эталон
            //обзаводится сам. Любой чужой - это чужая модель типа, и быстрый
            //путь про неё ничего не знает
            var resolver = options.TypeInfoResolver;
            if (resolver is not null && resolver.GetType() != typeof(DefaultJsonTypeInfoResolver))
            {
                return false;
            }

            if (!ChainIsDefault(options))
            {
                return false;
            }

            return ComparedPropertiesAreDefault(options);
        }

        /// <summary>
        /// Все сравниваемые свойства совпадают с умолчанием. Вынесено из
        /// <see cref="Compute"/> затем, чтобы вопрос моста и вопрос фасада
        /// отличались ровно тем, чем они отличаются, - и ни одним свойством
        /// больше.
        /// </summary>
        /// <summary>
        /// Чем именно эти опции отличаются от умолчаний - именами свойств.
        ///
        /// <para>
        /// Существует ради одного: «опции не умолчательные» - бесполезный
        /// ответ. Человек, у которого не ускорилось, должен увидеть
        /// <c>PropertyNamingPolicy, PropertyNameCaseInsensitive,
        /// NumberHandling</c> и узнать в этом списке настройки ASP.NET,
        /// которых он сам не ставил.
        /// </para>
        ///
        /// <para>
        /// Резолвер этого не зовёт: там довольно <c>true</c>/<c>false</c>, а
        /// перечисление стои́т рефлексии по всем свойствам. Зовётся только
        /// тогда, когда ответ понадобился словами.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> DifferencesFromDefault(JsonSerializerOptions? options)
        {
            if (options is null)
            {
                return Array.Empty<string>();
            }

            var differences = new List<string>();

            if (options.Converters.Count > 0)
            {
                differences.Add(nameof(JsonSerializerOptions.Converters));
            }

            if (!EncoderIsDefault(options))
            {
                differences.Add(nameof(JsonSerializerOptions.Encoder));
            }

            foreach (var property in Compared)
            {
                object? mine;
                object? theirs;

                try
                {
                    mine = property.GetValue(options);
                    theirs = property.GetValue(Fresh);
                }
                catch (Exception)
                {
                    differences.Add(property.Name);
                    continue;
                }

                if (!Equals(mine, theirs))
                {
                    differences.Add(property.Name);
                }
            }

            return differences;
        }

        private static bool ComparedPropertiesAreDefault(JsonSerializerOptions options)
        {
            if (!EncoderIsDefault(options))
            {
                return false;
            }

            foreach (var property in Compared)
            {
                object? mine;
                object? theirs;

                try
                {
                    mine = property.GetValue(options);
                    theirs = property.GetValue(Fresh);
                }
                catch (Exception)
                {
                    //свойство не отдалось - значит мы про него ничего не знаем,
                    //значит «нет»
                    return false;
                }

                if (!Equals(mine, theirs))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Цепочка резолверов пуста либо состоит из одного резолвера по
        /// умолчанию. Свойство читается рефлексией: на netstandard2.0 с
        /// System.Text.Json 9.0 оно есть, но писать его имя в коде значило бы
        /// потерять сборку на таргете, где его нет.
        /// </summary>
        private static bool ChainIsDefault(JsonSerializerOptions options)
        {
            var property = typeof(JsonSerializerOptions).GetProperty("TypeInfoResolverChain");
            if (property is null)
            {
                return true;
            }

            object? value;
            try
            {
                value = property.GetValue(options);
            }
            catch (Exception)
            {
                return false;
            }

            if (value is not IEnumerable<IJsonTypeInfoResolver> chain)
            {
                return false;
            }

            var count = 0;
            foreach (var item in chain)
            {
                count++;
                if (count > 1 || item.GetType() != typeof(DefaultJsonTypeInfoResolver))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
