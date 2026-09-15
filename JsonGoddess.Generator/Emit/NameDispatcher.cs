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
    /// net10, интервалы не перекрываются ни в одну сторону):
    /// <list type="bullet">
    /// <item>REGULAR, 7 членов с разными длинами: по длине 573.6 ± 29 ns,
    /// по ключу 651.3 ± 23 ns - ключ на 13.5% хуже;</item>
    /// <item>WIDE, 30 членов с именами по 7 байт: по длине 817.7 ± 39 ns,
    /// по ключу 587.2 ± 16 ns - ключ на 28% лучше.</item>
    /// </list>
    /// Механизм обеих сторон один: <c>switch</c> по длине попадает в член сразу,
    /// когда длины разводят членов по корзинам, и вырождается в линейную цепочку
    /// <c>SequenceEqual</c>, когда все имена в одной корзине. Ключ от разброса
    /// длин не зависит, но его надо вычислить, и там, где длина уже всё решила,
    /// это чистый расход.
    ///
    /// Отсюда гибрид: <c>switch</c> по длине верхним уровнем всегда, а внутри
    /// корзины, где членов больше порога, - <c>switch</c> по ключу вместо
    /// цепочки. <b>Сам гибрид не измерен</b>, и порог тем более; цена ошибки в
    /// нём ограничена сверху измеренной вилкой 13%/28%.
    /// </summary>
    public static class NameDispatcher
    {
        public const string Scan = ValueSourceProducer.Scan;
        public const string NameKey = "global::JsonGoddess.Internal.JsonNameKey";

        /// <summary>
        /// Начиная со скольких членов в одной корзине цепочка сравнений
        /// уступает место <c>switch</c>'у по ключу. Значение выбрано между
        /// двумя измеренными точками (2 члена в корзине - выиграла цепочка,
        /// 30 - выиграл ключ) и само по себе не измерено.
        /// </summary>
        public const int KeySwitchThreshold = 4;

        public static void Emit(SourceBuilder builder, IReadOnlyList<MemberModel> members, Action<MemberModel> emitBody)
        {
            var buckets = members
                .GroupBy(m => m.JsonNameUtf8.Length)
                .OrderBy(g => g.Key)
                .ToList();

            builder.OpenBlock("switch (name.Length)");

            foreach (var bucket in buckets)
            {
                var items = bucket.ToList();
                var useKey = items.Count >= KeySwitchThreshold;

                builder.Line(
                    "case " + bucket.Key + ": //членов: " + items.Count + ", "
                    + (useKey ? "switch по ключу" : "цепочка сравнений")
                    );
                builder.OpenBlock();

                if (useKey)
                {
                    EmitByKey(builder, items, emitBody);
                }
                else
                {
                    EmitByChain(builder, items, emitBody);
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
            Action<MemberModel> emitBody
            )
        {
            foreach (var member in members)
            {
                builder.OpenBlock("if (global::System.MemoryExtensions.SequenceEqual(name, " + SourceBuilder.Utf8Literal(member.JsonName) + "))");
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
                        builder.OpenBlock("if (global::System.MemoryExtensions.SequenceEqual(name, " + SourceBuilder.Utf8Literal(member.JsonName) + "))");
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
