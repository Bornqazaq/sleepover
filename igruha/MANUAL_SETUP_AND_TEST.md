# EPIC 2 Manual Setup & Testing Guide

## 📋 ШАГ 0: Подготовка
- [ ] Закрыть все окна Unity Editor
- [ ] Очистить konsole
- [ ] Убедиться что есть 2+ ядра (для Host + Client одновременно)

---

## 📝 ШАГ 1: Открыть проект в Unity

```bash
cd C:\Users\ASRock\Desktop\sleepover\igruha
# или просто откройте через Unity Hub
```

**Ожидание:** Unity загружается, компилирует скрипты (~30 сек)

**Проверка:** Console должна быть чистая (нет ошибок)

---

## ⚙️ ШАГ 2: Добавить DisconnectionHandler на NetworkManager

**В Boot сцене:**

1. Открыть `Assets/_Project/Scenes/Boot.unity`
2. В Hierarchy найти **NetworkManager** GameObject
3. Inspector → **Add Component**
4. Ввести "DisconnectionHandler" в search
5. Выбрать **DisconnectionHandler** из Networking namespace
6. **Save** сцену (Ctrl+S)

**Проверка:** 
```
✅ DisconnectionHandler компонент виден на NetworkManager
✅ Скрипт не показывает ошибок в Inspector
```

---

## 🎨 ШАГ 3: Добавить NetworkAnimator на Player.prefab

**Открыть prefab:**

1. `Assets/_Project/Prefabs/Player/Player.prefab` (double-click)
2. В Hierarchy вверху должна быть надпись **"Player (Prefab)"**

**Добавить NetworkAnimator:**

3. Inspector → **Add Component**
4. Search: "NetworkAnimator"
5. Выбрать **Unity.Netcode.NetworkAnimator**

**Настроить:**

6. В Inspector найти поле **Animator**
7. Drag-and-drop в это поле детский GameObject с Animator компонентом
   - Или просто нажать маленький кружок и выбрать из меню
   - Если не найдёшь — просто оставить пусто (auto-find обычно работает)

8. **Save** prefab (Ctrl+S в режиме prefab edit)

**Проверка:**
```
✅ NetworkAnimator компонент на Player
✅ Animator field не пуст (или серый = auto-find)
✅ Нет ошибок в Inspector
```

---

## 🎭 ШАГ 4: Заменить CharacterAnimatorDriver на NetworkCharacterAnimatorDriver

**На Player.prefab:**

1. Найти компонент **CharacterAnimatorDriver** в Inspector
2. Нажать три точки (⋮) → **Remove Component**
3. **Add Component** → Search: "NetworkCharacterAnimatorDriver"
4. Выбрать **NetworkCharacterAnimatorDriver** (из Igruha.Networking)

**Настроить:**

5. В поле **Animator Driver** скопировать ссылку на CharacterAnimatorDriver
   - Или просто оставить пусто (обычно auto-find срабатывает)

6. **Save** prefab (Ctrl+S)

**Проверка:**
```
✅ NetworkCharacterAnimatorDriver компонент на Player
✅ CharacterAnimatorDriver удалена полностью
✅ Нет ошибок компиляции
```

---

## 🧪 ШАГ 5: Сохранить и перекомпилировать

```bash
# В Unity:
- File → Save All (Ctrl+S)
- Wait for compilation (жди "Ready" в консоли)
```

**Проверка Console:**
```
✅ "Assets have been modified" → compilation started
✅ Нет красных ошибок
✅ "Compiling..." → "Ready" (или просто готово)
```

---

## 🚀 ШАГ 6: Запустить HOST инстанс

**Окно 1 (HOST):**

1. Откройте `Assets/_Project/Scenes/Boot.unity`
2. Нажать **Play** (Ctrl+P или кнопка Play)

**Ожидание:**
```
Console должен показать:
✅ "Compiling..."
✅ "🟢 Started as HOST - NetworkManager ready (with Connection Approval)"
✅ "✅ ConnectionApprovalManager: Connection Approval enabled"
✅ "✅ DisconnectionHandler: Ready for disconnections"
```

**Если ошибок:**
```
❌ "NetworkManager.Singleton is NULL" → DisconnectionHandler не добавлен
❌ "Animator not found" → NetworkAnimator не настроена
❌ Компиляция ошибок → сохранить и перезагрузить (Ctrl+R)
```

---

## 🎮 ШАГ 7: Запустить CLIENT инстанс (второе окно)

**Окно 2 (CLIENT):**

1. **Не закрывать первое окно!** Просто откройте вторую копию Unity
   - **Windows → Layouts → 2 Editor Windows** (или ParrelSync если установлен)
   - ИЛИ просто откройте второе окно Unity Editor отдельно

2. Откройте тот же проект (разные окна, один проект)

3. Откройте `Assets/_Project/Scenes/Boot.unity`

4. В AppNetworkManager.cs найди код и убедись что CLIENT mode включен:
   ```csharp
   bool isClientMode = System.Array.Exists(System.Environment.GetCommandLineArgs(),
       element => element.Equals("--client"));
   ```
   Это должно автоматически включиться если нет первого inстанса

5. Нажать **Play** (Ctrl+P)

**Ожидание Console:**
```
✅ "🟢 Started as CLIENT - Connecting to Host (with protocol version)"
✅ Может быть задержка в подключении (это OK)
```

**Если ошибок:**
```
❌ "Protocol version mismatch" → версии не совпадают
❌ "Server is full" → слишком много клиентов
❌ "Connection timeout" → HOST не работает в первом окне
```

---

## ✅ ШАГ 8: Проверить соединение

**В обоих окнах Console должно быть:**

```
HOST Console:
✅ 🟢 Started as HOST
✅ ✅ ConnectionApprovalManager enabled
✅ Client [ID] APPROVED
✅ 🟢 NetworkManager: Client [ID] connected

CLIENT Console:
✅ 🟢 Started as CLIENT
✅ Connected to Host
✅ 📡 [Player] Удалённый персонаж / 🎮 [Player] Мой персонаж
```

**В обоих окнах Scene view:**
```
✅ Видно 2 персонажа (оба Karlan'а - капсулы)
✅ Они на одном месте (0, 0, 0)
✅ Нет красных ошибок в Scene
```

---

## 🎯 ШАГ 9: Тест ДВИЖЕНИЯ

**В HOST окне:**

1. Нажать на Game view (чтобы фокус был там)
2. Нажать **W** → персонаж должен идти вперёд
3. Нажать **A/D** → боковое движение
4. Нажать **S** → идти назад

**В CLIENT окне одновременно:**
```
✅ Видишь что HOST персонаж движется
✅ Движение плавное (не резкое, не "резиновое")
✅ Пауза < 200ms между нажатием и видимым движением

Если рывки: это нормально на localhost, главное что синхронизируется
```

**В CLIENT окне нажать WASD:**
```
✅ В HOST окне видно что твой персонаж движется
✅ Синхронизация работает в оба конца
```

---

## 🎬 ШАГ 10: Тест АНИМАЦИЙ

**В HOST окне:**

1. Персонаж стоит → видна **idle** анимация (стоит на месте)
2. Нажать **W** → анимация меняется на **walk** (идёт)
3. Отпустить **W** → обратно на **idle**

**В CLIENT окне:**
```
✅ Видишь те же анимации что HOST
✅ Плавный переход между idle/walk
```

**Прыжок:**
1. HOST нажимает **Space** → прыгает
2. CLIENT видит прыжок в реальном времени

```
✅ Прыжок синхронизируется
✅ Анимация "Jump" видна
✅ Падение видно (auto-play)
```

---

## 💥 ШАГ 11: Тест ТОЛЧКОВ (Collision)

**Сценарий: HOST толкает CLIENT**

1. В HOST окне нажать **W** и идти в сторону CLIENT персонажа
2. Когда столкнутся → оба персонажа должны отскочить
3. Может включиться **Knockdown** анимация (падение)

**В CLIENT окне:**
```
✅ Видишь что HOST персонаж толкнул твоего
✅ Скорость изменилась (отскочил)
✅ Если была Knockdown анимация → она синхронизирована
```

**Проверить Console:**
```
HOST Console должен показать:
💥 [Player_0] ServerRpc ApplyPush — direction=..., force=...
✅ Толчок применён на сервере
```

---

## 🚪 ШАГ 12: Тест ОТКЛЮЧЕНИЯ

**Закрыть CLIENT:**

1. Нажать **Stop** в CLIENT окне (Ctrl+P или кнопка Stop)

**В HOST Console:**
```
✅ 🚪 CLIENT DISCONNECTED: ClientId=1
✅ Despawning player object
✅ Remaining players: 1
```

**В HOST Game view:**
```
✅ CLIENT персонаж исчезла со сцены
✅ Остался только HOST персонаж
```

**Закрыть HOST:**

2. Нажать **Stop** в HOST окне

**Что произойдёт:**
```
✅ Оба окна очищены
✅ Нет ошибок в Console
✅ Сцена обнулена
```

---

## 📊 Финальный CHECKLIST

- [ ] **Connection:** HOST показывает "Started as HOST", CLIENT показывает "Connected to Host"
- [ ] **Scene:** Видны 2 персонажа в обоих окнах
- [ ] **Movement:** WASD работает, движение синхронизируется между окнами
- [ ] **Animation:** idle/walk/jump видны и синхронизируются
- [ ] **Collision:** Толчки работают, персонажи отскакивают друг от друга
- [ ] **Disconnect:** Выход клиента не крашит HOST
- [ ] **Console:** Нет красных ошибок, только зелёные логи

**Если всё ✅ — EPIC 2 работает!**

---

## 🛠️ Если что-то не работает

### Ошибка: "Cannot find PlayerPrefab"
```
Решение:
1. Проверить что Player.prefab в DefaultNetworkPrefabs.asset
2. File → Reimport All Assets
3. Перезагрузить сцену (Ctrl+R)
```

### Ошибка: "NetworkAnimator not syncing"
```
Решение:
1. Проверить что NetworkAnimator добавлена на Player.prefab
2. Убедиться что Animator field заполнен (не пуст)
3. Проверить что на child GameObject есть Animator компонент
```

### Ошибка: "Movement doesn't sync"
```
Решение:
1. Проверить что NetworkTransform на Player.prefab
2. Убедиться что EnableSceneManagement = true
3. Проверить что PlayerController.enabled для owner'а
```

### Ошибка: "Collision doesn't work"
```
Решение:
1. Убедиться что Rigidbody.isKinematic = false
2. Проверить что CapsuleCollider на персонаже
3. Перезагрузить Boot сцену (Ctrl+R)
```

---

## 🎉 Успех!

Если всё работает — **EPIC 2 готова к разработке EPIC 3+ минигейм!**

Документация для разработчиков:
- `MOVEMENT_SYNC_README.md` — как работает синхронизация движения
- `ANIMATION_SYNC_SETUP.md` — как настраивать новые анимации
- `IMPULSE_RPC_GUIDE.md` — как добавлять новые толчки
- `NETWORK_PLAYTEST_CHECKLIST.md` — полный тест-кейс

Копировать паттерны из:
- `SessionManager.cs` — для состояния игры (NetworkVariable)
- `NetworkPlayerController.cs` — для сетевых действий (ServerRpc)

Погнали разрабатывать минигейм! 🚀
