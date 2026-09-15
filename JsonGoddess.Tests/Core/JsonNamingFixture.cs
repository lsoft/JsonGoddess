using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonGoddess.Internal;
using Xunit;

namespace JsonGoddess.Tests.Core
{
    /// <summary>
    /// Политики именования сверяются с <c>System.Text.Json</c> на корпусе имён.
    ///
    /// Это тот случай, где своя реализация опасна тише всего: алгоритм выглядит
    /// как «опустить первую букву», а на деле <c>ABCd</c> даёт <c>abCd</c>,
    /// <c>X509Certificate</c> - <c>x509Certificate</c> (цифра обрывает пробег
    /// заглавных), а <c>HTTPResponseCode</c> - <c>httpResponseCode</c>.
    /// Ошибиться здесь легко, и ошибка проявится документом, разошедшимся с
    /// эталонным на первом же <c>ID</c>.
    ///
    /// Своя реализация, а не вызов их <c>ConvertName</c> из генератора, выбрана
    /// сознательно: тащить <c>System.Text.Json</c> в контекст загрузки
    /// компилятора значило бы и приколотить алгоритм к <b>нашей</b> версии
    /// эталона, которая может не совпасть с рантайм-версией потребителя. Цена
    /// решения - вот этот тест.
    ///
    /// Оговорка, которую стоит помнить: связывание на компиляции делает
    /// <b>снимок</b> алгоритма. Если эталон его однажды поменяет, этот тест
    /// покраснеет - и расхождение станет видимым решением, а не тихим
    /// сюрпризом.
    /// </summary>
    public class JsonNamingFixture
    {
        /// <summary>
        /// Корпус подобран по границам алгоритма, а не по красоте: пробеги
        /// заглавных, цифры внутри и на конце, уже готовый snake_case,
        /// одиночная буква, не-ASCII, пустая строка, начало со строчной.
        /// </summary>
        public static readonly string[] Corpus =
        {
            "ID", "Id", "id", "IOStream", "X509Certificate", "ABC", "ABCd", "ABCdE",
            "Order1", "Order1Line", "OrderID", "IPAddress", "HTTPResponseCode",
            "A", "a", "AB", "aB", "Already_snake", "_leading", "trailing_",
            "With Space", "Two  Spaces", "Имя", "ИмяДва", "ИМЯ",
            "camelAlready", "PascalCase", "has1digit", "Has1Digit", "N1", "N1n",
            "", "X", "x9", "X9Y", "__", "a_B",
        };

        public static IEnumerable<object[]> Cases =>
            from name in Corpus
            from style in new[]
            {
                JsonNamingStyle.CamelCase,
                JsonNamingStyle.SnakeCaseLower,
                JsonNamingStyle.SnakeCaseUpper,
                JsonNamingStyle.KebabCaseLower,
                JsonNamingStyle.KebabCaseUpper,
            }
            select new object[] { name, style, };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Conversion_agrees_with_system_text_json(string name, JsonNamingStyle style)
        {
            Assert.Equal(Theirs(style).ConvertName(name), JsonNaming.Convert(name, style));
        }

        [Fact]
        public void None_leaves_the_name_alone()
        {
            foreach (var name in Corpus)
            {
                Assert.Equal(name, JsonNaming.Convert(name, JsonNamingStyle.None));
            }
        }

        /// <summary>
        /// Наше перечисление читается генератором из метаданных чужого атрибута
        /// числом, поэтому значения обязаны совпадать не только по имени.
        /// Разъехавшись, они дали бы не ошибку компиляции, а <b>другую
        /// политику</b> - молча.
        /// </summary>
        [Fact]
        public void Enum_values_match_the_ones_of_system_text_json()
        {
            foreach (var name in Enum.GetNames(typeof(JsonNamingStyle)))
            {
                var theirs = name == nameof(JsonNamingStyle.None)
                    ? JsonKnownNamingPolicy.Unspecified
                    : (JsonKnownNamingPolicy)Enum.Parse(typeof(JsonKnownNamingPolicy), name);

                var ours = (JsonNamingStyle)Enum.Parse(typeof(JsonNamingStyle), name);

                Assert.Equal((int)theirs, (int)ours);
            }

            //и наоборот: у них не должно появиться политики, которой мы не знаем
            Assert.Equal(
                Enum.GetValues(typeof(JsonKnownNamingPolicy)).Length,
                Enum.GetValues(typeof(JsonNamingStyle)).Length
                );
        }

        private static JsonNamingPolicy Theirs(JsonNamingStyle style)
        {
            switch (style)
            {
                case JsonNamingStyle.CamelCase: return JsonNamingPolicy.CamelCase;
                case JsonNamingStyle.SnakeCaseLower: return JsonNamingPolicy.SnakeCaseLower;
                case JsonNamingStyle.SnakeCaseUpper: return JsonNamingPolicy.SnakeCaseUpper;
                case JsonNamingStyle.KebabCaseLower: return JsonNamingPolicy.KebabCaseLower;
                case JsonNamingStyle.KebabCaseUpper: return JsonNamingPolicy.KebabCaseUpper;
                default: throw new ArgumentOutOfRangeException(nameof(style));
            }
        }
    }
}
