using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// SubscribableProperty{T}용 MessagePack 포맷터.
    /// 내부 .Value (T) 만 직렬화/역직렬화한다.
    /// </summary>
    public sealed class SubscribablePropertyFormatter<T> : IMessagePackFormatter<SubscribableProperty<T>>
    {
        public void Serialize(ref MessagePackWriter writer, SubscribableProperty<T> value,
            MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            formatter.Serialize(ref writer, value.Value, options);
        }

        public SubscribableProperty<T> Deserialize(ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
                return null;

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            var innerValue = formatter.Deserialize(ref reader, options);
            return new SubscribableProperty<T>(innerValue);
        }
    }
}
