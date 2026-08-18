using System;
using MessagePack;

namespace Kylin.Serialization.MessagePack
{
    /// <summary>
    /// Resource limits applied by the KDI formatters. These limits are independent of
    /// MessagePackSecurity.MaximumObjectGraphDepth, which protects recursion depth rather
    /// than the width of a single collection.
    /// </summary>
    public sealed class KDIMessagePackSettings : IEquatable<KDIMessagePackSettings>
    {
        public const int DefaultMaximumCollectionLength = 65_536;
        public const int DefaultMaximumDictionaryLength = 65_536;
        public const int DefaultMaximumInitialCapacity = 1_024;

        public static readonly KDIMessagePackSettings Default =
            new KDIMessagePackSettings();

        public KDIMessagePackSettings(
            int maximumCollectionLength = DefaultMaximumCollectionLength,
            int maximumDictionaryLength = DefaultMaximumDictionaryLength,
            int maximumInitialCapacity = DefaultMaximumInitialCapacity)
        {
            if (maximumCollectionLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCollectionLength));
            }

            if (maximumDictionaryLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDictionaryLength));
            }

            if (maximumInitialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumInitialCapacity));
            }

            MaximumCollectionLength = maximumCollectionLength;
            MaximumDictionaryLength = maximumDictionaryLength;
            MaximumInitialCapacity = maximumInitialCapacity;
        }

        public int MaximumCollectionLength { get; }
        public int MaximumDictionaryLength { get; }
        public int MaximumInitialCapacity { get; }

        public bool Equals(KDIMessagePackSettings other)
        {
            return other != null &&
                   MaximumCollectionLength == other.MaximumCollectionLength &&
                   MaximumDictionaryLength == other.MaximumDictionaryLength &&
                   MaximumInitialCapacity == other.MaximumInitialCapacity;
        }

        public override bool Equals(object obj)
            => Equals(obj as KDIMessagePackSettings);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = MaximumCollectionLength;
                hash = (hash * 397) ^ MaximumDictionaryLength;
                hash = (hash * 397) ^ MaximumInitialCapacity;
                return hash;
            }
        }
    }

    internal interface IKDIMessagePackSettingsProvider
    {
        KDIMessagePackSettings Settings { get; }
    }

    internal static class KDIMessagePackReaderGuard
    {
        public static KDIMessagePackSettings GetSettings(
            MessagePackSerializerOptions options)
        {
            return (options.Resolver as IKDIMessagePackSettingsProvider)?.Settings ??
                   KDIMessagePackSettings.Default;
        }

        public static void ValidateSerializeLength(
            int count,
            int maximumLength,
            string containerName)
        {
            if (count > maximumLength)
            {
                throw new MessagePackSerializationException(
                    $"KDI {containerName} contains {count} entries, exceeding the configured " +
                    $"limit of {maximumLength}.");
            }
        }

        public static void ValidateDeserializeLength(
            ref MessagePackReader reader,
            int count,
            int maximumLength,
            int minimumBytesPerEntry,
            string containerName)
        {
            ValidateSerializeLength(count, maximumLength, containerName);

            // Every MessagePack value consumes at least one byte. This lower-bound check is
            // intentionally repeated here instead of relying on a particular MessagePack-CSharp
            // version's Read*Header implementation.
            var remainingBytes = reader.Sequence.Length - reader.Consumed;
            var minimumRequiredBytes = (long)count * minimumBytesPerEntry;
            if (minimumRequiredBytes > remainingBytes)
            {
                throw new MessagePackSerializationException(
                    $"KDI {containerName} header declares {count} entries, which require at " +
                    $"least {minimumRequiredBytes} bytes, but only {remainingBytes} bytes remain.");
            }
        }

        public static int GetInitialCapacity(
            int count,
            KDIMessagePackSettings settings)
            => Math.Min(count, settings.MaximumInitialCapacity);
    }
}
