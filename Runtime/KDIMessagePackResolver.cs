using System;
using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using UnityEngine;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// KDI SubscribableProperty 타입들을 MessagePack에서 직렬화할 수 있도록 하는 리졸버.
    /// 런타임 초기화 시 자동으로 등록되므로 별도 설정이 필요 없다.
    /// </summary>
    public sealed class KDIMessagePackResolver : IFormatterResolver
    {
        public static readonly KDIMessagePackResolver Instance = new();

        private KDIMessagePackResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        /// <summary>
        /// 본 리졸버 + StandardResolver를 합성한 MessagePackSerializerOptions를 반환한다.
        /// 수동으로 옵션을 구성할 때 사용한다.
        /// </summary>
        public static MessagePackSerializerOptions GetOptions()
        {
            var composite = CompositeResolver.Create(
                Instance,
                StandardResolver.Instance
            );
            return MessagePackSerializerOptions.Standard.WithResolver(composite);
        }

        /// <summary>
        /// 제네릭 타입 캐시. 타입별로 한 번만 포맷터를 생성한다.
        /// </summary>
        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

            static FormatterCache()
            {
                Formatter = (IMessagePackFormatter<T>)ResolveFormatter(typeof(T));
            }
        }

        private static object ResolveFormatter(Type type)
        {
            // SubscribableProperty<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableProperty<>))
            {
                var innerType = type.GetGenericArguments()[0];
                var formatterType = typeof(SubscribablePropertyFormatter<>).MakeGenericType(innerType);
                return Activator.CreateInstance(formatterType);
            }

            // SubscribableCollection<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableCollection<>))
            {
                var innerType = type.GetGenericArguments()[0];
                var formatterType = typeof(SubscribableCollectionFormatter<>).MakeGenericType(innerType);
                return Activator.CreateInstance(formatterType);
            }

            // SubscribableDictionary<TKey, TValue>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SubscribableDictionary<,>))
            {
                var genericArgs = type.GetGenericArguments();
                var formatterType = typeof(SubscribableDictionaryFormatter<,>).MakeGenericType(genericArgs);
                return Activator.CreateInstance(formatterType);
            }

            return null;
        }
    }

    /// <summary>
    /// 런타임 초기화 시 KDI MessagePack 리졸버를 자동 등록한다.
    /// 패키지 설치만으로 별도 코드 없이 즉시 사용 가능하다.
    /// </summary>
    public static class KDIMessagePackInitializer
    {
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // KDI 리졸버를 기본 리졸버 앞에 합성하여 전역 기본 옵션으로 설정
            var resolver = CompositeResolver.Create(
                KDIMessagePackResolver.Instance,
                StandardResolver.Instance
            );

            MessagePackSerializer.DefaultOptions =
                MessagePackSerializerOptions.Standard.WithResolver(resolver);

            Debug.Log("[KDI] MessagePack 어댑터가 자동 등록되었습니다.");
        }
    }
}
