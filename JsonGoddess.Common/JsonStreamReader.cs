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

        /// <summary>Автомат дошёл до конца документа.</summary>
        protected abstract bool IsDone
        {
            get;
        }

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

        public async Task<T> ReadAsync(
            PipeReader pipe,
            int cap = DefaultCap,
            CancellationToken cancellationToken = default
            )
        {
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

                    if (buffer.Length > cap)
                    {
                        throw new JsonDocumentException(
                            "The pending JSON value exceeds the configured limit of "
                            + cap.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes.",
                            0
                            );
                    }

                    var consumed = Window(buffer, read.IsCompleted);

                    //съедено - слева, просмотрено - до конца окна: иначе труба
                    //отдаст то же самое и будет ждать вечно
                    pipe.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    if (IsDone)
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
