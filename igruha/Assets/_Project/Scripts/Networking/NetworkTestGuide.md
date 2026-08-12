# Network Test Guide — Host + Client Локальное Соединение

## Сценарий A: Editor Localhost (быстрый прототип)

### Что проверяем
- ✅ HOST запускается и выводит "Started as HOST"
- ✅ CLIENT подключается к HOST
- ✅ Оба видят друг друга в NetworkManager.ConnectedClientIds
- ✅ Player.prefab спавнится для обоих
- ✅ Движения синхронизируются (когда реализуем IGR-51)

### Как запустить

#### На машине с 2+ ядрами:
1. **Окно 1 (HOST):**
   - Unity Editor → Scenes/Boot.unity
   - Play (Ctrl+P)
   - Ожидание: Console → "🟢 Started as HOST - NetworkManager ready"

2. **Окно 2 (CLIENT):**
   - Unity Editor → Scenes/Boot.unity
   - Play (Ctrl+P)
   - Код в AppNetworkManager.cs будет запущен как CLIENT (второе окно)
   - Ожидание: Console → "Connected to Host" (когда реализуем client code)

#### На машине с 1 ядром:
- Использовать ParrelSync или игровой build вместо второго окна редактора

### Текущий статус (на момент IGR-49)
```csharp
// AppNetworkManager.cs сейчас:
if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
{
    NetworkManager.Singleton.StartHost();  // HOST mode
    Debug.Log("🟢 Started as HOST");
}

// TODO: ClientMode для второго инстанса
```

### Что добавить для полного теста
```csharp
// AppNetworkManager.cs (расширить):
private bool isHost = true;  // or read from launch args

private void Start()
{
    if (isHost)
        NetworkManager.Singleton.StartHost();
    else
        NetworkManager.Singleton.StartClient();  // localhost по умолчанию
}
```

---

## Сценарий B: Build Test (реальный bilд)

### Создание двух конфигураций Build

#### HostBuild:
```
1. File → Build Settings
2. Scenes: Assets/_Project/Scenes/Boot.unity
3. Platform: Windows (Standalone)
4. Build folder: Build/Host
5. Build
6. В Build/Host/sleepover.exe добавить launcher script или аргумент --server
```

#### ClientBuild:
```
1. Аналогично HostBuild, но folder: Build/Client
2. Аргумент: --client
```

### Как запустить оба build'а
```bash
# Terminal 1 (Host)
./Build/Host/sleepover.exe --server

# Terminal 2 (Client)  
./Build/Client/sleepover.exe --client localhost 7777
```

### Expected Output

**HOST Console:**
```
🟢 Started as HOST - NetworkManager ready
✅ NetworkManager.IsHost: true
✅ Player spawned (NetworkObject ID: 1)
```

**CLIENT Console:**
```
✅ Connecting to localhost:7777...
✅ Connected to HOST
✅ Player spawned (NetworkObject ID: 2)
```

**Оба видят друг друга:**
```
NetworkManager.ConnectedClientIds: [0, 1]
```

---

## Отладка

### Если CLIENT не подключается
```csharp
// Check Console для:
- "Connection timeout"
- "Connection refused"
- Network transport errors

// Решение:
1. Убедиться что HOST запущен первым
2. Убедиться что PORT 7777 открыт
3. Firewall может блокировать — добавить исключение
```

### Если Player не спавнится
```csharp
// Check:
- DefaultNetworkPrefabs.asset содержит Player.prefab ✅ (IGR-50)
- NetworkManager.PlayerPrefab указана ✅ (IGR-48)
- Console нет ошибок "Cannot find prefab"
```

### Network Logs
```csharp
// Включить подробное логирование:
NetworkManager.NetworkConfig.EnableNetworkLogs = true;  // ✅ уже enabled

// Console покажет:
- Все RPC вызовы
- Все NetworkVariable changes
- Все спавны объектов
```

---

## Definition of Done для IGR-49
- [ ] Запустить Host — видно "Started as HOST"
- [ ] Запустить Client (второе окно/build) — видно "Connected"
- [ ] Оба инстанса видят друг друга в ConnectedClientIds
- [ ] Player.prefab спавнится для обоих (проверить в Scene Hierarchy)
- [ ] Console обоих чист от Network errors
- [ ] Написана документация по запуску (этот файл)

---

## Когда будет полная синхронизация (IGR-51+)
```csharp
// После IGR-51 (Movement sync):
// В HOST окне нажать W/A/S/D
// → CLIENT окно видит движение в реальном времени
```
