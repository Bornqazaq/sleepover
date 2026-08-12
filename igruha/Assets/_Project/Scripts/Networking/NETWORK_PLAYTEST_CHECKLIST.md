# Network Playtest Checklist (IGR-54)

## Полный тест сетевой инфраструктуры

После выполнения IGR-47 до IGR-53, можно проверить что всё работает вместе.

---

## Setup: Запуск теста

### Вариант A: Local Editor (два окна)
```bash
# Terminal 1: Host instance
cd /path/to/igruha
unity -projectPath . --args -logFile Boot_Host.log

# Terminal 2: Client instance (ParrelSync или отдельный Editor)
unity -projectPath . --args --client -logFile Boot_Client.log
```

### Вариант B: Build на одной машине
```bash
# Build двух версий:
# 1. Build/Host/sleepover.exe
# 2. Build/Client/sleepover.exe

# Terminal 1 (Host)
./Build/Host/sleepover.exe

# Terminal 2 (Client)
./Build/Client/sleepover.exe --client localhost 7777
```

### Вариант C: Network (две машины)
```bash
# Machine 1 (HOST)
./sleepover.exe

# Machine 2 (CLIENT)
./sleepover.exe --client [HOST_IP] 7777
```

---

## Checklist: Что проверить

### ✅ Connection & Spawn (10 очков)
- [ ] **HOST window**: Console выводит "🟢 Started as HOST"
- [ ] **CLIENT window**: Console выводит "🟢 Started as CLIENT — Connecting to Host"
- [ ] **Both windows**: Нет ошибок вроде "NetworkManager.Singleton is NULL"
- [ ] **Both windows**: Console выводит "NetworkManager connected"
- [ ] **Scene**: Видны 2 персонажа (Karlan) — один для HOST, один для CLIENT
- [ ] **NetworkManager**: Оба показывают ConnectedClientIds = [0, 1]
- [ ] **Console clean**: Нет Network transport errors
- [ ] **Player spawn**: OnNetworkSpawn логирование видно
- [ ] **Player control**: HOST видит "это МОЙ персонаж" (ввод включен)
- [ ] **Remote players**: CLIENT видит HOST player как удалённый (ввод отключен)

### ✅ Movement Sync (10 очков)
- [ ] **HOST window**: Нажать W — персонаж начинает идти
- [ ] **CLIENT window**: Видит HOST персонажа идущего (в реальном времени)
- [ ] **CLIENT window**: Нажать W — его персонаж начинает идти
- [ ] **HOST window**: Видит CLIENT персонажа идущего
- [ ] **HOST window**: Отпустить W — персонаж останавливается
- [ ] **CLIENT window**: Видит остановку HOST персонажа
- [ ] **Smooth movement**: Движение плавное, без "резиновых" скачков
- [ ] **No desync**: Позиции совпадают на обоих (допустимо 1-2 frame задержку)
- [ ] **Speed parameter**: Console можно видеть NormalizedSpeed меняется
- [ ] **No errors**: Нет "NetworkTransform sync failed" ошибок

### ✅ Animation Sync (6 очков)
- [ ] **Idle animation**: Персонажи стоят в idle, когда не движутся
- [ ] **Walk animation**: Персонажи идут (Walk clip), когда нажимается WASD
- [ ] **Jump animation**: Нажать Space — персонаж прыгает и видна анимация
- [ ] **Remote jump**: CLIENT видит HOST прыгающего
- [ ] **Speed blending**: Анимация плавно переходит от idle к walk при ускорении
- [ ] **Sync timing**: Прыжки и падения синхронизированы между окнами

### ✅ Collision & Impulse (8 очков)
- [ ] **Collision detection**: Два персонажа могут столкнуться друг с другом
- [ ] **Push physics**: Когда одиничестоны, другой должен быть отолкнут
- [ ] **Knockdown trigger**: При сильном столкновении — Knockdown animation
- [ ] **Both see impulse**: HOST видит что CLIENT толкнул его
- [ ] **SERVER validates**: Толчок идёт через ServerRpc, логирование видно
- [ ] **Velocity sync**: После толчка скорость синхронизируется
- [ ] **Animation + physics**: Knockdown animation играет вместе с физикой
- [ ] **No double-push**: Толчок применяется один раз, не дублируется

### ✅ Stability & Errors (8 очков)
- [ ] **No crashes**: Ни один из процессов не крашится во время теста
- [ ] **Clean console**: Нет "Unhandled exception" или stack traces
- [ ] **No RPC errors**: Нет "RPC call failed" или "not registered"
- [ ] **No memory leaks**: Console memory не растёт экспоненциально
- [ ] **Graceful disconnect**: Если закрыть CLIENT → HOST не крашится
- [ ] **Graceful host exit**: Если закрыть HOST → CLIENT возвращается в меню
- [ ] **Long session**: Тест работает ≥5 минут без проблем
- [ ] **No desync over time**: После 5 мин движения позиции ещё синхронизированы

### ✅ Performance (6 очков)
- [ ] **FPS stable**: ≥30 FPS на обоих окнах
- [ ] **Network update rate**: ~30 updates/sec (TickRate = 30)
- [ ] **Input latency**: < 100ms от нажатия WASD до видимого движения
- [ ] **CPU usage**: < 50% на обычной машине
- [ ] **Network bandwidth**: ~5-10 KB/s на игрока (можно проверить в Profiler)
- [ ] **No frame drops**: Нет заметных дёрганий при синхронизации

---

## Test Scenarios (обязательные)

### Scenario 1: Basic Movement
```
Duration: 2 min
Steps:
1. HOST: WASD для движения по сцене
2. CLIENT: WASD для движения
3. Verify: Оба видят друг друга движущимися
4. Verify: Нет lag, плавное движение
```

### Scenario 2: Jump Sync
```
Duration: 1 min
Steps:
1. HOST: Нажимает Space 5 раз (прыгает)
2. CLIENT: Видит HOST прыгающего
3. CLIENT: Прыгает (Space) 5 раз
4. HOST: Видит CLIENT прыгающего
5. Verify: Прыжки синхронизированы, падение видно
```

### Scenario 3: Collision & Push
```
Duration: 2 min
Steps:
1. HOST: Движется к CLIENT персонажу
2. Collision: Персонажи сталкиваются
3. Verify: Оба ощущают толчок (физика работает)
4. Verify: Knockdown animation играет (если был сильный толчок)
5. Verify: CLIENT видит HOST толчок
6. Verify: HOST видит CLIENT толчок
```

### Scenario 4: Long Session
```
Duration: 5 min
Steps:
1. HOST + CLIENT просто двигаются, прыгают, сталкиваются
2. Every 1 min: Проверить что нет desync
3. Verify: Console clean (нет ошибок)
4. Verify: FPS stable
5. Verify: Позиции не разошлись
```

### Scenario 5: Graceful Shutdown
```
Duration: 30 sec
Steps:
1. HOST + CLIENT подвижны и синхронизированы
2. Закрыть CLIENT окно
3. Verify: HOST не крашится, Console чистая
4. Verify: "Player left" сообщение (когда добавим IGR-57)
5. Закрыть HOST окно
```

---

## Success Criteria

**All tests PASS if:**
- ✅ No crashes or unhandled exceptions
- ✅ Movement synced (positions match on both windows)
- ✅ Animations synced (idle/walk/jump visible)
- ✅ Physics working (collisions and impulses apply)
- ✅ No lag > 200ms
- ✅ Can run for 5+ minutes stable
- ✅ Console clean (no unexpected errors)

**Test FAILS if:**
- ❌ Either window crashes
- ❌ Positions diverge (desync > 1m after 1 min play)
- ❌ Animations don't sync (one player doesn't see other's jump)
- ❌ Physics broken (collisions don't push, impulses not applied)
- ❌ RPC errors in console
- ❌ Lag > 500ms consistently

---

## Debugging If Test Fails

### Symptom: CLIENT doesn't connect
```
Check:
1. PORT 7777 open on HOST machine
2. Firewall allows Unity
3. Both running Boot scene
4. NetworkManager enabled on both
5. Check Console for "Connection timeout" error
```

### Symptom: Positions don't sync
```
Check:
1. NetworkTransform on Player.prefab ✅
2. Player.prefab in DefaultNetworkPrefabs ✅
3. EnableSceneManagement = true ✅
4. Both see same ConnectedClientIds
5. Check for "NetworkTransform sync failed" errors
```

### Symptom: Animations don't sync
```
Check:
1. NetworkAnimator added to Player.prefab (IGR-52)
2. NetworkCharacterAnimatorDriver set up
3. Animator Controller has Speed, Jump, Knockdown parameters
4. Console shows animator parameter changes
5. No "Animator parameter not found" errors
```

### Symptom: Impulse doesn't work
```
Check:
1. ServerRpc decorated correctly (RequireOwnershipck = true)
2. Only owner calls RPC (check IsOwner)
3. Rigidbody.isKinematic = false
4. OnCollisionEnter fires (check logs)
5. Check for "RPC validation failed" errors
```

---

## After Test Passes

Document results:
- [ ] Date & time of test
- [ ] Machines used (local / network / build / editor)
- [ ] Duration: how long before any desync
- [ ] Any issues encountered and how fixed
- [ ] Performance metrics (FPS, bandwidth, latency)
- [ ] Signature / approval from team lead

Then proceed to:
- **IGR-265**: Connection Approval (validate client connections)
- **IGR-266**: NetworkVariable (game state sync)
- **IGR-55**: Relay integration (public server connection)

