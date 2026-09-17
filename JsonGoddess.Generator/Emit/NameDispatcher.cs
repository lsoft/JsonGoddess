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
        /// уступает место <c>switch</c>'у по ключу. Значение выбрано между
        /// двумя измеренными точками (2 члена в корзине - выиграла цепочка,
        /// 30 - выиграл ключ) и само по себе не измерено.
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
            //Починить это можно - считать ключ по СВЁРНУТОМУ имени (свёртка
            //ASCII делается прямо в упаковке), и тогда снова один член - один
            //ключ. Это второй вариант Compute плюс свёрнутые константы у
            //генератора, и цена его не измерена: замер стоимости включённых
            //опций отложен целиком (§9.12). Пока включённая фича печатает
            //цепочку EqualsIgnoreCase вне зависимости от размера корзины.
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
