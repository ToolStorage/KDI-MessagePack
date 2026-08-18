using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using UnityEngine.Scripting;

namespace Kylin.Serialization.MessagePack
{
    [Preserve]
    public sealed class SubscribablePropertyFormatter<T> : IMessagePackFormatter<SubscribableProperty<T>>
    {
        private const int WireFormatVersion = 2;
        private const int EnvelopeLength = 2;

        public static readonly SubscribablePropertyFormatter<T> Instance =
            new SubscribablePropertyFormatter<T>();

        public void Serialize(
            ref MessagePackWriter writer,
            SubscribableProperty<T> value,
            MessagePackSerializerOptions options)
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            // A tagged envelope distinguishes a null wrapper (nil) from a non-null wrapper
            // whose Value is null ([2, nil]). The former v1 scalar shape could not do so.
            writer.WriteArrayHeader(EnvelopeLength);
            writer.Write(WireFormatVersion);
            options.Resolver.GetFormatterWithVerify<T>()
                .Serialize(ref writer, value.Value, options);
        }

        public SubscribableProperty<T> Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            reader.CancellationToken.ThrowIfCancellationRequested();
            if (reader.TryReadNil()) return null;

            options.Security.DepthStep(ref reader);
            try
            {
                var envelopeLength = reader.ReadArrayHeader();
                if (envelopeLength != EnvelopeLength)
                {
                    throw new MessagePackSerializationException(
                        $"Invalid SubscribableProperty envelope length {envelopeLength}; " +
                        $"expected {EnvelopeLength} for KDI wire format v{WireFormatVersion}.");
                }

                var wireFormatVersion = reader.ReadInt32();
                if (wireFormatVersion != WireFormatVersion)
                {
                    throw new MessagePackSerializationException(
                        $"Unsupported SubscribableProperty wire format version " +
                        $"{wireFormatVersion}; expected {WireFormatVersion}.");
                }

                var innerValue = options.Resolver.GetFormatterWithVerify<T>()
                    .Deserialize(ref reader, options);
                return new SubscribableProperty<T>(innerValue);
            }
            finally
            {
                reader.Depth--;
            }
        }
    }
}
