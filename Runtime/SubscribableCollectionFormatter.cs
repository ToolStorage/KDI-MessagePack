using System.Collections.Generic;
using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using UnityEngine.Scripting;

namespace Kylin.Serialization.MessagePack
{
    [Preserve]
    public sealed class SubscribableCollectionFormatter<T> : IMessagePackFormatter<SubscribableCollection<T>>
    {
        public static readonly SubscribableCollectionFormatter<T> Instance =
            new SubscribableCollectionFormatter<T>();

        public void Serialize(
            ref MessagePackWriter writer,
            SubscribableCollection<T> value,
            MessagePackSerializerOptions options)
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            var settings = KDIMessagePackReaderGuard.GetSettings(options);
            var formatter = options.Resolver.GetFormatterWithVerify<T>();
            var count = value.Count;
            KDIMessagePackReaderGuard.ValidateSerializeLength(
                count,
                settings.MaximumCollectionLength,
                "SubscribableCollection");
            writer.WriteArrayHeader(count);

            for (var i = 0; i < count; i++)
            {
                writer.CancellationToken.ThrowIfCancellationRequested();
                formatter.Serialize(ref writer, value[i], options);
            }
        }

        public SubscribableCollection<T> Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            reader.CancellationToken.ThrowIfCancellationRequested();
            if (reader.TryReadNil()) return null;

            options.Security.DepthStep(ref reader);
            try
            {
                var formatter = options.Resolver.GetFormatterWithVerify<T>();
                var count = reader.ReadArrayHeader();
                var settings = KDIMessagePackReaderGuard.GetSettings(options);
                KDIMessagePackReaderGuard.ValidateDeserializeLength(
                    ref reader,
                    count,
                    settings.MaximumCollectionLength,
                    1,
                    "SubscribableCollection");
                var items = new List<T>(
                    KDIMessagePackReaderGuard.GetInitialCapacity(count, settings));

                for (var i = 0; i < count; i++)
                {
                    reader.CancellationToken.ThrowIfCancellationRequested();
                    items.Add(formatter.Deserialize(ref reader, options));
                }

                // Bulk construction never emits notifications and does not participate in a
                // currently active Reaction in application code.
                return new SubscribableCollection<T>(items);
            }
            finally
            {
                reader.Depth--;
            }
        }
    }
}
