using System;
using JsonGoddess.Internal;

namespace JsonGoddess
{
    /// <summary>
    /// То же, что <see cref="PooledUtf8Exhauster"/>, но строки экранируются
    /// так, как их экранирует энкодер <c>System.Text.Json</c> по умолчанию.
    ///
    /// <para>
    /// Существует ровно для Compat-слоя (PLAN.md §10). Тот, кто позвал
    /// JsonGoddess по имени, выбрал наш набор - минимум RFC 8259 §7 - вместе с
    /// библиотекой. Тот, кто подменил <c>JsonSerializer</c> псевдонимом, не
    /// выбирал ничего: он поменял пакет, а не код, - и байты его документа
    /// меняться не должны. А менялись они на любой не-ASCII строке, то есть на
    /// всяком русском имени; нашлось это прогоном их корпуса поверх фасада
    /// (PLAN.md §11.1, маршрут B).
    /// </para>
    ///
    /// <para>
    /// Отказать вместо этого было нельзя: содержимое строки на компиляции
    /// неизвестно, и отказывать пришлось бы всякому типу со строковым членом,
    /// то есть почти всякому.
    /// </para>
    ///
    /// <para>
    /// Цена платится только на строках, и только на тех, где есть что
    /// экранировать: чистый ASCII идёт тем же одним транскодированием, что и у
    /// <see cref="PooledUtf8Exhauster"/>. Отстать от эталона нельзя по
    /// построению - тот же проход по строке делает и он.
    /// </para>
    /// </summary>
    public sealed class CompatUtf8Exhauster : PooledUtf8ExhausterBase
    {
        public CompatUtf8Exhauster()
            : base(DefaultCapacity)
        {
        }

        public CompatUtf8Exhauster(int capacity)
            : base(capacity)
        {
        }

        public override void Append(string? value)
        {
            if (value is null)
            {
                AppendNull();
                return;
            }

            AppendText(value.AsSpan());
        }

        public override void Append(char value)
        {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            AppendText(one);
        }

        /// <summary>
        /// Строка в кавычках. Устроена так же, как у базового sink'а: быстрый
        /// путь - прогон без экранируемого, одно транскодирование; медленный
        /// идёт прогонами между экранируемыми символами, без промежуточной
        /// строки.
        ///
        /// <para>
        /// Отличие одно и оно в конце: escape съедает не всегда один символ.
        /// Суррогатная пара - это один символ Unicode, записанный двумя
        /// <c>char</c>, и эталон пишет её двумя escape'ами подряд, по одному
        /// на каждую половину пары; разорвать её нельзя.
        /// </para>
        /// </summary>
        private void AppendText(ReadOnlySpan<char> text)
        {
            //3 байта на символ - потолок UTF-8 для любого char; бюджет тот же,
            //что у базового sink'а, потому что и здесь подавляющее большинство
            //строк экранирования не требует вовсе. Экранирование раздувает до
            //шести, и на этот случай ниже стоит пересчёт с ростом
            var span = GetSpan(text.Length * 3 + 2);
            span[0] = (byte)'"';
            var written = 1;

            var rest = text;
            while (true)
            {
                var escapable = JsonReferenceStringEncoder.IndexOfEscapable(rest);
                if (escapable < 0)
                {
                    written += JsonStringEncoder.Transcode(rest, span.Slice(written));
                    break;
                }

                if (escapable > 0)
                {
                    written += JsonStringEncoder.Transcode(rest.Slice(0, escapable), span.Slice(written));
                }

                rest = rest.Slice(escapable);

                //проверяется место под худший случай всего остатка, а не под
                //один escape: тогда рост случается максимум один раз за строку
                var worstCaseRest = rest.Length * JsonReferenceStringEncoder.MaxBytesPerEscape + 1;
                if (span.Length - written < worstCaseRest)
                {
                    Advance(written);
                    span = GetSpan(worstCaseRest);
                    written = 0;
                }

                //экранируемое идёт прогоном, а не по одному символу за оборот
                //внешнего цикла. Разница здесь не косметическая: у русского
                //текста экранируется КАЖДЫЙ символ, и поиск следующего
                //экранируемого - векторный вызов с постоянной ценой - тогда
                //приходился бы на каждую букву. Числа - в PLAN.md §15 O9
                do
                {
                    written += JsonReferenceStringEncoder.WriteEscape(
                        rest, span.Slice(written), out var consumed
                        );

                    rest = rest.Slice(consumed);
                }
                while (rest.Length > 0 && JsonReferenceStringEncoder.NeedsEscape(rest[0]));
            }

            span[written] = (byte)'"';
            Advance(written + 1);
        }
    }
}
