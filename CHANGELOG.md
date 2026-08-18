# Changelog

## [2.0.0] - 2026-08-18

### Changed
- Added additive `MessagePackSerializerOptions.WithKDI()` composition without default global mutation
- Added depth, cancellation, secure dictionary comparer, and silent bulk deserialization safeguards
- Added immutable `KDIMessagePackSettings` with conservative 65,536-entry quotas, remaining-byte lower-bound validation, and a 1,024-entry initial-capacity ceiling
- Added an explicit dictionary comparer policy tag; supported stable comparers round-trip for trusted data, arbitrary comparer objects fail before writing, and untrusted data never bypasses MessagePack's collision-resistant comparer
- Added explicit IL2CPP/AOT formatter registration and linker preservation
- Updated the package contract to `com.kylin.subscribable` 2.0.0
- Raised the supported MessagePack-CSharp baseline to security-supported 3.1.7+ compatible 3.x
- Added a publish gate that verifies the exact Subscribable dependency is already available
- Updated trusted publishing to Node 24 and one `publish.yml` identity for both push and manual dispatch.

### Breaking
- Installing the package no longer changes `MessagePackSerializer.DefaultOptions`; applications must own and pass `options.WithKDI()` (or explicitly opt into the legacy bridge)
- `SubscribableProperty<T>` now uses `[2, value]` for a non-null wrapper, preserving the distinction between wrapper `null` and `Value == null`; v1 scalar payloads require migration
- `SubscribableDictionary<TKey,TValue>` now uses `[2, comparerTag, map]`; v1 raw-map payloads require migration
- Collection/dictionary serialization now rejects values above the configured quota (65,536 by default)
- Kept the former initializer type as a non-mutating obsolete compatibility shim

## [1.0.0] - 2026-05-14

### Added
- SubscribableProperty<T> MessagePack formatter
- SubscribableCollection<T> MessagePack formatter
- SubscribableDictionary<TKey, TValue> MessagePack formatter
- KDIMessagePackResolver with auto-registration via RuntimeInitializeOnLoadMethod
- KDIMessagePackResolver.GetOptions() for manual configuration
