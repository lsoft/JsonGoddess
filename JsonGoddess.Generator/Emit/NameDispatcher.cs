using System;
using System.Collections.Generic;
using System.Linq;
using JsonGoddess.Generator.Binding;
using JsonGoddess.Generator.Model;

namespace JsonGoddess.Generator.Emit
{
    /// <summary>
    /// Диспетчер имён свойств - главная точка выигрыша на чтении (§8.2 плана).
    ///
    /// Форм две, и у каждой своя измеренная область (A/B внутри одного прогона,
    /// net10, процесс прибит к P-ядрам, интервалы не перекрываются ни в одну
    /// сторону):
    /// <list type="bullet">
    /// <item>REGULAR, 7 членов с разными длинами: по длине 615.6 ns,
    /// по ключу 705.8 ns - ключ на 15% хуже;</item>
    /// <item>WIDE, 30 членов с именами по 7 байт: по длине 793.0 ns,
    /// по ключу 617.4 ns - ключ на 28% лучше.</item>
    /// </list>
    /// (Числа последнего прогона, §12 плана; прежние - 12-13.5% и 28-32% -
    /// получены раньше на той же машине и воспроизвелись.)
    /// Механизм обеих сторон один: <c>switch</c> по длине попадает в член сразу,
    /// когда длины разводят членов по корзинам, и вырождается в линейную цепочку
    /// <c>SequenceEqual</c>, когда все имена в одной корзине. Ключ от разброса
    /// длин не зависит, но его надо вычислить, и там, где длина уже всё решила,
    /// это чистый расход.
    ///
    /// Отсюда гибрид: <c>switch</c> по длине верхним уровнем всегда, а внутри
    /// корзины, где членов больше порога, - <c>switch</c> по ключу вместо
    /// цепочки.
    ///
    /// <b>Гибрид измерен</b> (§12.2 плана), и обе точки сошлись: он выбирает
    /// ту форму, которая на этой форме документа и выигрывает.
    /// <list type="bullet">
    /// <item>REGULAR, 7 имён разной длины: по длине 615.6 нс, по ключу 705.8 -
    /// длина лучше на 15%; порождённый код дал 619.1, то есть выбрал длину;</item>
    /// <item>WIDE, 30 имён одной длины: по длине 793.0, по ключу 617.4 - ключ
    /// лучше на 28%; порождённый код дал 619.9, то есть выбрал ключ.</item>
    /// </list>
    /// Сам <b>порог</b> измерен на 2, 4 и 8 членах (цепочка выигрывает везде)
    /// и на 30 (выигрывает ключ); перелом между ними не найден. Как из этого
    /// выбрана 24 - в комментарии к <see cref="KeySwitchThreshold"/>.
    /// </summary>
    public static class NameDispatcher
    {
        public const string Scan = ValueSourceProducer.Scan;
        public const string Mem = ValueSourceProducer.Mem;
        public const string NameKey = "global::JsonGoddess.Internal.JsonNameKey";

        /// <summary>
        /// Начиная со скольких членов в одной корзине цепочка сравнений
        /// уступает место <c>switch</c>'у по ключу.
        ///
        /// <b>Здесь стояла четвёрка, и она стояла не там</b> - это измерено
        /// дважды, §12.3 и §12.4 плана. На именах по семь байт, где ключ
        /// <b>полон</b> и сравнения байтов после него нет вовсе, цепочка
        /// всё равно выигрывает у ключа:
        /// <list type="bullet">
        /// <item>2 члена - ключ 1.28 (прошлый прогон 1.49);</item>
        /// <item>4 члена - ключ 1.53 (1.51);</item>
        /// <item>8 членов - ключ 2.09 (1.90).</item>
        /// </list>
        /// То есть для корзин 4..8 генератор выбирал форму в полтора-два раза
        /// хуже той, которую умеет печатать. На тридцати членах (WIDE, §12.2 и
        /// §12.4) ключ, наоборот, выигрывает: 584.1 нс против 763.1 у цепочки.
        ///
        /// <b>Перелом так и не измерен</b> - он лежит где-то между 9 и 29, и
        /// вывести его из имеющихся точек нельзя: подгонка квадратичной модели
        /// по трём корзинам даёт отрицательный коэффициент при N^2, то есть
        /// данные её не поддерживают вовсе (корзины 2/4/8 - разные типы с
        /// разными постоянными на документ, а полоса неразличимости стенда
        /// около 8%, §12.5).
        ///
        /// Поэтому <b>24 выбрана правилом, а не подгонкой</b>, и правило
        /// такое:
        /// <list type="number">
        /// <item><b>Цена ошибки несимметрична.</b> Ключ там, где лучше
        /// цепочка, стоил измеренные 2.09x. Цепочка там, где лучше ключ,
        /// стоит измеренные 1.31x. Ошибаться в сторону цепочки дешевле.</item>
        /// <item><b>Частота ошибки несимметрична.</b> Корзина из 4-8 имён
        /// одной длины - обычное дело (Field00..Field05); корзина из 24+ имён
        /// одной длины - редкость.</item>
        /// <item><b>Тренд в данных смотрит вверх.</b> Проигрыш ключа не
        /// убывает к восьми членам, а растёт: 1.28 -> 1.53 -> 2.09. Развернуться
        /// он обязан (при тридцати ключ уже выигрывает), но на восьми ещё
        /// уходит от нуля, а не приближается к нему.</item>
        /// </list>
        /// Все три довода указывают в верхнюю половину отрезка 9..29 - отсюда
        /// 24. Заметно шире брать нельзя: при 30 ключ измеренно выигрывает, и
        /// порог обязан оставить WIDE ключу.
        ///
        /// Число по-прежнему подлежит замеру - но теперь оно ошибается в
        /// дешёвую сторону, а не в дорогую.
        /// </summary>
        public const int KeySwitchThreshold = 24;

        private const string AsciiName = "global::JsonGoddess.Internal.JsonAsciiName";

        public static void Emit(
            SourceBuilder builder,
            IReadOnlyList<MemberModel> members,
            JsonFeature features,
            Action<MemberModel> emitBody
            )
        {
            //JsonFeature.CaseInsensitiveNames: свёрнутый по ASCII байт даёт ту
            //же длину, что и байт без свёртки (A..Z/a..z - однобайтовые), так
            //что бакет по name.Length остаётся верным ориентиром и с фичей.
            //
            //А вот switch по ключу внутри бакета - нет, и причина не в том,
            //что ключ неоднозначен: JsonNameKey.Compute - чистая функция от
            //байт, и для имени до семи байт одно значение ключа означает ровно
            //одну последовательность байт. Мешает обратное отношение. Ключ
            //считается по СЫРЫМ байтам входящего имени, а при свёрнутом
            //сравнении один член обязан принять все варианты регистра: "Id" -
            //это id, Id, iD, ID, то есть ЧЕТЫРЕ разных ключа на один case, а в
            //общем случае 2^k по числу латинских букв среди первых семи байт -
            //до 128 меток на один член. Таблица переходов от этого перестаёт
            //быть таблицей.
            //
            //Починить это можно - считать ключ по СВЁРНУТОМУ имени, приводя
            //каждый байт к нижнему регистру перед укладкой в ulong; тогда
            //снова один член - один ключ, а константу генератор свернёт тем же
            //правилом на компиляции.
            //
            //Ловушка там одна, и она стоит того, чтобы её назвать здесь:
            //свёртка - НЕ "сбросить бит 0x20 на восьми байтах разом". Этот бит
            //меняет не только буквы ('_' 0x5F -> DEL 0x7F, '[' -> '{'), а для
            //имени до семи байт ключ ПОЛОН и сравнения после него нет вовсе -
            //значит слепо свёрнутый ключ молча принял бы "a\x7Fb" за член
            //"A_B". Свёртка обязана быть побайтовой с условием, как в
            //JsonAsciiName.EqualsIgnoreCase: b|0x20 засчитывается, только если
            //результат попал в a..z.
            //
            //Цена не измерена - это восемь байт на каждое прочитанное имя,
            //которых сейчас нет, а цепочка на корзине из одного-двух членов,
            //вероятно, дешевле; граница меряется, как и порог выше (§9.12).
            //Пока включённая фича печатает цепочку EqualsIgnoreCase вне
            //зависимости от размера корзины.
            var caseInsensitive = (features & JsonFeature.CaseInsensitiveNames) != 0;

            var buckets = members
                .GroupBy(m => m.JsonNameUtf8.Length)
                .OrderBy(g => g.Key)
                .ToList();

            builder.OpenBlock("switch (name.Length)");

            foreach (var bucket in buckets)
            {
                var items = bucket.ToList();
                var useKey = !caseInsensitive && items.Count >= KeySwitchThreshold;

                builder.Line(
                    "case " + bucket.Key + ": //членов: " + items.Count + ", "
                    + (useKey ? "switch по ключу" : caseInsensitive ? "цепочка сравнений без учёта регистра" : "цепочка сравнений")
                    );
                builder.OpenBlock();

                if (useKey)
                {
                    EmitByKey(builder, items, emitBody);
                }
                else
                {
                    EmitByChain(builder, items, emitBody, caseInsensitive);
                }

                builder.Line("break;");
                builder.CloseBlock();
                builder.Line();
            }

            builder.CloseBlock();
        }

        private static void EmitByChain(
            SourceBuilder builder,
            IReadOnlyList<MemberModel> members,
            Action<MemberModel> emitBody,
            bool caseInsensitive
            )
        {
            foreach (var member in members)
            {
                var comparison = caseInsensitive
                    ? AsciiName + ".EqualsIgnoreCase(name, " + SourceBuilder.Utf8Literal(member.JsonName) + ")"
                    : Mem + ".SequenceEqual(name, " + SourceBuilder.Utf8Literal(member.JsonName) + ")";

                builder.OpenBlock("if (" + comparison + ")");
                emitBody(member);
                builder.Line("goto next;");
                builder.CloseBlock();
                builder.Line();
            }
        }

        private static void EmitByKey(
            SourceBuilder builder,
            IReadOnlyList<MemberModel> members,
            Action<MemberModel> emitBody
            )
        {
            //двух case с одной константой в C# не бывает, а два имени от восьми
            //байт с одинаковыми первыми семью дают один ключ - такие члены
            //обязаны собраться в одну ветку и разделиться внутри сравнением
            var groups = members
                .GroupBy(m => JsonNameUtf8.ComputeKey(m.JsonNameUtf8))
                .ToList();

            builder.OpenBlock("switch (" + NameKey + ".Compute(name))");

            foreach (var group in groups)
            {
                var items = group.ToList();
                var exact = JsonNameUtf8.IsKeyExact(items[0].JsonNameUtf8);

                builder.Line(
                    "case 0x" + group.Key.ToString("X16") + "UL: //"
                    + string.Join(", ", items.Select(m => m.JsonName))
                    + (exact ? " - ключ полон, сравнение не нужно" : " - ключ лишь префильтр")
                    );
                builder.OpenBlock();

                if (exact)
                {
                    emitBody(items[0]);
                    builder.Line("goto next;");
                }
                else
                {
                    foreach (var member in items)
                    {
                        builder.OpenBlock("if (" + Mem + ".SequenceEqual(name, " + SourceBuilder.Utf8Literal(member.JsonName) + "))");
                        emitBody(member);
                        builder.Line("goto next;");
                        builder.CloseBlock();
                        builder.Line();
                    }

                    builder.Line("break;");
                }

                builder.CloseBlock();
                builder.Line();
            }

            builder.CloseBlock();
        }
    }
}
