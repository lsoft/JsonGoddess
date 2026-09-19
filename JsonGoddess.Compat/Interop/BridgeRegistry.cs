using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JsonGoddess.Compat.Interop
{
    /// <summary>
    /// Список обслуживаемых типов - для резолвера, который знает только
    /// <see cref="Type"/> и не может обратиться к
    /// <c>BridgeBinding&lt;T&gt;</c> напрямую.
    ///
    /// <para>
    /// Словарь здесь не стои́т ничего: резолвера эталон спрашивает <b>один
    /// раз на тип и экземпляр опций</b> и результат кеширует у себя. На пути
    /// сериализации этого кода нет вовсе - там уже лежит готовый
    /// <see cref="JsonTypeInfo"/> с нашим конвертером. Именно поэтому здесь
    /// можно то, чего нельзя в <see cref="CompatBinding{T}"/>.
    /// </para>
    /// </summary>
    internal static class BridgeRegistry
    {
        private static readonly object Gate = new object();

        /// <summary>
        /// Том менять целиком, а не по месту: читают его без блокировки, из
        /// резолвера, который может быть вызван из нескольких потоков сразу.
        /// Записей единицы и бывают они один раз, при инициализации модуля.
        /// </summary>
        private static Dictionary<Type, Func<JsonSerializerOptions, JsonTypeInfo>> _factories =
            new Dictionary<Type, Func<JsonSerializerOptions, JsonTypeInfo>>();

        internal static void Add<T>()
        {
            lock (Gate)
            {
                var copy = new Dictionary<Type, Func<JsonSerializerOptions, JsonTypeInfo>>(_factories)
                {
                    [typeof(T)] = static options =>
                        JsonMetadataServices.CreateValueInfo<T>(options, new BridgeConverter<T>()),
                };

                _factories = copy;
            }
        }

        internal static void Remove(Type type)
        {
            lock (Gate)
            {
                if (!_factories.ContainsKey(type))
                {
                    return;
                }

                var copy = new Dictionary<Type, Func<JsonSerializerOptions, JsonTypeInfo>>(_factories);
                copy.Remove(type);
                _factories = copy;
            }
        }

        internal static bool TryGet(Type type, out Func<JsonSerializerOptions, JsonTypeInfo>? factory)
        {
            return _factories.TryGetValue(type, out factory);
        }

        /// <summary>
        /// Сколько типов обслуживает мост. Существует ради тестов и ради
        /// человека, который хочет убедиться, что генератор вообще отработал.
        /// </summary>
        public static int Count => _factories.Count;
    }
}
