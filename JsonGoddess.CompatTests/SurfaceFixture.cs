using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace JsonGoddess.CompatTests
{
    /// <summary>
    /// Поверхность фасада против поверхности эталона - рефлексией, а не
    /// списком.
    ///
    /// <para>
    /// Drop-in проверяется не тем, что документ совпал, а тем, что чужой код
    /// вообще собрался. Не повторённая перегрузка - это ошибка компиляции у
    /// потребителя, и узнать о ней он должен не в тот день, когда решит
    /// попробовать.
    /// </para>
    ///
    /// <para>
    /// Список пишется рефлексией по той же причине, по которой
    /// <c>CompatOptions</c> обходит свойства, а не перечисляет их: эталон
    /// растёт. Перегрузка, добавленная в следующей версии, обязана красить
    /// этот тест, а не ждать, пока о ней вспомнят.
    /// </para>
    ///
    /// <para>
    /// И поэтому же тест идёт на трёх таргетах: поверхность у эталона <b>разная
    /// по таргетам</b> (<c>PipeReader</c>-перегрузки появились в 9.0,
    /// <c>AllowDuplicateProperties</c> - в 10.0). Одного прогона хватило бы,
    /// чтобы поверить в паритет, которого нет.
    /// </para>
    /// </summary>
    public class SurfaceFixture
    {
        private static string Name(Type type)
        {
            if (type.IsGenericParameter)
            {
                //T, TValue - по позиции, а не по имени: имя параметра типа у
                //нас и у них совпадать не обязано
                return "!" + type.GenericParameterPosition;
            }

            if (type.IsByRef)
            {
                return Name(type.GetElementType()!) + "&";
            }

            if (type.IsArray)
            {
                return Name(type.GetElementType()!) + "[]";
            }

            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }

            var arity = type.Name.IndexOf('`');
            var bare = arity >= 0 ? type.Name.Substring(0, arity) : type.Name;

            return (type.Namespace is null ? bare : type.Namespace + "." + bare)
                + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }

        /// <summary>
        /// Подпись без возвращаемого типа. Возвращаемый тип в перегрузку не
        /// входит, а вот разойтись он может только у тех методов, где мы и так
        /// обязаны совпасть буквально, - там это поймает компилятор теста.
        /// </summary>
        private static string Signature(MethodInfo method)
        {
            var generics = method.IsGenericMethodDefinition
                ? "<" + method.GetGenericArguments().Length + ">"
                : string.Empty;

            return method.Name + generics
                + "(" + string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType))) + ")";
        }

        private static HashSet<string> SurfaceOf(Type type)
        {
            return new HashSet<string>(
                type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(Signature),
                StringComparer.Ordinal
                );
        }

        [Fact]
        public void Every_overload_of_the_reference_exists_on_the_facade()
        {
            var theirs = SurfaceOf(typeof(global::System.Text.Json.JsonSerializer));
            var ours = SurfaceOf(typeof(global::JsonGoddess.Compat.JsonSerializer));

            var missing = theirs.Except(ours, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.True(
                missing.Count == 0,
                "фасад не повторяет " + missing.Count + " перегрузок эталона:"
                + Environment.NewLine + string.Join(Environment.NewLine, missing)
                );
        }

        /// <summary>
        /// Обратное направление - не паритет, а гигиена: лишний публичный
        /// статический метод на фасаде означает, что мы ушли от подписи
        /// эталона и потребитель, вернувшийся к эталону, соберётся не так.
        /// </summary>
        [Fact]
        public void The_facade_adds_nothing_of_its_own()
        {
            var theirs = SurfaceOf(typeof(global::System.Text.Json.JsonSerializer));
            var ours = SurfaceOf(typeof(global::JsonGoddess.Compat.JsonSerializer));

            var extra = ours.Except(theirs, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.True(
                extra.Count == 0,
                "на фасаде есть " + extra.Count + " методов, которых у эталона нет:"
                + Environment.NewLine + string.Join(Environment.NewLine, extra)
                );
        }

        /// <summary>
        /// Единственное расхождение с эталоном, и оно намеренное: пятнадцать
        /// его методов - расширения, наши одноимённые - нет.
        ///
        /// <para>
        /// Псевдоним типа расширения не подменяет: они ищутся по
        /// импортированным пространствам имён, а не по имени класса. Пометь мы
        /// первый параметр <c>this</c> - у потребителя, импортировавшего и
        /// <c>System.Text.Json</c>, и <c>JsonGoddess.Compat</c>, вызов
        /// <c>document.Deserialize&lt;T&gt;()</c> стал бы неоднозначным. Что
        /// это не догадка, а факт, показала сборка: пока <c>this</c> стоял,
        /// <c>ForwardingFixture</c> не компилировался с тремя CS0121.
        /// </para>
        ///
        /// <para>
        /// Потери нет: все пятнадцать уходят эталону целиком, то есть вызов,
        /// связавшийся с его расширением, делает ровно то же самое.
        /// </para>
        /// </summary>
        [Fact]
        public void The_facade_declares_no_extension_methods()
        {
            var extensions = typeof(global::JsonGoddess.Compat.JsonSerializer)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                .Select(Signature)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                extensions.Count == 0,
                "фасад объявил " + extensions.Count + " методов-расширений - каждый из них отберёт у"
                + " потребителя форму document.Deserialize<T>():"
                + Environment.NewLine + string.Join(Environment.NewLine, extensions)
                );

            //а у эталона они есть - и именно пятнадцать. Если однажды не
            //станет, исчезнет и повод для этого теста
            var theirs = typeof(global::System.Text.Json.JsonSerializer)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Count(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false));

            Assert.Equal(15, theirs);
        }
    }
}
