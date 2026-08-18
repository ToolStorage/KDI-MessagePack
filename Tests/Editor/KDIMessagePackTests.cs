using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Kylin.SubscribableProperty;
using MessagePack;
using NUnit.Framework;

namespace Kylin.Serialization.MessagePack.Tests
{
    public class KDIMessagePackTests
    {
        [Test]
        public void WithKDI_PreservesBaseOptionsAndDoesNotMutateGlobalDefaults()
        {
            var global = MessagePackSerializer.DefaultOptions;
            var source = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData)
                .WithCompression(MessagePackCompression.Lz4BlockArray);

            var composed = source.WithKDI();

            Assert.That(MessagePackSerializer.DefaultOptions, Is.SameAs(global));
            Assert.That(composed.Security, Is.SameAs(source.Security));
            Assert.That(composed.Compression, Is.EqualTo(source.Compression));
            Assert.That(composed.WithKDI(), Is.SameAs(composed));
        }

        [Test]
        public void Formatters_RoundTripV2WireShape()
        {
            var options = MessagePackSerializerOptions.Standard.WithKDI();

            var propertyBytes = MessagePackSerializer.Serialize(
                new SubscribableProperty<int>(42),
                options);
            var propertyReader = new MessagePackReader(
                new ReadOnlySequence<byte>(propertyBytes));
            Assert.That(propertyReader.ReadArrayHeader(), Is.EqualTo(2));
            Assert.That(propertyReader.ReadInt32(), Is.EqualTo(2));
            Assert.That(propertyReader.ReadInt32(), Is.EqualTo(42));
            Assert.That(propertyReader.End, Is.True);
            Assert.That(
                MessagePackSerializer.Deserialize<SubscribableProperty<int>>(propertyBytes, options).Value,
                Is.EqualTo(42));

            var collection = new SubscribableCollection<string>(new[] { "a", "b" });
            var collectionCopy = MessagePackSerializer.Deserialize<SubscribableCollection<string>>(
                MessagePackSerializer.Serialize(collection, options),
                options);
            CollectionAssert.AreEqual(new[] { "a", "b" }, collectionCopy.ToArray());

            var dictionary = new SubscribableDictionary<string, int>();
            dictionary.Add("a", 1);
            var dictionaryCopy = MessagePackSerializer.Deserialize<SubscribableDictionary<string, int>>(
                MessagePackSerializer.Serialize(dictionary, options),
                options);
            Assert.That(dictionaryCopy["a"], Is.EqualTo(1));
        }

        [Test]
        public void PropertyWire_DistinguishesNullWrapperFromNullValue()
        {
            var options = MessagePackSerializerOptions.Standard.WithKDI();
            SubscribableProperty<string> nullWrapper = null;
            var wrapperBytes = MessagePackSerializer.Serialize(nullWrapper, options);
            var nullValueBytes = MessagePackSerializer.Serialize(
                new SubscribableProperty<string>(null),
                options);

            var wrapperReader = new MessagePackReader(
                new ReadOnlySequence<byte>(wrapperBytes));
            Assert.That(wrapperReader.TryReadNil(), Is.True);

            var reader = new MessagePackReader(
                new ReadOnlySequence<byte>(nullValueBytes));
            Assert.That(reader.ReadArrayHeader(), Is.EqualTo(2));
            Assert.That(reader.ReadInt32(), Is.EqualTo(2));
            Assert.That(reader.TryReadNil(), Is.True);
            Assert.That(reader.End, Is.True);

            Assert.That(
                MessagePackSerializer.Deserialize<SubscribableProperty<string>>(
                    wrapperBytes,
                    options),
                Is.Null);
            var restored = MessagePackSerializer.Deserialize<SubscribableProperty<string>>(
                nullValueBytes,
                options);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Value, Is.Null);
        }

        [Test]
        public void PropertyDeserialize_RejectsLegacyScalarWireShape()
        {
            var legacyBytes = MessagePackSerializer.Serialize(42);
            Exception failure = null;

            try
            {
                MessagePackSerializer.Deserialize<SubscribableProperty<int>>(
                    legacyBytes,
                    MessagePackSerializerOptions.Standard.WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
        }

        [Test]
        public void DictionaryDeserialize_UsesSecurityComparer()
        {
            var options = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData)
                .WithKDI();
            var source = new SubscribableDictionary<string, int>();
            source.Add("key", 1);
            var bytes = MessagePackSerializer.Serialize(source, options);
            var restored = MessagePackSerializer.Deserialize<SubscribableDictionary<string, int>>(
                bytes,
                options);

            Assert.That(
                restored.Comparer.GetType(),
                Is.EqualTo(options.Security.GetEqualityComparer<string>().GetType()));

            // The runtime-only secure comparer remains safe for subsequent MessagePack saves.
            var restoredAgain = MessagePackSerializer.Deserialize<
                SubscribableDictionary<string, int>>(
                MessagePackSerializer.Serialize(restored, options),
                options);
            Assert.That(restoredAgain["key"], Is.EqualTo(1));
            Assert.That(
                restoredAgain.Comparer.GetType(),
                Is.EqualTo(options.Security.GetEqualityComparer<string>().GetType()));
        }

        [Test]
        public void DictionaryWire_PreservesSupportedComparerForTrustedData()
        {
            var options = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.TrustedData)
                .WithKDI();
            var source = new SubscribableDictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            source.Add("key", 1);

            var bytes = MessagePackSerializer.Serialize(source, options);
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
            Assert.That(reader.ReadArrayHeader(), Is.EqualTo(3));
            Assert.That(reader.ReadInt32(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo((byte)2));
            Assert.That(reader.ReadMapHeader(), Is.EqualTo(1));

            var restored = MessagePackSerializer.Deserialize<
                SubscribableDictionary<string, int>>(bytes, options);
            Assert.That(restored.Comparer, Is.SameAs(StringComparer.OrdinalIgnoreCase));
            Assert.That(restored.ContainsKey("KEY"), Is.True);
        }

        [Test]
        public void DictionarySerialize_RejectsArbitraryComparerBeforeWriting()
        {
            var comparer = new FirstCharacterComparer();
            var raw = new Dictionary<string, int>(comparer) { ["key"] = 1 };
            var runtimeFactory = typeof(SubscribableDictionary<string, int>).GetMethod(
                "CreateRuntimeOnly",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(runtimeFactory, Is.Not.Null);
            var source = (SubscribableDictionary<string, int>)runtimeFactory.Invoke(
                null,
                new object[] { raw, comparer });
            Exception failure = null;

            try
            {
                MessagePackSerializer.Serialize(
                    source,
                    MessagePackSerializerOptions.Standard.WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("Arbitrary comparer objects are never serialized", failure.ToString());
        }

        [Test]
        public void DictionaryDeserialize_UntrustedDataRejectsComparerItCannotPreserveSecurely()
        {
            var trustedOptions = MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.TrustedData)
                .WithKDI();
            var source = new SubscribableDictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            source.Add("key", 1);
            var bytes = MessagePackSerializer.Serialize(source, trustedOptions);
            Exception failure = null;

            try
            {
                MessagePackSerializer.Deserialize<SubscribableDictionary<string, int>>(
                    bytes,
                    MessagePackSerializerOptions.Standard
                        .WithSecurity(MessagePackSecurity.UntrustedData)
                        .WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("HashCollisionResistant", failure.ToString());
        }

        [Test]
        public void DictionarySerialize_UntrustedDataRejectsComparerItCannotRoundTripSecurely()
        {
            var source = new SubscribableDictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            source.Add("key", 1);
            Exception failure = null;

            try
            {
                MessagePackSerializer.Serialize(
                    source,
                    MessagePackSerializerOptions.Standard
                        .WithSecurity(MessagePackSecurity.UntrustedData)
                        .WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("HashCollisionResistant", failure.ToString());
        }

        [Test]
        public void CollectionDeserialize_RejectsConfiguredQuotaBeforeConstruction()
        {
            var payload = MessagePackSerializer.Serialize(new[] { 1, 2, 3 });
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
            var options = MessagePackSerializerOptions.Standard.WithKDI(
                new KDIMessagePackSettings(
                    maximumCollectionLength: 2,
                    maximumDictionaryLength: 2,
                    maximumInitialCapacity: 1));
            var initialDepth = reader.Depth;
            Exception failure = null;

            try
            {
                SubscribableCollectionFormatter<int>.Instance.Deserialize(
                    ref reader,
                    options);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("configured limit of 2", failure.ToString());
            Assert.That(reader.Depth, Is.EqualTo(initialDepth));
        }

        [Test]
        public void DictionaryDeserialize_RejectsImpossibleRemainingByteCount()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteArrayHeader(3);
            writer.Write(2);
            writer.Write((byte)0);
            writer.WriteMapHeader(2);
            writer.WriteNil();
            writer.Flush();
            var reader = new MessagePackReader(
                new ReadOnlySequence<byte>(buffer.WrittenMemory));
            var initialDepth = reader.Depth;
            Exception failure = null;

            try
            {
                SubscribableDictionaryFormatter<string, int>.Instance.Deserialize(
                    ref reader,
                    MessagePackSerializerOptions.Standard.WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(reader.Depth, Is.EqualTo(initialDepth));
        }

        [Test]
        public void DictionaryDeserialize_RejectsConfiguredQuotaBeforeConstruction()
        {
            var source = new SubscribableDictionary<string, int>();
            source.Add("a", 1);
            source.Add("b", 2);
            source.Add("c", 3);
            var payload = MessagePackSerializer.Serialize(
                source,
                MessagePackSerializerOptions.Standard.WithKDI());
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
            var options = MessagePackSerializerOptions.Standard.WithKDI(
                new KDIMessagePackSettings(
                    maximumCollectionLength: 2,
                    maximumDictionaryLength: 2,
                    maximumInitialCapacity: 1));
            var initialDepth = reader.Depth;
            Exception failure = null;

            try
            {
                SubscribableDictionaryFormatter<string, int>.Instance.Deserialize(
                    ref reader,
                    options);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            StringAssert.Contains("configured limit of 2", failure.ToString());
            Assert.That(reader.Depth, Is.EqualTo(initialDepth));
        }

        [Test]
        public void Deserialize_RestoresDepthWhenInnerFormatterFails()
        {
            var payload = MessagePackSerializer.Serialize(new[] { "not-an-int" });
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
            var initialDepth = reader.Depth;
            Exception failure = null;

            try
            {
                SubscribableCollectionFormatter<int>.Instance.Deserialize(
                    ref reader,
                    MessagePackSerializerOptions.Standard.WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(reader.Depth, Is.EqualTo(initialDepth));
        }

        [Test]
        public void Deserialize_ObservesReaderCancellationAndRestoresDepth()
        {
            var payload = MessagePackSerializer.Serialize(new[] { 1 });
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload))
            {
                CancellationToken = cancellation.Token,
            };
            var initialDepth = reader.Depth;
            Exception failure = null;

            try
            {
                SubscribableCollectionFormatter<int>.Instance.Deserialize(
                    ref reader,
                    MessagePackSerializerOptions.Standard.WithKDI());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.TypeOf<OperationCanceledException>());
            Assert.That(reader.Depth, Is.EqualTo(initialDepth));
        }

        [Test]
        public void AotRegistration_ReturnsClosedGenericSingleton()
        {
            KDIMessagePackAot.RegisterProperty<int>();
            KDIMessagePackAot.RegisterCollection<string>();
            KDIMessagePackAot.RegisterDictionary<string, int>();

            Assert.That(
                KDIMessagePackResolver.Instance.GetFormatter<SubscribableProperty<int>>(),
                Is.SameAs(SubscribablePropertyFormatter<int>.Instance));
            Assert.That(
                KDIMessagePackResolver.Instance.GetFormatter<SubscribableCollection<string>>(),
                Is.SameAs(SubscribableCollectionFormatter<string>.Instance));
            Assert.That(
                KDIMessagePackResolver.Instance.GetFormatter<SubscribableDictionary<string, int>>(),
                Is.SameAs(SubscribableDictionaryFormatter<string, int>.Instance));
        }

        private sealed class FirstCharacterComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null || x.Length == 0 || y.Length == 0) return false;
                return x[0] == y[0];
            }

            public int GetHashCode(string obj)
                => string.IsNullOrEmpty(obj) ? 0 : obj[0];
        }
    }
}
