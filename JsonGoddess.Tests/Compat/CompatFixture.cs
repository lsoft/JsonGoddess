using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using JsonGoddess.Compat;
using JsonGoddess.Tests.Generated;
using Xunit;

using Facade = JsonGoddess.Compat.JsonSerializer;
using Reference = System.Text.Json.JsonSerializer;

namespace JsonGoddess.Tests.Compat
{
    /// <summary>
    /// Фасад <c>JsonGoddess.Compat.JsonSerializer</c> (§10, маршрут A) -
    /// отдельно от генератора.
    ///
    /// <para>
    /// Быстрый путь здесь регистрируется <b>руками</b>, ровно тем же вызовом
    /// <see cref="CompatBinding{T}.Register"/>, который потом будет печатать
    /// генератор в <c>[ModuleInitializer]</c>. Это единственный способ
    /// проверить фасад как таковой: пока генератора нет, а когда он появится,
    /// эти тесты продолжат проверять именно фасад, а не его вместе с
    /// генератором.
    /// </para>
    ///
    /// <para>
    /// Типы для «обслуживаем» и «не обслуживаем» разные и регистрация никогда
    /// не снимается. <see cref="CompatBinding{T}"/> - глобальное состояние
    /// процесса, а xUnit гоняет классы параллельно: тест, который
    /// регистрировал бы и разрегистрировал один тип, менял бы ответ соседа в
    /// зависимости от того, кто успел первым.
    /// </para>
    /// </summary>
    public class CompatFixture
    {
        /// <summary>
        /// Тип, для которого быстрый путь зарегистрирован. Регистрация в
        /// статическом конструкторе: она обязана произойти один раз и до
        /// первого теста, кто бы из них ни пошёл первым.
        /// </summary>
        static CompatFixture()
        {
            CompatBinding<Flat>.Register(Write, Read);
        }

        private static byte[] Write(Flat? value)
        {
            using (var exhauster = new PooledUtf8Exhauster())
            {
                FlatSerializer.Serialize(exhauster, value);
                return exhauster.ToArray();
            }
        }

        private static Flat Read(ReadOnlySpan<byte> utf8)
        {
            FlatSerializer.Deserialize(DefaultInjector.Instance, utf8, out Flat? result);

            //документ "null" даёт здесь null, и это законный результат чтения;
            //делегат объявлен по типу субъекта, а не по его nullable-форме
            return result!;
        }

        /// <summary>
        /// Тип, который фасад обслужить не умеет: для него быстрый путь не
        /// регистрируется никогда. Собственный, а не одолженный у соседней
        /// фикстуры, - чтобы никакой чужой тест не мог его зарегистрировать.
        /// </summary>
        public class Unserved
        {
            public int Id
            {
                get;
                set;
            }

            public string? Name
            {
                get;
                set;
            }
        }

        // ---------- быстрый путь берётся и даёт то же, что эталон ----------

        [Fact]
        public void A_bound_type_is_written_exactly_as_the_reference_writes_it()
        {
            var value = Flat.CreateSample();

            Assert.True(CompatBinding<Flat>.IsBound);
            Assert.Equal(
                Reference.Serialize(value, Stj.Reference.Relaxed),
                Facade.Serialize(value)
                );
        }

        [Fact]
        public void A_bound_type_is_read_back_into_the_same_object_as_the_reference_reads()
        {
            var json = Reference.Serialize(Flat.CreateSample(), Stj.Reference.Relaxed);

            var ours = Facade.Deserialize<Flat>(json);
            var theirs = Reference.Deserialize<Flat>(json);

            Assert.Equal(
                Reference.Serialize(theirs, Stj.Reference.Relaxed),
                Reference.Serialize(ours, Stj.Reference.Relaxed)
                );
        }

        [Fact]
        public void The_utf8_overloads_go_through_the_fast_path_without_a_copy()
        {
            var value = Flat.CreateSample();
            var utf8 = Facade.SerializeToUtf8Bytes(value);

            Assert.Equal(Reference.SerializeToUtf8Bytes(value, Stj.Reference.Relaxed), utf8);

            var back = Facade.Deserialize<Flat>(new ReadOnlySpan<byte>(utf8));
            Assert.Equal(value.Id, back!.Id);
            Assert.Equal(value.Customer, back.Customer);
        }

        [Fact]
        public void The_char_span_overload_goes_through_the_fast_path()
        {
            var json = Reference.Serialize(Flat.CreateSample(), Stj.Reference.Relaxed);

            var back = Facade.Deserialize<Flat>(json.AsSpan());

            Assert.Equal(Flat.CreateSample().Id, back!.Id);
        }

        [Fact]
        public void The_stream_overloads_go_through_the_fast_path()
        {
            var value = Flat.CreateSample();

            using (var written = new MemoryStream())
            {
                Facade.Serialize(written, value);

                Assert.Equal(Reference.SerializeToUtf8Bytes(value, Stj.Reference.Relaxed), written.ToArray());

                written.Position = 0;
                var back = Facade.Deserialize<Flat>(written);
                Assert.Equal(value.Reference, back!.Reference);
            }
        }

        [Fact]
        public void The_writer_overload_hands_the_ready_document_to_the_reference_writer()
        {
            var value = Flat.CreateSample();

            using (var buffer = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartArray();
                    Facade.Serialize(writer, value);
                    writer.WriteEndArray();
                }

                //учёт запятых и скобок остаётся за писателем эталона, а тело
                //значения приходит от нас
                var expected = "[" + Reference.Serialize(value, Stj.Reference.Relaxed) + "]";
                Assert.Equal(expected, Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }

        // ---------- незнакомый тип продолжает работать ----------

        [Fact]
        public void An_unbound_type_is_served_by_the_reference_and_keeps_working()
        {
            var value = new Unserved { Id = 42, Name = "сорок два", };

            Assert.False(CompatBinding<Unserved>.IsBound);
            Assert.Equal(Reference.Serialize(value), Facade.Serialize(value));

            var back = Facade.Deserialize<Unserved>(Reference.Serialize(value));
            Assert.Equal(42, back!.Id);
            Assert.Equal("сорок два", back.Name);
        }

        [Fact]
        public void An_unbound_type_that_the_reference_itself_refuses_fails_the_same_way()
        {
            //фасад не должен превращать чужой отказ в свой: он этот вызов даже
            //не трогает
            Assert.Throws<JsonException>(() => Facade.Deserialize<Unserved>("{\"Id\":"));
        }

        // ---------- чужие опции отменяют быстрый путь ----------

        [Fact]
        public void Options_that_are_not_the_default_send_even_a_bound_type_to_the_reference()
        {
            var value = Flat.CreateSample();
            var indented = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            //если бы быстрый путь тут сработал, документ был бы без отступов -
            //валидный и не тот, о котором просили
            Assert.Equal(
                Reference.Serialize(value, indented),
                Facade.Serialize(value, indented)
                );
            Assert.Contains("\n", Facade.Serialize(value, indented));
        }

        [Fact]
        public void A_custom_encoder_sends_a_bound_type_to_the_reference()
        {
            //Энкодер, у которого разрешена только базовая латиница: кириллица
            //уходит у него в \uXXXX, а наш sink пишет её как есть
            //(docs/stj-divergences.md §1.1). На этой паре и видно, что фасад
            //именно отдал работу, а не сделал её сам.
            //
            //Раньше здесь стоял JavaScriptEncoder.Default, и это перестало
            //быть примером: явно выписанное умолчание - не чужая настройка, и
            //CompatOptions теперь засчитывает его умолчанием. Пример
            //потребовался настоящий.
            var value = new Flat { Customer = "Ёжик", };
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin),
            };

            Assert.False(CompatOptions.IsDefault(options));
            Assert.Equal(Reference.Serialize(value, options), Facade.Serialize(value, options));
            Assert.Contains("\\u0401", Facade.Serialize(value, options));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_default_options_instances_do_not_disable_the_fast_path(bool useStaticDefault)
        {
            var options = useStaticDefault ? JsonSerializerOptions.Default : new JsonSerializerOptions();

            Assert.True(CompatOptions.IsDefault(options));
        }

        [Fact]
        public void Null_options_cost_nothing_and_mean_the_default()
        {
            Assert.True(CompatOptions.IsDefault(null));
        }

        [Fact]
        public void An_options_instance_stays_default_after_the_reference_has_frozen_it()
        {
            //эталон при первом использовании делает опции readonly и заводит
            //резолвер; вердикт про них от этого меняться не должен
            var options = new JsonSerializerOptions();
            Reference.Serialize(new Unserved(), options);

            Assert.True(options.IsReadOnly);
            Assert.True(CompatOptions.IsDefault(options));
        }

        // ---------- энкодер: одно послабление, и оно доказано ----------

        /// <summary>
        /// <c>Encoder = JavaScriptEncoder.Default</c> - это не чужая
        /// настройка, а умолчание, выписанное явно.
        ///
        /// <para>
        /// Ложный отказ здесь не теоретический: minimal API выставляет
        /// энкодер именно так, и без этого послабления Compat-слой отступал бы
        /// в ASP.NET по причине, которой нет.
        /// </para>
        ///
        /// <para>
        /// Утверждение снимается с эталона, а не объявляется: сперва
        /// проверяется, что байты совпадают, и только потом - что вердикт
        /// «умолчание». Порядок важен. Если эталон однажды разведёт эти два
        /// энкодера, первый <c>Assert</c> покраснеет раньше, чем послабление
        /// успеет пропустить чужое поведение на быстрый путь.
        /// </para>
        /// </summary>
        [Fact]
        public void The_default_encoder_written_out_explicitly_is_still_the_default()
        {
            var explicitEncoder = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Default,
            };

            var value = new Unserved { Name = "Ёжик <b>&</b> 'кавычки' + плюс", };

            Assert.Equal(
                Reference.Serialize(value),
                Reference.Serialize(value, explicitEncoder)
                );

            Assert.True(CompatOptions.IsDefault(explicitEncoder));
        }

        /// <summary>
        /// Послабление узкое: засчитывается ровно один экземпляр. Любой другой
        /// энкодер меняет байты, и <c>UnsafeRelaxedJsonEscaping</c> - первый
        /// тому пример.
        /// </summary>
        [Fact]
        public void Any_other_encoder_is_still_a_no()
        {
            var relaxed = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            var value = new Unserved { Name = "Ёжик", };

            Assert.NotEqual(Reference.Serialize(value), Reference.Serialize(value, relaxed));
            Assert.False(CompatOptions.IsDefault(relaxed));
        }

        // ---------- «сравниваем всё» - проверено, а не обещано ----------

        [Fact]
        public void Every_option_of_the_reference_is_either_compared_or_handled_by_name()
        {
            //пять свойств разобраны отдельно, остальные сравниваются в лоб.
            //Если эталон заведёт новое - оно попадёт в сравнение само, и это
            //число сойдётся; если кто-то добавит исключение руками - разойдётся
            var all = typeof(JsonSerializerOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Count(p => p.CanRead && p.GetIndexParameters().Length == 0);

            Assert.Equal(all - 5, CompatOptions.ComparedPropertyCount);
        }

        private sealed class OwnResolver : IJsonTypeInfoResolver
        {
            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                return new DefaultJsonTypeInfoResolver().GetTypeInfo(type, options);
            }
        }

        private static bool TryDifferentValue(PropertyInfo property, JsonSerializerOptions from, out object? value)
        {
            object? current;
            try
            {
                current = property.GetValue(from);
            }
            catch (Exception)
            {
                value = null;
                return false;
            }

            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (type == typeof(bool))
            {
                value = !(bool)current!;
                return true;
            }

            if (type == typeof(int))
            {
                value = (int)current! + 1;
                return true;
            }

            if (type == typeof(char))
            {
                value = (char)current! == ' ' ? '\t' : ' ';
                return true;
            }

            if (type == typeof(string))
            {
                value = (string?)current == "\n" ? "\r\n" : "\n";
                return true;
            }

            if (type.IsEnum)
            {
                //перебором, а не «первое непохожее»: у эталона есть члены
                //перечисления, которые он сам же и отвергает при присваивании
                //(JsonIgnoreCondition.Always), и упереться в такой - не повод
                //считать свойство непроверяемым
                foreach (var candidate in Enum.GetValues(type))
                {
                    if (Equals(candidate, current))
                    {
                        continue;
                    }

                    try
                    {
                        property.SetValue(from, candidate);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    value = candidate;
                    return true;
                }

                value = null;
                return false;
            }

            if (type == typeof(JsonNamingPolicy))
            {
                value = JsonNamingPolicy.CamelCase;
                return true;
            }

            if (type == typeof(JavaScriptEncoder))
            {
                value = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                return true;
            }

            if (type == typeof(ReferenceHandler))
            {
                value = ReferenceHandler.Preserve;
                return true;
            }

            if (type == typeof(IJsonTypeInfoResolver))
            {
                value = new OwnResolver();
                return true;
            }

            value = null;
            return false;
        }

        [Fact]
        public void Any_option_set_away_from_its_default_disables_the_fast_path()
        {
            var skipped = new List<string>();

            foreach (var property in typeof(JsonSerializerOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var options = new JsonSerializerOptions();

                if (!TryDifferentValue(property, options, out var value))
                {
                    skipped.Add(property.Name);
                    continue;
                }

                try
                {
                    property.SetValue(options, value);
                }
                catch (Exception)
                {
                    skipped.Add(property.Name);
                    continue;
                }

                Assert.False(CompatOptions.IsDefault(options), property.Name + " не отменило быстрый путь");
            }

            //список пропущенных закреплён нарочно: без него тест, переставший
            //уметь строить отличающееся значение, молча проверял бы всё меньше
            Assert.Equal(Array.Empty<string>(), skipped.ToArray());
        }

        [Fact]
        public void A_converter_in_the_options_disables_the_fast_path()
        {
            //Converters - список, и он единственное свойство, которое нельзя
            //сравнить присваиванием: его правят методом Add
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());

            Assert.False(CompatOptions.IsDefault(options));
        }
    }
}
