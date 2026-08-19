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
6. **Добавить сцену в Build Settings** (галка включена) и вписать её имя в поле `sceneName` конфига.

> **Не класть сцену в Addressables.** NGO опознаёт сцены по индексу в списке сборки, и сцена
> без индекса отбивается на валидации ещё до отправки: по сети она просто не загрузится.
> Addressables же вычищает из своих групп всё, что попало в Build Settings — два пути
> взаимно исключают друг друга. Путь один: Build Settings.
>
> Имя в `sceneName` — короткое (`Stopwatch`), в путь его разворачивает `BuildSceneCatalog`.

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
| Колесо эмоций и танцев (Tab) | `EmoteWheel` + `PlayerEmoteAbility`, привязывает `MinigameBootstrap` | Core/UI, Core/Player |
| Приседание с реальным сжатием капсулы, видно всем | `PlayerController.IsCrouched`, реплицируется `NetworkPlayerController` | Core/Player |
| Свой персонаж каждому игроку | сервер выдаёт из `CharacterRoster` при одобрении подключения | Networking |
| Возврат в хаб после результатов | `MinigameControllerBase`, через `resultsDisplaySeconds` | Core/Minigame |

### Что из этого влияет на дизайн, а не только на код

- **Персонажа выбирает сервер, не игрок.** В ростере 5 моделей на 8 мест: до пяти
  участников все разные, дальше идут по кругу. Механику «выбери героя под роль»
  в спеку не закладывать.
- **Приседание — полноценная механика укрытия.** Капсула реально сжимается, и это
  видит сервер, а не только сам приседающий. На прятки опираться можно.
- **Выход из мини-игры проектировать не надо.** После экрана результатов сервер сам
  возвращает всех в хаб.
- **Танцы настроены только у одного персонажа из пяти** (Шланга). Строить на эмоциях
  геймплей пока нельзя, как украшение — можно.

### Правило, которое стоило отдельного дня

**Любую роль или блокировку, которую мини-игра вешает на игрока, она обязана снимать
сама в `OnRoundEnded`.** Персонаж переезжает между сценами живым — это `NetworkObject`,
— и незакрытая роль уезжает в хаб вместе с ним. У «Ангелов» так уехала блокировка
движения Водящего: он приезжал в хаб обездвиженным и неуязвимым, пока остальные ходили.

Симптом «не работает у одного человека из всех» — почти всегда именно это, а не общий
баг игры. Смотреть, чем этот игрок отличался: роль, персонаж, хост он или нет.

## Иерархия сцены-шаблона

```
_Arena       геометрия (пол, платформа, препятствие)
_Spawns      SpawnPointSet + PlayerSpawner, 8 точек Default + 1 Special
_Bounds      KillZone_Bottom под ареной
_Traps       SpringTrap, FallingCrateTrap, TrapButton (жмётся на E)
_Pickups     PickupCube (подбор на E, бросок ЛКМ)
_UI          Canvas: TimerText, TutorialPanel, ResultsPanel, EmoteWheel; EventSystem
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
