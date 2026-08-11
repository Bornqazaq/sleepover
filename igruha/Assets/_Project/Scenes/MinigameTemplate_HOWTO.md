# Как сделать новую мини-игру из шаблона

Шаблон = сцена `MinigameTemplate.unity` + системы в `Igruha.Core`.
Правило: если механика может понадобиться второй игре — её место в Core, а не в папке игры.

## 6 шагов

1. **Скопировать сцену.** `MinigameTemplate.unity` → `Assets/_Project/Scenes/Minigames/<ИмяИгры>.unity`.
2. **Создать конфиг.** `Create → Igruha → Minigame Definition` в `Assets/_Project/Settings/Gameplay/Minigames/`.
   Заполнить: название, цель (текст обучалки), подсказки управления, длительность раунда,
   мин/макс игроков, категорию (FreeForAll / Team / Asymmetric), режим камеры.
3. **Написать контроллер игры.** Класс в `Assets/_Project/Scripts/Minigames/<ИмяИгры>/`,
   наследник `MinigameControllerBase`. Обязателен только `CollectResults` — расставить места.
   Опционально переопределить `OnPlayersReady` / `OnRoundStarted` / `OnRoundEnded`.
   Досрочное завершение — вызвать `EndMinigame()`.
4. **Заменить начинку сцены.** В `_Arena` — своя геометрия; в `_Spawns` — свои точки
   (роль `Special` для охотника/ведущего/оператора); в `_Traps` и `_Pickups` — свои объекты.
5. **Перецепить ссылки на `MinigameManager`.** Снять `TemplateMinigame`, повесить свой контроллер,
   назначить ему Definition / RoundTimer / TutorialScreen / RoundHud, а в `MinigameBootstrap` —
   Spawner / Minigame / CameraController.
6. **Зарегистрировать сцену в Addressables** и прописать её адрес в поле `sceneAddress` конфига.

## Что даёт шаблон бесплатно

| Система | Компонент | Где лежит |
|---|---|---|
| Бег/прыжок/поворот, инерция | `PlayerController` + `CharacterConfig` | Core/Player |
| Импульс-толчок между игроками | `PlayerPushAbility` → `PlayerController.ApplyPush` | Core/Player |
| Смешное падение и вставание | `PlayerController.Knockdown` + `CharacterAnimatorDriver` | Core/Player |
| Респаун | `PlayerRespawner` | Core/Player |
| Границы и зоны падения | `KillZone` (режимы Respawn / EventOnly) | Core/Arena |
| Спавн 2–8 с ролями | `SpawnPoint`, `SpawnPointSet`, `PlayerSpawner` | Core/Spawning |
| Таймер раунда + автозавершение | `RoundTimer` | Core/Minigame |
| Обучающая заставка | `TutorialScreen` (данные из Definition) | Core/UI |
| HUD таймера и результатов | `RoundHud` | Core/UI |
| Подбор и бросок предметов | `PickupItem`, `PlayerCarryAbility` | Core/Items |
| Стрельба с отдачей и «жидким» прицелом | `ProjectileShooter`, `Projectile` | Core/Combat |
| Кнопки и ловушки | `TrapActivationButton`, `SpringTrap`, `FallingCrateTrap` | Core/Traps |
| Взаимодействие (E) | `IInteractable`, `PlayerInteractor` | Core/Interaction |
| Камера под тип игры | `MinigameCameraController` + `PartyCameraRig.prefab` | Core/CameraSystems |
| Очки по местам | `SessionManager` (`очки = число_игроков − место`) | Core/Session |

## Иерархия сцены-шаблона

```
_Arena       геометрия (пол, платформа, препятствие)
_Spawns      SpawnPointSet + PlayerSpawner, 8 точек Default + 1 Special
_Bounds      KillZone_Bottom под ареной
_Traps       SpringTrap, FallingCrateTrap, TrapButton (жмётся на E)
_Pickups     PickupCube (подбор на E, бросок ЛКМ)
_UI          Canvas: TimerText, TutorialPanel, ResultsPanel; EventSystem
_Camera      Main Camera (CinemachineBrain) + PartyCameraRig
_Lighting    Directional Light
MinigameManager  SessionManager, RoundTimer, TemplateMinigame, MinigameBootstrap
```

## Локальный тест без сети

`PlayerSpawner.debugPlayerCount` (1–8) — сколько персонажей заспавнить.
Первый управляется игроком, остальные — манекены с выключенным `PlayerInputReader`
(нужны, чтобы проверять толчки, ловушки и попадания в одиночку).

## Network-ready: что станет серверным

Логику менять не придётся — эти вызовы просто уйдут за `IsServer` / `ServerRpc`:
`PlayerController.ApplyPush` / `ApplyImpulse` / `Knockdown` / `TeleportTo`,
`ProjectileShooter.Fire`, `TrapBase.Activate`, `PickupItem.OnPickedUp` / `OnThrown`,
`MinigameControllerBase.EndMinigame` и `CollectResults`, начисление очков в `SessionManager`.
Состояние раунда уже собрано в `RoundTimer` / `MinigameResults` / `SessionPlayer` —
это то, что мигрирует в `NetworkVariable`, а не разрозненные поля MonoBehaviour.
