using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// SubscribableDictionary{TKey, TValue}용 MessagePack 포맷터.
    /// 내부 딕셔너리를 Map으로 직렬화/역직렬화한다.
    /// </summary>
    public sealed class SubscribableDictionaryFormatter<TKey, TValue>
        : IMessagePackFormatter<SubscribableDictionary<TKey, TValue>>
    {
        public void Serialize(ref MessagePackWriter writer, SubscribableDictionary<TKey, TValue> value,
            MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            var keyFormatter = options.Resolver.GetFormatterWithVerify<TKey>();
            var valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();

            writer.WriteMapHeader(value.Count);

            foreach (var kvp in value)
            {
                keyFormatter.Serialize(ref writer, kvp.Key, options);
                valueFormatter.Serialize(ref writer, kvp.Value, options);
            }
        }

        public SubscribableDictionary<TKey, TValue> Deserialize(ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
                return null;

            var keyFormatter = options.Resolver.GetFormatterWithVerify<TKey>();
            var valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();

            var count = reader.ReadMapHeader();
            var dict = new SubscribableDictionary<TKey, TValue>(count);

            for (int i = 0; i < count; i++)
            {
                var key = keyFormatter.Deserialize(ref reader, options);
                var val = valueFormatter.Deserialize(ref reader, options);
                dict.Add(key, val);
            }

            return dict;
        }
    }
}
