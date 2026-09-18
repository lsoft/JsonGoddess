using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
                || name == nameof(JsonSerializerOptions.Converters);
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
