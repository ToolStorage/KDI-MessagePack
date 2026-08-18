# KDI MessagePack Adapter

KDI의 `SubscribableProperty`, `SubscribableCollection`, `SubscribableDictionary` 타입을 [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)로 바이너리 직렬화할 수 있게 하는 어댑터 패키지.

```
com.kylin.di.messagepack | Unity 6000.0+ | MIT License
```

Version compatibility: KDI MessagePack Adapter 2.0.0 requires `com.kylin.subscribable` 2.0.0. It can be used with KDI 2.0.0 or directly with Subscribable 2.0.0.

---

## 설치

### 1. Scoped Registry 추가

`Packages/manifest.json`에 다음을 추가:

```json
{
  "scopedRegistries": [
    {
      "name": "Kylin",
      "url": "https://registry.npmjs.org",
      "scopes": ["com.kylin"]
    }
  ],
  "dependencies": {
    "com.kylin.di.messagepack": "2.0.0"
  }
}
```

### 2. 전제 조건

프로젝트에 MessagePack-CSharp가 설치되어 있어야 합니다. (NuGetForUnity 또는 직접 DLL 참조)

---

## 사용법

### 옵션 구성 (기본)

애플리케이션이 소유한 기존 옵션에 `WithKDI()`를 적용합니다. 기존 resolver, security, compression 등 다른 설정은 그대로 유지되며 패키지는 전역 `MessagePackSerializer.DefaultOptions`를 변경하지 않습니다.

```csharp
var options = MessagePackSerializerOptions.Standard.WithKDI();
var health = new SubscribableProperty<int>(100);
var bytes = MessagePackSerializer.Serialize(health, options);
var restored = MessagePackSerializer.Deserialize<SubscribableProperty<int>>(bytes, options);
// restored.Value == 100
```

기본적으로 `SubscribableCollection`과 `SubscribableDictionary`는 각각 최대 65,536개 항목만 허용하고, wire header를 초기 capacity로 그대로 사용하지 않습니다. 애플리케이션 데이터 계약에 맞춰 더 낮거나 높은 한도가 필요하면 명시적으로 설정할 수 있습니다.

```csharp
var kdiSettings = new KDIMessagePackSettings(
    maximumCollectionLength: 10_000,
    maximumDictionaryLength: 5_000,
    maximumInitialCapacity: 512);
var options = MessagePackSerializerOptions.Standard.WithKDI(kdiSettings);
```

`maximumInitialCapacity`는 header를 읽은 직후의 선할당만 제한합니다. 실제 항목은 quota와 남은 payload의 최소 byte 수를 먼저 검증한 뒤 하나씩 읽으므로, 작은 악성 payload가 큰 header 하나로 대규모 할당을 유발하지 않습니다.

### 데이터 클래스 예시

```csharp
[MessagePackObject]
public class PlayerSaveData
{
    [Key(0)] public SubscribableProperty<int> Health { get; set; } = new(100);
    [Key(1)] public SubscribableProperty<string> Name { get; set; } = new("Player");
    [Key(2)] public SubscribableCollection<string> Inventory { get; set; } = new();
    [Key(3)] public SubscribableDictionary<string, int> Stats { get; set; } = new();
}

// 직렬화
var data = new PlayerSaveData();
data.Health.Value = 80;
data.Inventory.Add("Sword");
data.Stats["ATK"] = 50;

var options = MessagePackSerializerOptions.Standard.WithKDI();
var bytes = MessagePackSerializer.Serialize(data, options);

// 역직렬화 - 구독 가능한 프로퍼티로 복원됨
var loaded = MessagePackSerializer.Deserialize<PlayerSaveData>(bytes, options);
// loaded.Health.Value == 80
// loaded.Inventory[0] == "Sword"
// loaded.Stats["ATK"] == 50

// 복원 후에도 구독이 정상 동작
loaded.Health.Subscribe(hp => Debug.Log($"HP: {hp}"));
loaded.Health.Value = 60; // "HP: 60" 출력
```

### 1.x 마이그레이션 옵션 헬퍼와 wire 변경

기존 `GetOptions()`도 유지되지만 새 코드에서는 `WithKDI()`가 기존 설정을 보존하므로 권장됩니다.

```csharp
var options = KDIMessagePackResolver.GetOptions();
var bytes = MessagePackSerializer.Serialize(data, options);
var loaded = MessagePackSerializer.Deserialize<PlayerSaveData>(bytes, options);
```

이전 자동 initializer 타입은 소스 호환용으로만 남아 있으며 런타임 초기화 hook이나 전역 변경을 수행하지 않습니다. `KDIMessagePackInitializer.GetOptions()`는 현재 전역 옵션을 변경하지 않고 KDI가 합성된 새 옵션을 반환합니다.

2.0.0의 property/dictionary wire 형식은 1.x와 호환되지 않습니다. 1.x payload는 1.x formatter로 먼저 읽은 뒤 2.0.0 options로 다시 직렬화해야 합니다. 특히 1.x의 `SubscribableProperty<T>`는 wrapper `null`과 non-null wrapper의 `Value == null`을 모두 `nil`로 기록했으므로 사후에 두 상태를 복원할 방법이 없습니다. 원본 상태를 알고 있는 마이그레이션 단계에서 이를 결정해야 합니다.

### IL2CPP/AOT 등록

링커 보존 설정이 포함되어 있습니다. 닫힌 제네릭 타입을 정적으로 확정해야 하는 빌드에서는 시작 시점에 다음처럼 명시적으로 등록할 수 있습니다.

```csharp
KDIMessagePackAot.RegisterProperty<int>();
KDIMessagePackAot.RegisterCollection<ItemData>();
KDIMessagePackAot.RegisterDictionary<string, int>();
```

---

## 동작 원리

| KDI 타입 | 직렬화 형태 | 설명 |
|----------|------------|------|
| `SubscribableProperty<T>` | `[2, value]` 또는 wrapper `nil` | v2 tag envelope로 wrapper `null`과 `Value == null`을 구분. 구독 상태는 직렬화하지 않음 |
| `SubscribableCollection<T>` | MessagePack Array | 내부 아이템 리스트를 배열로 직렬화 |
| `SubscribableDictionary<K,V>` | `[2, comparerTag, map]` 또는 wrapper `nil` | comparer 정책과 Map을 함께 기록. comparer 객체 자체는 직렬화하지 않음 |

역직렬화 시 새 인스턴스가 생성되며, 구독은 비어 있는 상태로 시작합니다. 이는 의도된 동작으로, 구독은 런타임에 코드로 설정하는 것이 올바른 패턴입니다.

Dictionary comparer는 `EqualityComparer<TKey>.Default`와 안정적인 `StringComparer.Ordinal`, `OrdinalIgnoreCase`, `InvariantCulture`, `InvariantCultureIgnoreCase`만 지원합니다. 그 밖의 임의 comparer는 의미를 조용히 바꾸지 않고 직렬화 전에 실패합니다. `MessagePackSecurity.UntrustedData`에서는 hash-collision-resistant comparer가 의미를 보존할 수 있는 Default/Ordinal 정책만 허용합니다. case-insensitive/culture 정책은 secure comparer를 우회하거나 다른 의미로 바꾸지 않고 명확히 실패하므로, 신뢰된 저장 데이터에만 `TrustedData`와 함께 사용해야 합니다.

---

## 의존성

- `com.kylin.subscribable` 2.0.0
- MessagePack-CSharp 3.1.7 이상(호환되는 3.x)
