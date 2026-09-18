using System;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Order;
using JsonGoddess.PerformanceTests.Generated;
using JsonGoddess.PerformanceTests.Model;

namespace JsonGoddess.PerformanceTests
{
    /// <summary>
    /// LINK: дешёвое звено цепочки <b>и калибровка самого стенда</b> (§12.5
    /// плана).
    ///
    /// Зачем нужна калибровка. Пара «порождённый/рукописный» на записи WIDE
    /// разошлась на 8% при том, что IL у них совпадает байт в байт - 928 байт
    /// вместе с токенами метаданных. Значит разница возникла не в коде, а в
    /// раскладке: выравнивание, попадание в наборы кэша команд, алиасинг
    /// предсказателя переходов. Доверительные интервалы BenchmarkDotNet этого
    /// не описывают: раскладка внутри процесса фиксирована, поэтому её вклад
    /// попадает не в StdDev, а прямо в Mean.
    ///
    /// Отсюда устройство этой фикстуры. Форм здесь четыре, но вопроса два, и
    /// формы стоят <b>парами</b>: <c>CallsA</c>/<c>CallsB</c> - цепочка вызовов
    /// <c>SequenceEqual</c> (что эмиттер печатает сегодня),
    /// <c>WordsA</c>/<c>WordsB</c> - цепочка дешёвых звеньев. Половины пары
    /// порождены одним и тем же кодом и совпадают до последнего байта IL, то
    /// есть разойтись во времени могут <b>только</b> раскладкой.
    ///
    /// <b>Порядок чтения таблицы обратный обычному.</b> Сперва смотреть
    /// <c>|A−B|</c> внутри каждой пары - это полоса неразличимости, измеренная
    /// в этом самом прогоне. И только потом разницу между парами - это эффект.
    /// Эффект меньше полосы означает, что замер не сказал ничего, сколько бы
    /// итераций ни было прокручено.
    ///
    /// <b>Чем сужается полоса.</b> Раскладка внутри одной сборки
    /// детерминирована, поэтому большим числом итераций её не усреднить -
    /// итерации крутят один и тот же машинный код по одному и тому же адресу.
    /// Усредняется она по <b>запускам</b>: адреса кучи кода отдаются
    /// операционной системой заново на каждый процесс, и попадание метода в
    /// наборы кэша меняется вместе с ними. Поэтому здесь
    /// <c>launchCount: 9</c> - девять процессов вместо одного, - а число
    /// итераций внутри процесса взято скромным: оно борется со случайным
    /// шумом, которого и так немного, а не с систематикой.
    ///
    /// Форма модели подобрана под вопрос: восемь членов, все длины разные (то
    /// есть в каждой корзине один член, и на свойство приходится ровно одно
    /// сравнение имени) и все в 8…15 байт (то есть дешёвое звено применимо
    /// везде). Это форма обычного типа, а не вырожденного, как PREFIX, - там
    /// звено экономит пятнадцать вызовов на имя и даёт 41%, а здесь экономит
    /// один, и вопрос как раз в том, видно ли это вообще.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Throughput, launchCount: 9, warmupCount: 4, iterationCount: 8)]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [Orderer(SummaryOrderPolicy.Declared)]
    [CategoriesColumn]
    public class LayoutFixture
    {
        private Link _link = null!;
        private byte[] _utf8 = null!;

        [GlobalSetup]
        public void Setup()
        {
            _link = Link.CreateSample();
            _utf8 = JsonSerializer.SerializeToUtf8Bytes(_link);

            Verify();
        }

        /// <summary>
        /// Пять форм обязаны прочитать один документ в один объект. Для пар
        /// это ещё и проверка того, что они действительно одинаковы: если
        /// половины разойдутся, калибровка перестанет быть калибровкой, а
        /// таблица этого не покажет никак.
        /// </summary>
        private void Verify()
        {
            LinkSerializer.DeserializeCallsA(DefaultInjector.Instance, _utf8, out var callsA);
            LinkSerializer.DeserializeCallsB(DefaultInjector.Instance, _utf8, out var callsB);
            LinkSerializer.DeserializeWordsA(DefaultInjector.Instance, _utf8, out var wordsA);
            LinkSerializer.DeserializeWordsB(DefaultInjector.Instance, _utf8, out var wordsB);
            LinkGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var generated);

            var expected = Checksum(_link);

            Expect("calls/A", callsA, expected);
            Expect("calls/B", callsB, expected);
            Expect("words/A", wordsA, expected);
            Expect("words/B", wordsB, expected);
            Expect("generated", generated, expected);

            //и отдельно - что чужое имя той же длины НЕ совпало. Дешёвое звено
            //сравнивает константы, которые посчитал скрипт; перепутанная
            //константа не падает, она просто не совпадает - свойство молча
            //уедет в SkipValue, и документ прочитается с дырой. Проверить это
            //надо явно, потому что ни одна другая проверка такого не заметит.
            var alien = System.Text.Encoding.UTF8.GetBytes(
                "{\"Quantitx\":1,\"Referencx\":2,\"CustomerIx\":3,\"Descriptiox\":4,"
                + "\"DeliveryDatx\":5,\"InvoiceNumbex\":6,\"ShippingMethox\":7,\"PaymentProvidex\":8}"
                );

            LinkSerializer.DeserializeWordsA(DefaultInjector.Instance, alien, out var nothing);
            if (nothing is null || Checksum(nothing) != 0)
            {
                throw new InvalidOperationException(
                    "LINK: дешёвое звено приняло чужие имена той же длины - контрольная сумма "
                    + (nothing is null ? "null" : Checksum(nothing).ToString())
                    );
            }
        }

        private static void Expect(string form, Link? actual, long expected)
        {
            if (actual is null)
            {
                throw new InvalidOperationException("LINK, форма " + form + ": прочитан null");
            }

            var got = Checksum(actual);
            if (got != expected)
            {
                throw new InvalidOperationException(
                    "LINK, форма " + form + ": контрольная сумма " + got + " вместо " + expected
                    );
            }
        }

        /// <summary>
        /// Сумма взвешенная: значения членов различны, и простая сумма пережила
        /// бы перестановку - а перестановка здесь ровно тот сорт ошибки, который
        /// и надо ловить. Спутанные местами константы дали бы ту же сумму и
        /// другой объект.
        /// </summary>
        private static long Checksum(Link value)
        {
            long sum = 0;
            sum += 1L * value.Quantity;
            sum += 2L * value.Reference;
            sum += 3L * value.CustomerId;
            sum += 4L * value.Description;
            sum += 5L * value.DeliveryDate;
            sum += 6L * value.InvoiceNumber;
            sum += 7L * value.ShippingMethod;
            sum += 8L * value.PaymentProvider;
            return sum;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Baseline = true, Description = "calls A: SequenceEqual (как сейчас)")]
        public Link? CallsA()
        {
            LinkSerializer.DeserializeCallsA(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "calls B: тот же код второй раз")]
        public Link? CallsB()
        {
            LinkSerializer.DeserializeCallsB(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "words A: один-два ulong вместо вызова")]
        public Link? WordsA()
        {
            LinkSerializer.DeserializeWordsA(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "words B: тот же код второй раз")]
        public Link? WordsB()
        {
            LinkSerializer.DeserializeWordsB(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "JsonGoddess (порождённый)")]
        public Link? Generated()
        {
            LinkGenerated.Deserialize(DefaultInjector.Instance, _utf8, out var result);
            return result;
        }

        [BenchmarkCategory("deserialize")]
        [Benchmark(Description = "System.Text.Json (для масштаба)")]
        public Link? Stj()
        {
            return JsonSerializer.Deserialize<Link>(_utf8);
        }
    }
}
