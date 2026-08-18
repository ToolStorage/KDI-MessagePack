using System;
using System.Collections.Generic;
using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using UnityEngine.Scripting;

namespace Kylin.Serialization.MessagePack
{
    [Preserve]
    public sealed class SubscribableDictionaryFormatter<TKey, TValue>
        : IMessagePackFormatter<SubscribableDictionary<TKey, TValue>>
    {
        private const int WireFormatVersion = 2;
        private const int EnvelopeLength = 3;

        public static readonly SubscribableDictionaryFormatter<TKey, TValue> Instance =
            new SubscribableDictionaryFormatter<TKey, TValue>();

        public void Serialize(
            ref MessagePackWriter writer,
            SubscribableDictionary<TKey, TValue> value,
            MessagePackSerializerOptions options)
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            var comparerKind = ClassifyComparer(value.Comparer, options.Security);
            ValidateComparerPolicy(comparerKind, options.Security);
            var settings = KDIMessagePackReaderGuard.GetSettings(options);
            KDIMessagePackReaderGuard.ValidateSerializeLength(
                value.Count,
                settings.MaximumDictionaryLength,
                "SubscribableDictionary");

            var keyFormatter = options.Resolver.GetFormatterWithVerify<TKey>();
            var valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();
            writer.WriteArrayHeader(EnvelopeLength);
            writer.Write(WireFormatVersion);
            writer.Write((byte)comparerKind);
            writer.WriteMapHeader(value.Count);

            foreach (var pair in value)
            {
                writer.CancellationToken.ThrowIfCancellationRequested();
                keyFormatter.Serialize(ref writer, pair.Key, options);
                valueFormatter.Serialize(ref writer, pair.Value, options);
            }
        }

        public SubscribableDictionary<TKey, TValue> Deserialize(
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
                        $"Invalid SubscribableDictionary envelope length {envelopeLength}; " +
                        $"expected {EnvelopeLength} for KDI wire format v{WireFormatVersion}.");
                }

                var wireFormatVersion = reader.ReadInt32();
                if (wireFormatVersion != WireFormatVersion)
                {
                    throw new MessagePackSerializationException(
                        $"Unsupported SubscribableDictionary wire format version " +
                        $"{wireFormatVersion}; expected {WireFormatVersion}.");
                }

                var comparerKind = (DictionaryComparerKind)reader.ReadByte();
                var comparer = ResolveComparer(comparerKind, options.Security);
                var keyFormatter = options.Resolver.GetFormatterWithVerify<TKey>();
                var valueFormatter = options.Resolver.GetFormatterWithVerify<TValue>();
                var count = reader.ReadMapHeader();
                var settings = KDIMessagePackReaderGuard.GetSettings(options);
                KDIMessagePackReaderGuard.ValidateDeserializeLength(
                    ref reader,
                    count,
                    settings.MaximumDictionaryLength,
                    2,
                    "SubscribableDictionary");
                var values = new Dictionary<TKey, TValue>(
                    KDIMessagePackReaderGuard.GetInitialCapacity(count, settings),
                    comparer);

                for (var i = 0; i < count; i++)
                {
                    reader.CancellationToken.ThrowIfCancellationRequested();
                    var key = keyFormatter.Deserialize(ref reader, options);
                    var value = valueFormatter.Deserialize(ref reader, options);
                    values.Add(key, value);
                }

                if (options.Security.HashCollisionResistant)
                {
                    // The adapter-only bridge preserves MessagePack's collision-resistant
                    // comparer without reopening SubscribableDictionary's public arbitrary-
                    // comparer API. This instance remains runtime-only for Unity serialization,
                    // whose OnBeforeSerialize contract deliberately rejects that comparer.
                    return SubscribableDictionary<TKey, TValue>.CreateRuntimeOnly(
                        values,
                        comparer);
                }

                return new SubscribableDictionary<TKey, TValue>(values, comparer);
            }
            finally
            {
                reader.Depth--;
            }
        }

        private static DictionaryComparerKind ClassifyComparer(
            IEqualityComparer<TKey> comparer,
            MessagePackSecurity security)
        {
            if (ReferenceEquals(comparer, EqualityComparer<TKey>.Default))
            {
                return DictionaryComparerKind.Default;
            }

            if (typeof(TKey) == typeof(string))
            {
                if (ReferenceEquals(comparer, StringComparer.Ordinal))
                {
                    return DictionaryComparerKind.StringOrdinal;
                }

                if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
                {
                    return DictionaryComparerKind.StringOrdinalIgnoreCase;
                }

                if (ReferenceEquals(comparer, StringComparer.InvariantCulture))
                {
                    return DictionaryComparerKind.StringInvariantCulture;
                }

                if (ReferenceEquals(comparer, StringComparer.InvariantCultureIgnoreCase))
                {
                    return DictionaryComparerKind.StringInvariantCultureIgnoreCase;
                }
            }

            // A dictionary previously deserialized with UntrustedData owns MessagePack's
            // collision-resistant comparer. It is safe to serialize it as the default semantic
            // policy; object identity or implementation details never enter the wire format.
            if (security.HashCollisionResistant &&
                ReferenceEquals(comparer, security.GetEqualityComparer<TKey>()))
            {
                return DictionaryComparerKind.Default;
            }

            throw new MessagePackSerializationException(
                $"Comparer '{comparer?.GetType().FullName ?? "null"}' cannot be represented " +
                $"safely for SubscribableDictionary<{typeof(TKey).FullName}, " +
                $"{typeof(TValue).FullName}>. Use EqualityComparer<TKey>.Default or one of " +
                "StringComparer.Ordinal, OrdinalIgnoreCase, InvariantCulture, or " +
                "InvariantCultureIgnoreCase. Arbitrary comparer objects are never serialized.");
        }

        private static IEqualityComparer<TKey> ResolveComparer(
            DictionaryComparerKind comparerKind,
            MessagePackSecurity security)
        {
            ValidateComparerPolicy(comparerKind, security);

            if (security.HashCollisionResistant)
            {
                return security.GetEqualityComparer<TKey>();
            }

            switch (comparerKind)
            {
                case DictionaryComparerKind.Default:
                    return security.GetEqualityComparer<TKey>();
                case DictionaryComparerKind.StringOrdinal:
                    return CastStringComparer(StringComparer.Ordinal, comparerKind);
                case DictionaryComparerKind.StringOrdinalIgnoreCase:
                    return CastStringComparer(StringComparer.OrdinalIgnoreCase, comparerKind);
                case DictionaryComparerKind.StringInvariantCulture:
                    return CastStringComparer(StringComparer.InvariantCulture, comparerKind);
                case DictionaryComparerKind.StringInvariantCultureIgnoreCase:
                    return CastStringComparer(
                        StringComparer.InvariantCultureIgnoreCase,
                        comparerKind);
                default:
                    throw new MessagePackSerializationException(
                        $"Unknown SubscribableDictionary comparer policy tag " +
                        $"{(byte)comparerKind}.");
            }
        }

        private static void ValidateComparerPolicy(
            DictionaryComparerKind comparerKind,
            MessagePackSecurity security)
        {
            if (!security.HashCollisionResistant) return;

            if (comparerKind == DictionaryComparerKind.Default ||
                (typeof(TKey) == typeof(string) &&
                 comparerKind == DictionaryComparerKind.StringOrdinal))
            {
                return;
            }

            throw new MessagePackSerializationException(
                $"Comparer policy '{comparerKind}' cannot be used while " +
                "MessagePackSecurity.HashCollisionResistant is enabled. KDI will not silently " +
                "replace its equality semantics or bypass MessagePack's secure comparer. Use " +
                "TrustedData only for trusted input, or use the Default/Ordinal policy for " +
                "untrusted data.");
        }

        private static IEqualityComparer<TKey> CastStringComparer(
            StringComparer comparer,
            DictionaryComparerKind comparerKind)
        {
            if (typeof(TKey) != typeof(string))
            {
                throw new MessagePackSerializationException(
                    $"String comparer policy '{comparerKind}' is invalid for dictionary key " +
                    $"type '{typeof(TKey).FullName}'.");
            }

            return (IEqualityComparer<TKey>)(object)comparer;
        }

        private enum DictionaryComparerKind : byte
        {
            Default = 0,
            StringOrdinal = 1,
            StringOrdinalIgnoreCase = 2,
            StringInvariantCulture = 3,
            StringInvariantCultureIgnoreCase = 4,
        }
    }
}
