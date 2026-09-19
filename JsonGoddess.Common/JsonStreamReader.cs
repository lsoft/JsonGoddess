//Труба (System.IO.Pipelines) живёт в самом рантайме начиная с .NET 5, а на
//netstandard2.0 приехала бы отдельным пакетом. Платить им там незачем: потоковый
//путь печатается только сборке, которая ссылается на ASP.NET Core, то есть
//net8.0 и новее. Отсутствие типа на netstandard2.0 никого не сломает - там
//просто нет и порождённого кода, который бы его звал.
#if NET8_0_OR_GREATER

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Общая половина потокового чтения (PLAN.md §12.9, фаза 10): цикл по трубе,
    /// сборка непрерывного окна, потолок на недочитанную конструкцию, возврат
    /// аренды. От типа не зависит ничего из перечисленного, поэтому печатать это
    /// в каждый хост было бы напрасно - печатается только
    /// <see cref="ParseWhatFits"/>, то есть автомат верхнего уровня.
    ///
    /// <para>
    /// <b>Класс, а не делегат</b>, и причина техническая: окно - это
    /// <c>ReadOnlySpan&lt;byte&gt;</c>, а спан не переживает <c>await</c>
    /// (CS4007). Здесь он и не пытается: <see cref="ParseWhatFits"/> -
    /// синхронный метод, спан живёт внутри одного его вызова, а состояние
    /// разбора между вызовами хранится полями наследника. Ровно поэтому схема
    /// называется «переигрывать, а не возобновляться».
    /// </para>
    ///
    /// <para>
    /// Виртуальный вызов здесь один <b>на окно</b>, а не на элемент: окон на
    /// теле в 3.7 МБ приходится около девятисот.
    /// </para>
    /// </summary>
    public abstract class JsonStreamReader<T>
    {
        /// <summary>
        /// Потолок на недочитанную конструкцию. У эталона такой защиты нет
        /// вовсе - пробой подтверждено, что он растит буфер до размера токена,
        /// каким бы тот ни был.
        /// </summary>
        public const int DefaultCap = 8 * 1024 * 1024;

        private byte[]? _scratch;

        /// <summary>Наибольшее окно за чтение - главное число потокового пути.</summary>
        public int LargestWindow
        {
            get; private set;
        }

        /// <summary>Сколько раз обращались к трубе.</summary>
        public int Reads
        {
            get; private set;
        }

        /// <summary>Сколько раз окно пришлось собирать копией из нескольких сегментов.</summary>
        public int Gathers
        {
            get; private set;
        }

        /// <summary>Разбор начали и бросили: значение не влезло в окно.</summary>
        public int Retries
        {
            get; protected set;
        }

        /// <summary>Разбор не начали вовсе: заведомо не влезет (см. наследника).</summary>
        public int Skipped
        {
            get; protected set;
        }

        /// <summary>
        /// Тело приехало целиком одним куском, и его прочитал <b>обычный</b>
        /// читатель, без автомата и переигрываний.
        /// </summary>
        public bool ReadWhole
        {
            get; private set;
        }

        /// <summary>Автомат дошёл до конца документа.</summary>
        protected abstract bool IsDone
        {
            get;
        }

        /// <summary>
        /// Прочитать тело целиком обычным читателем - выбор пути по месту
        /// (PLAN.md §12.9, фаза 10, пункт 7).
        ///
        /// <para>
        /// Зовётся один раз и только тогда, когда первое же обращение к трубе
        /// вернуло <b>всё</b> тело <b>одним сегментом</b>: копировать нечего,
        /// ждать нечего, и автомат с его проверками «хватило ли байт» -
        /// чистый расход. Замер: 624.5 нс против 704.0 нс на объект, плюс не
        /// платится машинерия <c>async</c> (272 байта на запрос).
        /// </para>
        ///
        /// <para>
        /// Ложь означает «такого пути у меня нет» - база так и отвечает.
        /// </para>
        /// </summary>
        protected virtual bool TryReadWhole(scoped ReadOnlySpan<byte> json, out T value)
        {
            value = default!;
            return false;
        }

        /// <summary>
        /// Отвергать ли хвост после корневого значения -
        /// <c>JsonGuard.TrailingContent</c>.
        ///
        /// <para>
        /// Найдено пробой, а не рассуждением: драйвер останавливается, дочитав
        /// корень, и мусор, оставшийся в трубе, он попросту не видит - на
        /// <c>[]мусор</c> эталон отвечал 400, а мы 200. Цена проверки - проход
        /// по пробелам от конца корня до конца тела, и платит её только хост,
        /// попросивший строгости (веб-профиль моста просит).
        /// </para>
        /// </summary>
        protected virtual bool RefusesTrailingContent => false;

        /// <summary>
        /// Разобрать то, что влезло, и вернуть число <b>съеденных</b> байт -
        /// то есть границу, за которую откатываться уже не придётся.
        /// </summary>
        protected abstract int ParseWhatFits(ReadOnlySpan<byte> json, bool final);

        /// <summary>Результат целиком - зовётся один раз, после <see cref="IsDone"/>.</summary>
        protected abstract T Finish();

        /// <summary>Вернуть всё арендованное - и на успехе, и на отказе.</summary>
        protected virtual void Release()
        {
        }

        /// <param name="expectedLength">
        /// Длина тела, если она объявлена (<c>Content-Length</c>), иначе -1.
        ///
        /// <para>
        /// Нужна ровно для быстрого пути, и <b>без неё он почти не срабатывает</b>
        /// - замерено: у тела в 374 байта первое же обращение к трубе приносит
        /// его целиком, но <c>IsCompleted</c> при этом ещё ложно, потому что
        /// писатель трубы не закрыт. Ждать второго обращения только затем, чтобы
        /// узнать это, значило бы потерять весь выигрыш.
        /// </para>
        /// </param>
        public async Task<T> ReadAsync(
            PipeReader pipe,
            int cap = DefaultCap,
            long expectedLength = -1,
            CancellationToken cancellationToken = default
            )
        {
            var first = true;

            try
            {
                while (true)
                {
                    var read = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);

                    if (read.IsCanceled)
                    {
                        throw new OperationCanceledException();
                    }

                    var buffer = read.Buffer;
                    Reads++;

                    if (first)
                    {
                        first = false;

                        //всё тело, один сегмент - разбирать его автоматом незачем
                        if (buffer.IsSingleSegment
                            && (read.IsCompleted || (expectedLength >= 0 && buffer.Length >= expectedLength))
                            && buffer.Length <= cap
                            && TryReadWhole(buffer.FirstSpan, out var whole))
                        {
                            ReadWhole = true;
                            pipe.AdvanceTo(buffer.End);

                            return whole;
                        }
                    }

                    if (buffer.Length > cap)
                    {
                        throw new JsonDocumentException(
                            "The pending JSON value exceeds the configured limit of "
                            + cap.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.",
                            0
                            );
                    }

                    long consumed;

                    if (IsDone)
                    {
                        //корень уже прочитан на прошлом обороте - осталось
                        //убедиться, что дальше только пробелы
                        consumed = Trailing(buffer, 0);
                    }
                    else
                    {
                        consumed = Window(buffer, read.IsCompleted);

                        if (IsDone)
                        {
                            //хвост в ЭТОМ же окне: корень кончился, а байты
                            //за ним никто ещё не смотрел
                            consumed = Trailing(buffer, consumed);
                        }
                    }

                    //съедено - слева, просмотрено - до конца окна: иначе труба
                    //отдаст то же самое и будет ждать вечно
                    pipe.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    if (IsDone && (read.IsCompleted || !RefusesTrailingContent))
                    {
                        return Finish();
                    }

                    if (read.IsCompleted)
                    {
                        //сюда попасть нельзя: при final читатель обязан либо
                        //дочитать, либо отказать. Проверка на случай, если
                        //где-то в Try-примитивах упущен случай, - молчаливо
                        //принятый обрезанный документ хуже лишнего отказа
                        throw new JsonDocumentException("Unexpected end of data.", 0);
                    }
                }
            }
            finally
            {
                Release();

                if (_scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(_scratch);
                    _scratch = null;
                }
            }
        }

        /// <summary>
        /// Хвост после корня: от <paramref name="from"/> и до конца окна там
        /// обязаны быть одни пробелы. Возвращает новую границу съеденного -
        /// пробелы съедаются, иначе труба отдала бы их снова.
        ///
        /// <para>
        /// Идёт по сегментам, а не по непрерывной копии: хвост бывает какой
        /// угодно длины, и собирать его в один буфер ради того, чтобы отказать,
        /// было бы дорого ровно в том случае, когда отказ и нужен.
        /// </para>
        /// </summary>
        private long Trailing(in ReadOnlySequence<byte> buffer, long from)
        {
            if (!RefusesTrailingContent)
            {
                return from;
            }

            var offset = from;

            foreach (var segment in buffer.Slice(from))
            {
                var span = segment.Span;

                for (var i = 0; i < span.Length; i++)
                {
                    var b = span[i];

                    if (b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                    {
                        continue;
                    }

                    throw new JsonDocumentException(
                        "Unexpected trailing content after the top-level value.",
                        checked((int)(offset + i)),
                        "$",
                        -1,
                        -1,
                        null
                        );
                }

                offset += span.Length;
            }

            return buffer.Length;
        }

        private int Window(in ReadOnlySequence<byte> buffer, bool final)
        {
            var json = Contiguous(buffer);

            if (json.Length > LargestWindow)
            {
                LargestWindow = json.Length;
            }

            return ParseWhatFits(json, final);
        }

        /// <summary>
        /// Непрерывный кусок. Обычный случай - один сегмент, и тогда не
        /// копируется ничего. Иначе собирается <b>остаток</b>, а он ограничен
        /// недочитанным значением, а не телом.
        /// </summary>
        private ReadOnlySpan<byte> Contiguous(in ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                return buffer.FirstSpan;
            }

            var length = checked((int)buffer.Length);
            var scratch = _scratch;

            if (scratch is null || scratch.Length < length)
            {
                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }

                scratch = ArrayPool<byte>.Shared.Rent(length);
                _scratch = scratch;
            }

            buffer.CopyTo(scratch);
            Gathers++;

            return new ReadOnlySpan<byte>(scratch, 0, length);
        }
    }
}

#endif
