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
        private static Dictionary<Type, Entry> _entries = new Dictionary<Type, Entry>();

        private sealed class Entry
        {
            public Func<JsonSerializerOptions, JsonTypeInfo>? Default;

            public Func<JsonSerializerOptions, JsonTypeInfo>? Web;

            public Entry Copy()
            {
                return new Entry { Default = Default, Web = Web, };
            }
        }

        internal static void Add<T>(BridgeProfile profile)
        {
            lock (Gate)
            {
                var copy = new Dictionary<Type, Entry>(_entries);

                var entry = copy.TryGetValue(typeof(T), out var existing)
                    ? existing.Copy()
                    : new Entry();

                Func<JsonSerializerOptions, JsonTypeInfo> factory = profile == BridgeProfile.Web
                    ? static options => JsonMetadataServices.CreateValueInfo<T>(
                        options,
                        new BridgeConverter<T>(BridgeProfile.Web)
                        )
                    : static options => JsonMetadataServices.CreateValueInfo<T>(
                        options,
                        new BridgeConverter<T>(BridgeProfile.Default)
                        );

                if (profile == BridgeProfile.Web)
                {
                    entry.Web = factory;
                }
                else
                {
                    entry.Default = factory;
                }

                copy[typeof(T)] = entry;
                _entries = copy;
            }
        }

        internal static void Remove(Type type)
        {
            lock (Gate)
            {
                if (!_entries.ContainsKey(type))
                {
                    return;
                }

                var copy = new Dictionary<Type, Entry>(_entries);
                copy.Remove(type);
                _entries = copy;
            }
        }

        internal static bool TryGet(
            Type type,
            BridgeProfile profile,
            out Func<JsonSerializerOptions, JsonTypeInfo>? factory
            )
        {
            factory = null;

            if (!_entries.TryGetValue(type, out var entry))
            {
                return false;
            }

            factory = profile == BridgeProfile.Web ? entry.Web : entry.Default;
            return factory is not null;
        }

        internal static bool Knows(Type type)
        {
            return _entries.ContainsKey(type);
        }

        /// <summary>
        /// Сколько типов обслуживает мост. Существует ради тестов и ради
        /// человека, который хочет убедиться, что генератор вообще отработал.
        /// </summary>
        public static int Count => _entries.Count;
    }
}
