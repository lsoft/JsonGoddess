using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JsonGoddess.Compat.Interop
{
    /// <summary>
    /// Мост (§10, маршрут B): порождённый код, выданный эталону под видом
    /// <see cref="JsonTypeInfo"/>.
    ///
    /// <para>
    /// Зачем он нужен, когда есть маршрут A. Маршрут A подменяет вызов
    /// <c>JsonSerializer</c> в <b>вашем</b> коде - генератор видит вызов и
    /// печатает вместо него обращение к порождённому. В настоящем приложении
    /// вызовов в вашем коде почти нет: объект контроллера сериализует
    /// ASP.NET Core, ответ читает <c>HttpClient.ReadFromJsonAsync</c>, тело
    /// запроса разбирает форматтер. Весь этот код давно скомпилирован, и
    /// генератору в него не заглянуть. Мост входит с другой стороны - через
    /// штатную точку расширения эталона, - и потому достаёт до всего, что
    /// принимает <see cref="JsonSerializerOptions"/>.
    /// </para>
    ///
    /// <code>
    /// builder.Services
    ///     .AddControllers()
    ///     .AddJsonOptions(o => o.JsonSerializerOptions.UseJsonGoddess());
    /// </code>
    ///
    /// <para>
    /// Явным вызовом, а не само: опции строит потребитель, и вклиниться в
    /// чужой конструктор нечем. Это же и честнее - маршрут A включается
    /// молча, потому что там человек уже сослался на фасад и другого смысла в
    /// ссылке нет, а здесь он передаёт свои опции чужой библиотеке и должен
    /// знать, что в них добавилось.
    /// </para>
    /// </summary>
    public static class JsonGoddess
    {
        /// <summary>
        /// Добавить порождённый код в <paramref name="options"/>. Возвращает
        /// те же опции, чтобы вызов можно было писать цепочкой.
        ///
        /// <para>
        /// Типы, которых мост не обслуживает, продолжают идти обычным путём
        /// эталона: резолвер по умолчанию остаётся в цепочке следом за нашим.
        /// </para>
        /// </summary>
        public static JsonSerializerOptions UseJsonGoddess(this JsonSerializerOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            //Цепочка, а не подмена TypeInfoResolver: у потребителя там уже
            //может лежать его собственный резолвер - контекст source-gen,
            //например, - и затереть его значило бы сломать то, что работало.
            var chain = options.TypeInfoResolverChain;

            //Пустая цепочка - это НЕ «резолвер по умолчанию»: пока к ней никто
            //не притронулся, эталон подставляет умолчание сам, а первое же
            //обращение превращает её в настоящий список, и умолчания в нём
            //нет. Вставив в такую цепочку только себя, мы оставили бы без
            //резолвера все типы, которых мост не обслуживает, - то есть
            //сломали бы ровно то, что обязаны были не трогать. Проверено
            //прогоном: без этой ветки шесть тестов падают с
            //NotSupportedException.
            var empty = chain.Count == 0;

            chain.Insert(0, BridgeResolver.Instance);

            if (empty)
            {
                chain.Add(new DefaultJsonTypeInfoResolver());
            }

            return options;
        }

        /// <summary>
        /// Сколько типов обслуживает мост в этой сборке. Ноль означает, что
        /// генератор не отработал или отступил на всех типах; причину он
        /// сообщает диагностикой <c>JGD001</c> на сборке.
        /// </summary>
        public static int ServedTypeCount => BridgeRegistry.Count;

        /// <summary>
        /// Возьмётся ли мост за <paramref name="type"/> с этими опциями, и
        /// если нет - почему. Существует ради тестов и ради человека, который
        /// не понимает, отчего не ускорилось.
        /// </summary>
        public static string Explain(Type type, JsonSerializerOptions? options)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!BridgeRegistry.Knows(type))
            {
                return "the bridge does not serve '" + type + "': the generator either refused the type or never saw it.";
            }

            if (options is null || CompatOptions.IsDefaultApartFromTheResolver(options))
            {
                return BridgeRegistry.TryGet(type, BridgeProfile.Default, out _)
                    ? "the bridge serves '" + type + "' with the default profile."
                    : "the options are the defaults, but no default-profile code was generated for '" + type + "'.";
            }

            if (CompatOptions.IsWebApartFromTheResolver(options))
            {
                return BridgeRegistry.TryGet(type, BridgeProfile.Web, out _)
                    ? "the bridge serves '" + type + "' with the ASP.NET Core web profile."
                    : "the options are the ASP.NET Core web profile, but no web-profile code was generated;"
                    + " set <JsonGoddessCompatWeb>enable</JsonGoddessCompatWeb> in the project that owns this type.";
            }

            return "the options match neither the defaults nor the ASP.NET Core web profile ("
                + Differences(options) + " differ from the defaults);"
                + " serialization goes through System.Text.Json instead.";
        }

        /// <summary>
        /// Мост отступил: тип он обслуживает, но эти опции - не те, под
        /// которые напечатан код.
        ///
        /// <para>
        /// Событие, а не запись в лог, по двум причинам. Библиотека, пишущая
        /// в чужой <c>Console</c>, невежлива, а ссылаться из
        /// netstandard2.0-пакета на <c>Microsoft.Extensions.Logging</c> ради
        /// одной строки - дорого. Подписчику остаётся одна строка:
        /// </para>
        ///
        /// <code>
        /// JsonGoddess.Declined += (type, why) => logger.LogWarning("JsonGoddess: {Type}: {Why}", type, why);
        /// </code>
        ///
        /// <para>
        /// Молчать было нельзя. Человек подключил пакет, позвал
        /// <see cref="UseJsonGoddess"/>, ничего не ускорилось - и узнать
        /// почему неоткуда. Это ровно та же мысль, из-за которой в маршруте A
        /// появились <c>JGD001</c> и <c>JsonGoddessCompatStrict</c>: молча
        /// принятое решение хуже громкого.
        /// </para>
        ///
        /// <para>
        /// Срабатывает один раз на пару «тип и экземпляр опций»: резолвера
        /// эталон спрашивает однажды и ответ кеширует. Не срабатывает на типах,
        /// которых мост не обслуживает вовсе, - там подписчик получил бы
        /// <c>System.Int32</c> и прочий шум, а про настоящую причину генератор
        /// уже сказал диагностикой на сборке.
        /// </para>
        /// </summary>
        public static event Action<Type, string>? Declined;

        internal static void OnDeclined(Type type, JsonSerializerOptions options)
        {
            var handler = Declined;
            if (handler is null)
            {
                return;
            }

            handler(
                type,
                "the bridge stepped aside: the options match neither the defaults nor the ASP.NET Core web profile"
                + " (" + Differences(options) + " differ from the defaults),"
                + " and the generated code was produced for those two only."
                );
        }

        /// <summary>
        /// Опции опознаны, а кода под них не напечатано. Случай отдельный от
        /// <see cref="OnDeclined"/>, потому что и лечится он иначе: не
        /// настройками приложения, а свойством сборки.
        /// </summary>
        internal static void OnProfileMissing(Type type, BridgeProfile profile)
        {
            var handler = Declined;
            if (handler is null)
            {
                return;
            }

            handler(
                type,
                profile == BridgeProfile.Web
                    ? "the options are the ASP.NET Core web profile, but no web-profile code was generated;"
                    + " set <JsonGoddessCompatWeb>enable</JsonGoddessCompatWeb> in the project that owns this type."
                    : "the options are the defaults, but no default-profile code was generated for this type."
                );
        }

        private static string Differences(JsonSerializerOptions options)
        {
            var names = CompatOptions.DifferencesFromDefault(options);

            return names.Count == 0
                ? "no property differs, but the verdict is still negative"
                : string.Join(", ", names);
        }
    }

    /// <summary>
    /// Резолвер моста. Один на процесс: состояния у него нет, а решение
    /// зависит только от типа и опций, которые ему приносят.
    /// </summary>
    internal sealed class BridgeResolver : IJsonTypeInfoResolver
    {
        internal static readonly BridgeResolver Instance = new BridgeResolver();

        private BridgeResolver()
        {
        }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (!BridgeRegistry.Knows(type))
            {
                //не наш тип - пусть его строит следующий в цепочке
                return null;
            }

            //Порождённый код написан под КОНКРЕТНОЕ поведение, и профилей у
            //него столько, сколько напечатал генератор. Отдать ему чужие опции
            //значило бы выдать валидный документ, отличающийся от эталонного:
            //тот самый худший исход, ради которого весь этот слой и обвешан
            //проверками. Сомнение - «нет».
            var profile =
                CompatOptions.IsDefaultApartFromTheResolver(options) ? BridgeProfile.Default
                : CompatOptions.IsWebApartFromTheResolver(options) ? BridgeProfile.Web
                : (BridgeProfile?)null;

            if (profile is null)
            {
                JsonGoddess.OnDeclined(type, options);
                return null;
            }

            if (!BridgeRegistry.TryGet(type, profile.Value, out var factory))
            {
                //профиль опознан, а кода под него нет: веб-вариант печатается
                //не всегда, и молчать об этом нельзя - причина ровно та же,
                //что и у чужих опций
                JsonGoddess.OnProfileMissing(type, profile.Value);
                return null;
            }

            return factory!(options);
        }
    }
}
