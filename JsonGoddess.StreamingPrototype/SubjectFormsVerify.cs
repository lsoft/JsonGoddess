using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JsonGoddess.StreamingPrototype.Generated;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.StreamingPrototype
{
    /// <summary>
    /// Формы субъекта, которых потоковый читатель не брал до O11: полиморфный
    /// тип и субъект-коллекция (PLAN.md §15, O11 а и б).
    ///
    /// <para>
    /// Проверяется не то, что порождённый код компилируется, - это сказал бы и
    /// текстовый тест, - а то, что он <b>читает то же самое</b>. Ожидание
    /// нигде не записано литералом: его даёт <c>System.Text.Json</c> прямо
    /// здесь, разбирая то же тело.
    /// </para>
    ///
    /// <para>
    /// Главная нагрузка - <b>обрыв на каждом байте</b>. У полиморфного
    /// читателя она бьёт по месту, которого у прочих нет вовсе: окно может
    /// кончиться посреди имени дискриминатора, между ним и двоеточием, посреди
    /// его значения - и тогда попытка обязана переиграться целиком, а не
    /// доложить наполовину разобранный тип.
    /// </para>
    /// </summary>
    internal static class SubjectFormsVerify
    {
        private static readonly int[] Chunks = { 1, 2, 3, 7, 13, 64, 4096, int.MaxValue, };

        internal static async Task All()
        {
            Console.WriteLine("=== Полиморфный тип и субъект-коллекция потоком ===");

            await Roots();
            await Members();
            await CollectionSubjects();
            await Refusals();

            Console.WriteLine();
        }

        /// <summary>
        /// Субъект-коллекция членом - обе формы сразу (PLAN.md §15, O11 (б)).
        ///
        /// <para>
        /// Вместе с полиморфным членом в одном типе нарочно: это два разных
        /// условия в одной неподвижной точке, и проверять их порознь значило бы
        /// не проверить, что они уживаются.
        /// </para>
        /// </summary>
        private static async Task CollectionSubjects()
        {
            var canvas = new Canvas
            {
                Id = 11,
                Colors = new Palette { "красный", "зелёный", "с кавычкой \" и юникодом é中😀", },
                Weights = new Weights { ["толщина"] = 3, ["прозрачность"] = 70, },
                Cover = new Square { Id = 12, Label = "обложка", Side = 8, },
            };

            var body = Reference.SerializeToUtf8Bytes(canvas, Verify.Web);
            var expected = Reference.Serialize(Reference.Deserialize<Canvas>(body, Verify.Web), Verify.Web);

            foreach (var chunk in Chunks)
            {
                var pipe = new Dribble(body, true, chunk);

                try
                {
                    var one = await ShapeHost.StreamReadOne_JsonGoddess_StreamingPrototype_Canvas(
                        DefaultInjector.Instance, pipe
                        );

                    Verify.Check(
                        Reference.Serialize(one, Verify.Web) == expected,
                        "субъект-коллекция членом, кусок " + Name(chunk)
                        );
                }
                catch (Exception error)
                {
                    Verify.Check(false, "субъект-коллекция членом, кусок " + Name(chunk) + ": " + error.Message);
                }
            }
        }

        /// <summary>
        /// Два отказа, которых нет больше нигде: дискриминатор не первым
        /// свойством и дискриминатор, которого мы не знаем.
        ///
        /// <para>
        /// Утверждение здесь не «мы отказываем», а <b>«мы отказываем там же,
        /// где эталон»</b>: принять документ, который он отвергает, значило бы
        /// прочесть то, чего он не читает, а отвергнуть принятый им - потерять
        /// запрос на ровном месте. Поэтому в каждой строке спрашивается и он.
        /// </para>
        /// </summary>
        private static async Task Refusals()
        {
            var bodies = new (string Json, string What)[]
            {
                ("[{\"id\":1,\"$type\":\"circle\",\"radius\":1}]", "дискриминатор не первым свойством"),
                ("[{\"$type\":\"trapezoid\",\"id\":1}]", "неизвестный дискриминатор"),
            };

            foreach (var (json, what) in bodies)
            {
                var body = System.Text.Encoding.UTF8.GetBytes(json);

                var referenceRefused = false;

                try
                {
                    Reference.Deserialize<Shape[]>(body, Verify.Web);
                }
                catch (System.Text.Json.JsonException)
                {
                    referenceRefused = true;
                }

                var weRefused = false;

                try
                {
                    await ShapeHost.StreamReadArray_JsonGoddess_StreamingPrototype_Shape(
                        DefaultInjector.Instance, new Dribble(body, true)
                        );
                }
                catch (JsonDocumentException)
                {
                    weRefused = true;
                }

                Verify.Check(
                    referenceRefused == weRefused,
                    what + ": эталон " + (referenceRefused ? "отверг" : "принял")
                    + ", мы " + (weRefused ? "отвергли" : "приняли")
                    );
            }
        }

        /// <summary>
        /// Корень-массив полиморфных элементов. База вперемешку с производными
        /// нарочно: диспетчер обязан уметь и откатиться к базе, и уйти в тело.
        /// </summary>
        private static async Task Roots()
        {
            var shapes = new Shape[]
            {
                new Shape { Id = 1, Label = "база", },
                new Circle { Id = 2, Label = "круг", Radius = 2.5, },
                new Square
                {
                    Id = 3,
                    Label = "квадрат с кавычкой \" и юникодом é中😀",
                    Side = 4,
                    Corners = new List<string> { "a", "b", },
                },
                new Circle { Id = 4, Label = null, Radius = 0, },
            };

            var body = Reference.SerializeToUtf8Bytes(shapes, Verify.Web);
            var expected = Reference.Serialize(Reference.Deserialize<Shape[]>(body, Verify.Web), Verify.Web);

            foreach (var chunk in Chunks)
            {
                foreach (var segmented in new[] { false, true, })
                {
                    var pipe = new Dribble(body, segmented, chunk);

                    try
                    {
                        var items = await ShapeHost.StreamReadArray_JsonGoddess_StreamingPrototype_Shape(
                            DefaultInjector.Instance, pipe
                            );

                        Verify.Check(
                            Reference.Serialize(items, Verify.Web) == expected,
                            "корень-массив, кусок " + Name(chunk) + ", "
                            + (segmented ? "сегмент на байт" : "один сегмент")
                            );
                    }
                    catch (Exception error)
                    {
                        Verify.Check(false, "корень-массив, кусок " + Name(chunk) + ": " + error.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Полиморфный тип <b>членом</b> - и одиночным, и в списке. До починки
        /// один такой член снимал обслуживание со всего графа над собой, то
        /// есть этот корень читался бы эталоном целиком.
        /// </summary>
        private static async Task Members()
        {
            var drawing = new Drawing
            {
                Id = 7,
                Cover = new Circle { Id = 8, Label = "обложка", Radius = 1.5, },
                Shapes = new List<Shape>
                {
                    new Square { Id = 9, Label = "первый", Side = 3, },
                    new Shape { Id = 10, Label = "второй", },
                },
            };

            var body = Reference.SerializeToUtf8Bytes(drawing, Verify.Web);
            var expected = Reference.Serialize(Reference.Deserialize<Drawing>(body, Verify.Web), Verify.Web);

            foreach (var chunk in Chunks)
            {
                var pipe = new Dribble(body, true, chunk);

                try
                {
                    var one = await ShapeHost.StreamReadOne_JsonGoddess_StreamingPrototype_Drawing(
                        DefaultInjector.Instance, pipe
                        );

                    Verify.Check(
                        Reference.Serialize(one, Verify.Web) == expected,
                        "полиморфный член, кусок " + Name(chunk)
                        );
                }
                catch (Exception error)
                {
                    Verify.Check(false, "полиморфный член, кусок " + Name(chunk) + ": " + error.Message);
                }
            }
        }

        private static string Name(int chunk) =>
            chunk == int.MaxValue ? "тело целиком" : chunk + " Б";
    }
}
