# Персонажи: откуда что берётся

Карта источников на персонажа — какой файл даёт меш/материал, какие файлы дают анимации Animator Controller, откуда назначена текстура. Проверено вживую в редакторе (Motion состояний Animator Controller + `SkinnedMeshRenderer.sharedMaterial` собранного префаба), не по именам файлов на глаз.

| Персонаж | Меш/материал (рендерится) | Анимации (Motion в Animator Controller) | Текстура |
|---|---|---|---|
| **Karlan** | `Art/Models/Untitled_K.fbx` | `Karlan@*.fbx` + `Karlan(Fbx without color)@*.fbx` | `Color_892d1a28-...jpg`, авто-привязана Unity при импорте `Untitled_K.fbx` |
| Boss | `Boss@defaultRunning.fbx` (тот же файл) | все `Boss@*.fbx` | `Color_42f34e40-...jpg`, авто-привязана Unity |
| Shlanga | `Shlanga@idle.fbx` (тот же файл) | все `Shlanga@*.fbx` | `Color_2d80b265-...jpg`, авто-привязана Unity |
| Fat | `Fat@Neutral Idle.fbx` (тот же файл) | все `Fat@*.fbx` | `Textures/Fat.jpg` → `Art/Materials/Fat.mat`, remap через `CharacterPrefabBuilder` |
| MyBoy | `MyBoy@Old Man Idle.fbx` (тот же файл) | все `MyBoy@*.fbx` | `Textures/MyBoy.jpg` → `Art/Materials/MyBoy.mat`, remap через `CharacterPrefabBuilder` |

## Karlan — исключение из общей схемы

У Boss/Shlanga/Fat/MyBoy меш и анимации всегда приходят из одного и того же семейства `Имя@*.fbx`: `CharacterPrefabBuilder` берёт ЛЮБОЙ из клипов персонажа как модель (в нём уже есть скелет+меш+материал), и его же (или другой клип того же персонажа) — как позу Idle для замера роста. Один файл — две роли, конвенция единая.

**Karlan так не устроен.** Он собирался руками ещё в EPIC 1, до того как появился общий пайплайн (`CharacterPrefabBuilder`/`CharacterClipImportSetup`). Тогда меш/материал/текстуру взяли из отдельного, специально приготовленного файла `Untitled_K.fbx` (лежит в `Art/Models/`, не в `Art/Animations/`), а анимации — из пачки Mixamo-клипов `Karlan@*.fbx` и `Karlan(Fbx without color)@*.fbx`. У каждого из этих Mixamo-клипов ТОЖЕ есть свой встроенный меш+материал (так устроен экспорт Mixamo/Tripo), но он нигде не используется — сцена рендерит фигуру из `Untitled_K.fbx`, а из `Karlan@*.fbx`/`Karlan(Fbx without color)@*.fbx` берутся только кривые анимации (Motion в состояниях Animator Controller).

Проверено вживую (не предположение):
- `PlayerAnimator.controller`: все 8 состояний (`Idle`, `Run`, `Jump`, `Punch`, `FlyBack`, `StandUpFromBack`, `FallForward`, `StandUpFromForward`) ссылаются на Motion из `Karlan@Happy Idle.fbx`, `Karlan@Fast Run.fbx` и `Karlan(Fbx without color)@*.fbx` — файлы рабочие, используются.
- `Player.prefab` → визуал → `SkinnedMeshRenderer.sharedMaterial` резолвится в `Art/Models/Untitled_K.fbx` — меш и текстура рендерятся именно оттуда.

Из-за этого расхождения в предыдущей сессии я по ошибке принял `Karlan@Fast Run.fbx` за «неиспользуемый» файл: у его СОБСТВЕННОГО встроенного материала текстура была пустая (потому что вообще ничего не рендерит этот встроенный материал), и я спутал «материал этого конкретного sub-asset не назначен» с «файл не используется». Файл используется — просто не для меша.

### Стоит ли приводить Karlan к общей схеме?

Не сейчас. Аргументы against:
- Karlan уже работает целиком (рендер, текстура, все 8 анимаций) — трогать нечего чинить.
- Перевод на конвенцию «один файл — меш и анимации» означал бы взять меш из одного `Karlan@*.fbx`/`Karlan(Fbx without color)@*.fbx` вместо `Untitled_K.fbx`, а значит — заново пересчитать рост/капсулу (`CharacterPrefabBuilder`), перепроверить, что мэш/риг остальных клипов идентичны по пропорциям `Untitled_K.fbx` (не факт: `Untitled_K.fbx` мог быть создан отдельно, с другими пропорциями тела), и заново подтвердить текстуру. Чистый риск ради нуля функционального выигрыша.
- `Untitled_K.fbx`, вероятно, был выбран как визуал сознательно (иначе не было бы отдельного файла в `Art/Models/`, а не в `Art/Animations/`) — качество меша/текстуры там может быть выше, чем в первом попавшемся Mixamo-клипе.

Если когда-нибудь Karlan станет действительно проблемным (например, `Untitled_K.fbx` придётся заменить или его пропорции разъедутся с анимациями), тогда есть смысл провести его через `CharacterPrefabBuilder` как обычного персонажа — но не раньше, чем появится конкретная причина.

### Удалённый мёртвый файл

`Karlan@Jumping.fbx` не входил в список выше: не был привязан ни к одному состоянию `PlayerAnimator.controller` (Jump использует `Karlan(Fbx without color)@Unarmed Jump.fbx`) и не встречался больше нигде в проекте ни по имени, ни по guid (`b03eacab24ffe43418bb903a2a3ecaa8`) — проверено `grep` по всей `Assets/`. Похоже, дублирующий/альтернативный клип прыжка, который так и не подключили. Удалён.
