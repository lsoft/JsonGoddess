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
    /// Сам <b>порог</b> по-прежнему не измерен: известно, что при 2 членах в
    /// корзине выигрывает цепочка, а при 30 - ключ, и четвёрка выбрана между
    /// ними. Цена ошибки в нём ограничена сверху измеренной вилкой 15%/28%.
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
        /// <b>Четвёрка стоит не там, и это измерено</b> (§12.3 плана). На
        /// именах по семь байт, где ключ полон и сравнения байтов после него
        /// нет вовсе, цепочка выигрывает у ключа на корзине из двух членов
        /// (1.49), из четырёх (1.51) и из восьми (1.90) - то есть для корзин
        /// 4..8 генератор сегодня выбирает форму в полтора-два раза хуже. На
        /// тридцати членах (WIDE, §12.2) ключ, наоборот, выигрывает 28%, и
        /// противоречия здесь нет: цепочка стоит примерно a*N + b*N^2/2 на
        /// документ, ключ - c*N, и до восьми членов квадратичная часть ещё
        /// мала. Перелом лежит между 9 и 29.
        ///
        /// Значение не поднято здесь и сейчас намеренно: это правка поведения
        /// для всех существующих хостов, и делать её надо отдельно, найдя
        /// перелом замером, а не сдвинув число наугад во второй раз.
        /// </summary>
        public const int KeySwitchThreshold = 4;

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
