# KDI MessagePack Adapter

KDI의 `SubscribableProperty`, `SubscribableCollection`, `SubscribableDictionary` 타입을 [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)로 바이너리 직렬화할 수 있게 하는 어댑터 패키지.

```
com.kylin.di.messagepack | Unity 6000.0+ | MIT License
```

---

## 설치

### 1. Scoped Registry 추가

`Packages/manifest.json`에 다음을 추가:

```json
{
  "scopedRegistries": [
    {
      "name": "Kylin",
      "url": "https://www.npmjs.com/~kylin",
      "scopes": ["com.kylin"]
    }
  ],
  "dependencies": {
    "com.kylin.di": "1.0.0",
    "com.kylin.di.messagepack": "1.0.0"
  }
}
```

### 2. 전제 조건

프로젝트에 MessagePack-CSharp가 설치되어 있어야 합니다. (NuGetForUnity 또는 직접 DLL 참조)

---

## 사용법

### 자동 등록 (기본)

**별도 설정이 필요 없습니다.** 패키지 설치만으로 `RuntimeInitializeOnLoadMethod`를 통해 자동으로 MessagePack 기본 옵션에 KDI 리졸버가 등록됩니다.

```csharp
// 그냥 평소처럼 직렬화하면 됩니다
var health = new SubscribableProperty<int>(100);
var bytes = MessagePackSerializer.Serialize(health);
var restored = MessagePackSerializer.Deserialize<SubscribableProperty<int>>(bytes);
// restored.Value == 100
```

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

var bytes = MessagePackSerializer.Serialize(data);

// 역직렬화 - 구독 가능한 프로퍼티로 복원됨
var loaded = MessagePackSerializer.Deserialize<PlayerSaveData>(bytes);
// loaded.Health.Value == 80
// loaded.Inventory[0] == "Sword"
// loaded.Stats["ATK"] == 50

// 복원 후에도 구독이 정상 동작
loaded.Health.Subscribe(hp => Debug.Log($"HP: {hp}"));
loaded.Health.Value = 60; // "HP: 60" 출력
```

### 수동 옵션 구성 (선택)

자동 등록 대신 수동으로 옵션을 구성하려면:

```csharp
var options = KDIMessagePackResolver.GetOptions();
var bytes = MessagePackSerializer.Serialize(data, options);
var loaded = MessagePackSerializer.Deserialize<PlayerSaveData>(bytes, options);
```

---

## 동작 원리

| KDI 타입 | 직렬화 형태 | 설명 |
|----------|------------|------|
| `SubscribableProperty<T>` | `T` 값 그대로 | 내부 `.Value`만 직렬화. 구독 상태는 직렬화하지 않음 |
| `SubscribableCollection<T>` | MessagePack Array | 내부 아이템 리스트를 배열로 직렬화 |
| `SubscribableDictionary<K,V>` | MessagePack Map | 내부 딕셔너리를 Map으로 직렬화 |

역직렬화 시 새 인스턴스가 생성되며, 구독은 비어 있는 상태로 시작합니다. 이는 의도된 동작으로, 구독은 런타임에 코드로 설정하는 것이 올바른 패턴입니다.

---

## 의존성

- `com.kylin.di` >= 1.0.0
- MessagePack-CSharp >= 2.x
