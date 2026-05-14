using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// SubscribableCollection{T}용 MessagePack 포맷터.
    /// 내부 아이템 리스트를 배열로 직렬화/역직렬화한다.
    /// </summary>
    public sealed class SubscribableCollectionFormatter<T> : IMessagePackFormatter<SubscribableCollection<T>>
    {
        public void Serialize(ref MessagePackWriter writer, SubscribableCollection<T> value,
            MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            var count = value.Count;
            writer.WriteArrayHeader(count);

            for (int i = 0; i < count; i++)
            {
                formatter.Serialize(ref writer, value[i], options);
            }
        }

        public SubscribableCollection<T> Deserialize(ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
                return null;

            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            var count = reader.ReadArrayHeader();
            var collection = new SubscribableCollection<T>(count);

            for (int i = 0; i < count; i++)
            {
                collection.Add(formatter.Deserialize(ref reader, options));
            }

            return collection;
        }
    }
}
