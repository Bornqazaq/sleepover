# Movement Synchronization (IGR-51) — Как это работает

## Architecture: Server-Authority с ClientNetworkTransform

```
┌──────────────────────────────────────────────────────────────────┐
│                         HOST (Server)                             │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Player 1 (Owner: HOST)                                      │ │
│  │  ├─ PlayerController (ввод: WASD, ввод обрабатывается)     │ │
│  │  ├─ Rigidbody (физика работает)                             │ │
│  │  └─ NetworkTransform (реплицирует позицию → всем)           │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Player 2 (Owner: CLIENT, но спавнена на HOST)               │ │
│  │  ├─ PlayerController (ОТКЛЮЧЕНА — управление с CLIENT)      │ │
│  │  ├─ Rigidbody (физика на сервере)                           │ │
│  │  └─ NetworkTransform (получает позицию от CLIENT)           │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
                            ↕️ Сеть
┌──────────────────────────────────────────────────────────────────┐
│                      CLIENT (не-сервер)                          │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Player 1 (Owner: HOST, удалённый)                           │ │
│  │  ├─ PlayerController (ОТКЛЮЧЕНА — управление на HOST)       │ │
│  │  ├─ Rigidbody (локальная физика для показа)                │ │
│  │  └─ NetworkTransform (получает позицию от HOST)             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                    │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Player 2 (Owner: CLIENT)                                    │ │
│  │  ├─ PlayerController (ввод: WASD, ввод обрабатывается)     │ │
│  │  ├─ Rigidbody (предсказание движения клиента)              │ │
│  │  └─ NetworkTransform (отправляет позицию на HOST)           │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

## Flow: Как синхронизируется один шаг движения

### 1️⃣ Владелец персонажа (только на его machine)
```
HOST:
  Input (WASD) → PlayerController.Update()
    ↓
  ApplyLocomotion(moveInput)
    ↓
  Rigidbody.linearVelocity изменяется
  Rigidbody.rotation изменяется
    ↓
  NetworkTransform.OnUpdate() (каждый кадр)
    ↓
  Отправить позицию/ротацию на всех остальных
```

### 2️⃣ Удалённые персонажи (только отображение)
```
CLIENT (получение):
  NetworkTransform.OnNetworkSpawn()
    ↓
  Слушает изменения позиции от сервера
    ↓
  Update(): интерполирует между старой и новой позицией
    ↓
  Transform.position обновляется плавно
```

## Что уже настроено ✅

- ✅ `NetworkTransform` компонент добавлен на Player.prefab (IGR-50)
- ✅ `NetworkObject` компонент добавлен (IGR-50)
- ✅ `IsOwner` проверка в NetworkPlayerController отключает ввод для удалённых (IGR-51)
- ✅ PlayerController кэширует Rigidbody в Awake (CLAUDE.md правило)

## Что проверить

### На Player.prefab:
```
✅ NetworkObject (required for network sync)
✅ NetworkTransform с настройками:
   - Serialize Position: X, Y, Z (ON)
   - Serialize Rotation: Y (ON) — только vertical rotation
   - Serialize Scale: OFF (не нужна)
   - Interpolate: ON (плавное движение вместо шакотки)
   - Space: World (мировые координаты)
```

### На Boot сцене NetworkManager:
```
✅ EnableSceneManagement: true (сцена загружается для всех)
✅ PlayerPrefab: указывает на Player (IGR-48)
✅ DefaultNetworkPrefabs: содержит Player (IGR-50)
```

## Фактическое тестирование

### Тест A: Локально в редакторе
```
1. Запустить Boot сцену (Play)
2. Нажать Play в втором окне (Parrel Sync или --client arg)
3. В HOST окне нажать W → персонаж движется
4. В CLIENT окне видно что первый персонаж движется (синхронизация работает)
```

### Тест B: Build на двух машинах
```
Host machine:
  ./sleepover.exe --server
  
Client machine:
  ./sleepover.exe --client [host-ip] 7777
  
Expected:
  - Оба видят персонажей
  - Движение синхронизируется с ~30ms задержкой
  - Нет рассинхронов / "резиновых" персонажей
```

## Known Issues & Fixes

### Если движение не синхронизируется:
```
1. Проверить что Player.prefab в DefaultNetworkPrefabs ✅ (IGR-50)
2. Проверить что NetworkTransform настроена правильно (выше)
3. Проверить что EnableSceneManagement = true в NetworkConfig
4. Проверить что не используется InstantiateOptions.Destroy при спавне
```

### Если движение резиновое/дёрганое:
```
1. Включить Interpolate на NetworkTransform
2. Убедиться что TickRate = 30 (стандартный)
3. Может потребоваться ClientNetworkTransform вместо NetworkTransform
   (но сейчас NetworkTransform должна работать)
```

### Если персонажи падают сквозь пол:
```
1. Проверить что Rigidbody CollisionDetectionMode = Continuous
2. Проверить что Physics.gravity не отключена
3. Проверить что Ground check работает (SphereCast в PlayerController)
```

## Следующий шаг (IGR-52)
После этого добавим NetworkAnimator для синхронизации анимаций (idle/walk/jump).

## Следующий шаг (IGR-53)
Потом добавим ServerRpc для толчков (ApplyPush через сеть).
