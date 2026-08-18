using System;
using System.Collections.Generic;
using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using UnityEngine.Scripting;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// Resolves only KDI subscribable types. Compose it with an application's existing options
    /// through WithKDI instead of replacing MessagePackSerializer.DefaultOptions.
    /// </summary>
    [Preserve]
    public sealed class KDIMessagePackResolver : IFormatterResolver
    {
        public static readonly KDIMessagePackResolver Instance = new KDIMessagePackResolver();

        private KDIMessagePackResolver()
        {
        }

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            if (KDIMessagePackFormatterRegistry.TryGet(out IMessagePackFormatter<T> registered))
            {
                return registered;
            }

            return FormatterCache<T>.Formatter;
        }

        /// <summary>
        /// Compatibility helper. New code should compose its own base options with WithKDI.
        /// </summary>
        public static MessagePackSerializerOptions GetOptions()
            => MessagePackSerializerOptions.Standard.WithKDI();

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter =
                (IMessagePackFormatter<T>)ResolveFormatter(typeof(T));
        }

        private static object ResolveFormatter(Type type)
        {
            Type formatterType = null;
            string registrationHint = null;

            try
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableProperty<>))
                {
                    var argument = type.GetGenericArguments()[0];
                    registrationHint = $"{nameof(KDIMessagePackAot)}.{nameof(KDIMessagePackAot.RegisterProperty)}<{argument.FullName}>()";
                    formatterType = typeof(SubscribablePropertyFormatter<>).MakeGenericType(argument);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableCollection<>))
                {
                    var argument = type.GetGenericArguments()[0];
                    registrationHint = $"{nameof(KDIMessagePackAot)}.{nameof(KDIMessagePackAot.RegisterCollection)}<{argument.FullName}>()";
                    formatterType = typeof(SubscribableCollectionFormatter<>).MakeGenericType(argument);
                }
                else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableDictionary<,>))
                {
                    var arguments = type.GetGenericArguments();
                    registrationHint = $"{nameof(KDIMessagePackAot)}.{nameof(KDIMessagePackAot.RegisterDictionary)}<{arguments[0].FullName}, {arguments[1].FullName}>()";
                    formatterType = typeof(SubscribableDictionaryFormatter<,>).MakeGenericType(arguments);
                }

                if (formatterType == null) return null;
                return Activator.CreateInstance(formatterType);
            }
            catch (Exception exception)
            {
                throw new MessagePackSerializationException(
                    $"KDI could not construct a formatter for '{type.FullName}'. " +
                    $"On AOT/IL2CPP, register the closed generic formatter before serialization: {registrationHint}.",
                    exception);
            }
        }
    }

    public static class KDIMessagePackOptionsExtensions
    {
        /// <summary>
        /// Adds KDI formatters ahead of the existing resolver while preserving every other
        /// option (security, compression, string interning, old spec mode, and cancellation).
        /// </summary>
        public static MessagePackSerializerOptions WithKDI(this MessagePackSerializerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Resolver is KDIOptionsResolver) return options;
            return WithKDI(options, KDIMessagePackSettings.Default);
        }

        /// <summary>
        /// Adds KDI formatters and explicit KDI collection resource limits ahead of the
        /// existing resolver while preserving every other MessagePack option.
        /// </summary>
        public static MessagePackSerializerOptions WithKDI(
            this MessagePackSerializerOptions options,
            KDIMessagePackSettings settings)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (options.Resolver is KDIOptionsResolver existing)
            {
                if (existing.Settings.Equals(settings)) return options;
                return options.WithResolver(
                    new KDIOptionsResolver(existing.Fallback, settings));
            }

            return options.WithResolver(
                new KDIOptionsResolver(options.Resolver, settings));
        }

        [Preserve]
        private sealed class KDIOptionsResolver :
            IFormatterResolver,
            IKDIMessagePackSettingsProvider
        {
            private readonly IFormatterResolver _fallback;

            public KDIOptionsResolver(
                IFormatterResolver fallback,
                KDIMessagePackSettings settings)
            {
                _fallback = fallback ?? StandardResolver.Instance;
                Settings = settings;
            }

            public IFormatterResolver Fallback => _fallback;
            public KDIMessagePackSettings Settings { get; }

            public IMessagePackFormatter<T> GetFormatter<T>()
            {
                return KDIMessagePackResolver.Instance.GetFormatter<T>() ??
                       _fallback.GetFormatter<T>();
            }
        }
    }

    /// <summary>
    /// Closed-generic registrations for IL2CPP projects. Register concrete application types
    /// during startup before the resolver is queried.
    /// </summary>
    [Preserve]
    public static class KDIMessagePackAot
    {
        public static void RegisterProperty<T>()
            => KDIMessagePackFormatterRegistry.Register(
                SubscribablePropertyFormatter<T>.Instance);

        public static void RegisterCollection<T>()
            => KDIMessagePackFormatterRegistry.Register(
                SubscribableCollectionFormatter<T>.Instance);

        public static void RegisterDictionary<TKey, TValue>()
            => KDIMessagePackFormatterRegistry.Register(
                SubscribableDictionaryFormatter<TKey, TValue>.Instance);
    }

    internal static class KDIMessagePackFormatterRegistry
    {
        private static readonly Dictionary<Type, object> Formatters = new Dictionary<Type, object>();
        private static readonly object Sync = new object();

        public static void Register<T>(IMessagePackFormatter<T> formatter)
        {
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));
            lock (Sync) Formatters[typeof(T)] = formatter;
        }

        public static bool TryGet<T>(out IMessagePackFormatter<T> formatter)
        {
            lock (Sync)
            {
                if (Formatters.TryGetValue(typeof(T), out var value))
                {
                    formatter = (IMessagePackFormatter<T>)value;
                    return true;
                }
            }

            formatter = null;
            return false;
        }
    }

    /// <summary>
    /// Source-compatibility marker for the former automatic initializer. It intentionally has
    /// no runtime initialization hook and never mutates MessagePackSerializer.DefaultOptions.
    /// </summary>
    [Obsolete("Global MessagePack options are process-wide. Prefer application-owned options.WithKDI().", false)]
    public static class KDIMessagePackInitializer
    {
        public static MessagePackSerializerOptions GetOptions()
            => MessagePackSerializer.DefaultOptions.WithKDI();
    }
}
