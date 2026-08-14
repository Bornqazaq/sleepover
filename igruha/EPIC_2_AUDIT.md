# EPIC 2 Audit & Validation — Сетевая инфраструктура (Networking Core)

**Дата аудита:** 2026-08-12  
**Статус:** ✅ Структура валидна, дефекты требуют фиксации перед разработкой  
**Выявленных проблем:** 6 критических, 3 в логике описаний

---

## 📊 Статус тикетов (на момент аудита)

| ID | Название | Статус | Приоритет | Заметки |
|---|---|---|---|---|
| IGR-46 | 2.1 NGO install | ✅ Done | Urgent | ✅ Завершено |
| IGR-47 | 2.2 Relay + Lobby | ❌ Todo | Urgent | ⚠️ Пакеты НЕ установлены |
| IGR-48 | 2.3 NetworkManager | ⚠️ In Progress | Urgent | ⚠️ Дефекты: PlayerPrefab=NULL, DefaultNetworkPrefabs пуст |
| IGR-49 | 2.4 Host+Client тест | ❌ Todo | Urgent | ⚠️ Требует билдов + работающей синхронизации |
| IGR-50 | 2.5 Network Player | ⚠️ In Progress | Urgent | ⚠️ Компоненты есть, но префаб не зарегистрирован в сети |
| IGR-51 | 2.6 Movement sync | 📋 Backlog | Urgent | ✅ Описание корректно |
| IGR-52 | 2.7 Animation sync | 📋 Backlog | Urgent | ✅ Описание корректно |
| IGR-53 | 2.8 Impulse RPC | 📋 Backlog | Urgent | ✅ Описание корректно |
| IGR-54 | 2.9 Network test | 📋 Backlog | Urgent | ✅ Описание корректно |
| IGR-55 | 2.10 Relay integration | 📋 Backlog | Urgent | ✅ Зависит от IGR-47 |
| IGR-56 | 2.11 Join by code | 📋 Backlog | Urgent | ✅ Зависит от IGR-55 |
| IGR-57 | 2.12 Disconnect handling | 📋 Backlog | Urgent | ✅ Описание корректно |
| IGR-58 | 2.13 Host disconnect | 📋 Backlog | Urgent | ✅ Описание корректно |

---

## 🔍 Детальные находки

### 1️⃣ IGR-46 (2.1 NGO install) — ✅ ЗАВЕРШЕНО

**Проверка:**
- ✅ `com.unity.netcode.gameobjects@1.10.0` установлен в `Packages/manifest.json`
- ✅ Assembly Definition обновлены для NGO
- ✅ Коммит 2ab6c84 подтверждает завершение

**Действие:** Закрыть как Done (если ещё не закрыт).

---

### 2️⃣ IGR-47 (2.2 Relay + Lobby) — ❌ ТРЕБУЕТ НЕМЕДЛЕННЫХ ДЕЙСТВИЙ

**Текущее состояние:**
- ❌ `com.unity.services.relay` — **НЕ установлен**
- ❌ `com.unity.services.lobby` — **НЕ установлен**
- ❌ UGS проект — **НЕ завёден**

**Проверка (manifest.json):**
```json
// ОТСУТСТВУЮТ эти строки:
"com.unity.services.relay": "1.0.0",
"com.unity.services.lobby": "1.0.1",
// ЕСТЬ (но недостаточно для полной сетевой инфраструктуры):
"com.unity.multiplayer.center": "1.0.1"
```

**Что нужно сделать:**
1. Добавить пакеты в `Packages/manifest.json`:
   ```json
   "com.unity.services.relay": "1.0.0",
   "com.unity.services.lobby": "1.0.1"
   ```
2. Создать/связать проект с UGS (https://unity.com/gaming-services)
3. Получить Project ID и добавить в EditorBuildSettings / ProjectSettings

**Статус для развития:** 🔴 БЛОКИРУЕТ IGR-55 (Relay-интеграция)

---

### 3️⃣ IGR-48 (2.3 NetworkManager) — ⚠️ ТРЕБУЕТ ИСПРАВЛЕНИЯ

**Текущее состояние (Boot сцена):**
```
✅ GameObject "NetworkManager" существует
✅ Компонент NetworkManager (Unity.Netcode)
✅ UnityTransport настроен (127.0.0.1:7777, TickRate 30)
❌ ДЕФЕКТ 1: PlayerPrefab = NULL
❌ ДЕФЕКТ 2: DefaultNetworkPrefabs.asset существует, но содержит 0 префабов
❓ ДЕФЕКТ 3?: Тикет упоминает дубль AppNetworkManager, но найдена только 1 сцена (может быть уже исправлено)
```

**Проверка DefaultNetworkPrefabs:**
- Файл существует: `Assets/DefaultNetworkPrefabs.asset`
- Статус: Пуст (0 entries), нужно добавить Player prefab

**Что нужно сделать:**
1. Проверить/удалить дубль AppNetworkManager (если есть в Boot)
2. Добавить Player.prefab в DefaultNetworkPrefabs через NetworkPrefabsList
3. Установить PlayerPrefab ссылку на NetworkManager (если требуется для auto-spawn)

**Зависимость:** Требует завершения IGR-50

---

### 4️⃣ IGR-49 (2.4 Host+Client тест) — ❌ НЕ МОЖЕТ БЫТЬ ВЫПОЛНЕНО

**Почему тикет сейчас неполный:**
- Требует готового Network Manager (IGR-48 — ⚠️ с дефектами)
- Требует сетевого Player (IGR-50 — ⚠️ неполный)
- Требует готовых билдов (сейчас отсутствуют)
- Требует Relay-инфраструктуры для финального теста с удалённым IP (IGR-47)

**Текущие ограничения:**
```
❌ Build/Builds папки отсутствуют
❌ Build settings не настроены специально для Host/Client
✅ Сцена Boot существует (автоматически загружается)
```

**Рекомендуемое разбиение:**
1. **Подзадача 2.4a (localhost тест):** Host + Client на одной машине, локальный loop (требует IGR-48 fix + IGR-50 complete)
2. **Подзадача 2.4b (построение):** Создать две конфигурации Build — "HostBuild" и "ClientBuild" с разными defines/аргументами

**Статус для развития:** 🟡 МОЖЕТ ИДТИ после завершения IGR-48 + IGR-50

---

### 5️⃣ IGR-50 (2.5 Network Player) — ⚠️ ТРЕБУЕТ ЗАВЕРШЕНИЯ

**Текущее состояние:**
```
✅ Player.prefab имеет:
   - NetworkObject компонент
   - NetworkTransform компонент
   - NetworkPlayerController скрипт (гейтит ввод по IsOwner)

❌ ДЕФЕКТ: Префаб НЕ зарегистрирован в сети
   - DefaultNetworkPrefabs.asset пуст (0 entries)
   - NetworkManager.PlayerPrefab = NULL
```

**Проверка скрипта NetworkPlayerController:**
```csharp
✅ Проверяет IsOwner
✅ Отключает PlayerController для удалённых игроков
✅ Логирует корректно
⚠️ Может потребоваться расширение для RPC-команд (толчки и т.д.)
```

**Что нужно сделать:**
1. Добавить Player.prefab в DefaultNetworkPrefabs
2. Убедиться, что NetworkTransform использует правильный режим синхронизации (для NGO это обычно ClientNetworkTransform с server-side validation)
3. Проверить, что компонент PlayerController кэшируется в Awake (по CLAUDE.md правилу 2)

**Зависимость для:** IGR-51, IGR-52, IGR-53, IGR-54

---

### 6️⃣–13️⃣ IGR-51 до IGR-58 (2.6–2.13) — 📋 BACKLOG, СТРУКТУРНО КОРРЕКТНЫ

Все описания соответствуют CLAUDE.md требованиям:

| ID | Тема | Проверка |
|---|---|---|
| IGR-51 | Movement sync via ServerRpc | ✅ ClientNetworkTransform парадигма правильная |
| IGR-52 | NetworkAnimator | ✅ Правильный инструмент для синхронизации |
| IGR-53 | Impulse RPC | ✅ ServerRpc для толчков — правильно |
| IGR-54 | Network playtest | ✅ Валидация на реальном мультиплеере |
| IGR-55 | Relay join | ✅ Зависит от IGR-47 |
| IGR-56 | Join by code | ✅ Зависит от IGR-55 |
| IGR-57 | Disconnect handling | ✅ Обязательно для стабильности |
| IGR-58 | Host exit handling | ✅ Обязательно для стабильности |

---

## 🚫 Пропущенные тикеты (ДОЛЖНЫ БЫТЬ ДОБАВЛЕНЫ)

### Новый тикет: 2.14 Connection Approval (авторитет сервера)

**Зачем:** По CLAUDE.md 3.1, сервер — единственный источник истины. Connection Approval отсеивает неавторизованных клиентов на уровне NGO.

**Описание:**
```
Реализовать ConnectionApprovalCallback на сервере:
1. Валидировать данные подключения клиента
2. Проверять версию протокола
3. Отклонять читерские/невалидные подключения
4. (Опционально) валидировать лицензию/баны

Включить Connection Approval в NetworkConfig.
```

**Приоритет:** Urgent  
**Зависит от:** IGR-47 (Relay), IGR-55 (для полного функционала)  
**Блокирует:** IGR-57, IGR-58 (graceful shutdown нужна авторитетная архитектура)

---

### Новый тикет: 2.15 NetworkVariable для игрового состояния (Example: SessionManager NetworkVariable)

**Зачем:** По CLAUDE.md 3.2, состояние (не события) должно идти через NetworkVariable. Нужен боевой пример для других эпиков.

**Описание:**
```
На примере SessionManager показать правильное использование NetworkVariable:
1. Счёт матча → NetworkVariable<int>[]
2. Текущий раунд → NetworkVariable<int>
3. Фаза игры (Lobby, Playing, Results) → NetworkVariable<GamePhase>

Использовать ChangeEvent для синхронизации UI.
Не использовать RPC для передачи состояния.
```

**Приоритет:** High  
**Зависит от:** IGR-50 (Network Player)  
**Блокирует:** EPIC 4 (Game Session), EPIC 6+ (мини-игры)

---

### Обновления к существующим тикетам

**IGR-49 (2.4)** — Переделать описание:
- Разделить на два этапа: **localhost эмуляция** (для быстрой проверки) и **build-тест** (для реального билда)
- Добавить Definition of Done: "Две окна отвечают друг другу, синхронизируют позиции, видят толчки"

**IGR-57 & IGR-58** — Добавить требование обработки в CLAUDE.md:
- На сервере: обнаружить отключение, очистить данные игрока, объявить остальным
- На клиенте: получить сигнал, вернуться в лобби, показать "Player Left" сообщение
- Test case: Host вынимает кабель → все клиенты корректно выходят в меню

---

## 📋 Рекомендуемый порядок разработки

```
🟢 IGR-46 ✅ ПРОПУСТИТЬ (уже done)

🔴 БЛОКЕР: IGR-47 (Relay + Lobby) — НАЧАТЬ ОТСЮДА
└─ Зависимость: нужна UGS-инфраструктура

🟡 IGR-48 (NetworkManager fix) + IGR-50 (Player registration) — ПАРАЛЛЕЛЬНО
└─ Оба работают на Boot сцене, независимо друг от друга
└─ После этого можно запустить сцену и увидеть PlayerSpawn

🟡 NEW: IGR-2.14 (Connection Approval) — ПОСЛЕ IGR-48+50
└─ Требует работающего NetworkManager

🟡 IGR-51–54 (Movement, Animation, Impulse, Playtest) — ПОСЛЕДОВАТЕЛЬНО
└─ Каждый зависит от предыдущего

🟡 IGR-55–56 (Relay join, Join by code) — ПОСЛЕ IGR-47 COMPLETE
└─ Требует Relay infrastructure

🟡 IGR-57–58 (Disconnect, Host exit) — ПОСЛЕ 55+56
└─ Network должна быть в рабочем состоянии

🔵 NEW: IGR-2.15 (NetworkVariable example) — ПОСЛЕ 51–54
└─ Требует готового сетевого Player + Movement
```

---

## ✅ Контрольный список перед началом EPIC 2 разработки

- [ ] UGS проект завёден (для Relay/Lobby)
- [ ] Relay + Lobby пакеты добавлены в manifest.json (IGR-47)
- [ ] Boot сцена: одиночный NetworkManager (без дублей)
- [ ] Player.prefab зарегистрирован в DefaultNetworkPrefabs (IGR-50)
- [ ] NetworkManager.PlayerPrefab указывает на Player prefab (IGR-48)
- [ ] Сценарий тестирования Host+Client написан в Definition of Done (IGR-49)
- [ ] Добавлены новые тикеты 2.14 и 2.15 в Linear
- [ ] Все члены команды согласны с порядком разработки (выше)
- [ ] CLAUDE.md отражает Connection Approval и NetworkVariable парадигмы (доп. обновление)

---

## 📝 Выводы

1. **Структурно** EPIC 2 хорошо разбит и следует CLAUDE.md
2. **Практически** есть 6 критических дефектов, требующих фиксации перед полноценной разработкой
3. **Методологически** пропущены 2 ключевых тикета (Connection Approval, NetworkVariable example)
4. **Зависимости** не все явно отмечены в Linear → нужно добавить links после фиксации

**Рекомендация:** Выполнить ✅ контрольный список, затем начать с IGR-47, параллельно IGR-48+50.

