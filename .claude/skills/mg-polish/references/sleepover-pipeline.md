# Карта проекта для шлифовки

Пути ниже относительны корню репозитория `sleepover`, а не папке скилла.
Это ориентиры из проекта на 15.09.2026. Перед запуском сборщика прочитай его актуальный
код: команда может сохранять несколько сцен. Этот справочник не является статусом игр.
Примеры показывают инструменты и прежние решения; их расстановка и размеры не
ограничивают разрешённую художественную переработку выбранной сцены. Аудиты старой
геометрии адаптируй к новой планировке, сохраняя проверки игровой функции.

## Источники и размещение

| Что | Где |
|---|---|
| Статус и принятые уточнения | `STATE.md`; ищи последние разделы по игре |
| Дизайн и арт-бриф | `docs/minigames/<slug>.md` |
| Правила и замороженное | `CLAUDE.md`, `igruha/CLAUDE.md` |
| Игровые сцены | `igruha/Assets/_Project/Scenes/Minigames/<Game>.unity` |
| Параметры игры | `igruha/Assets/_Project/Settings/Gameplay/Minigames/` |
| Игровая логика | `igruha/Assets/_Project/Scripts/Minigames/<Game>/` |
| Сборка сцены и арта | `igruha/Assets/_Project/Scripts/Editor/` |
| Генераторы Blender | `tools/blender/` |
| Редактируемые 3D-исходники | `igruha/Assets/_Project/Art/Source/<Kit>/` |
| Игровые модели и текстуры | `igruha/Assets/_Project/Art/<Kit>/Models/`, `Textures/` |
| Материалы и префабы | Следуй существующему расположению набора внутри `Materials/` и `Prefabs/` |
| Кадры для сравнения | `docs/art/<slug>/` |
| Звук и события | `igruha/Assets/_Project/Audio/<Game>/`, `docs/art/<slug>-sfx.json` |

Названия: `cans-order` → `CansOrder`, `stopwatch` → `Stopwatch`,
`crying-angels` → `CryingAngels`, `memory-run` → `MemoryRun`,
`carry-item` → `CarryItem`, `exam` → `Exam`, `believe-or-not` → `BelieveOrNot`,
`hole-in-wall` → `HoleInWall`, `duck-hunt` → `DuckHunt`.

Новые бинарные модели, текстуры, звук и `.blend` подпадают под существующий
`.gitattributes`/LFS. Сохраняй `.meta` при замене уже импортированных файлов.
Для анимаций не дублируй весь тяжёлый меш в каждом клипе без необходимости.

## Проверенные примеры — открывать по задаче

### «Порядок банок» и «Секундомер»: собственный цирк

Общий набор — `CircusNight`; техническое имя сохранилось после смены атмосферы.
Последнее принятое направление: красно-сливочный шатёр, тёплый сценический свет,
дерево, латунь, ярмарочный реквизит. Ранняя бордово-бирюзовая ночь устарела.
Смотри `STATE.md` 3.93a–3.93g и более поздние уточнения, если появились.

- Геометрия: `tools/blender/circus_night.py`, `circus_fairground.py`.
- Собственный Бруно: `tools/blender/circus_bear.py`, `circus_bear_motion.py`.
- Исходники: `Art/Source/CircusNight/`, экспорты: `Art/CircusNight/` внутри `_Project`.
- Unity: `CircusNightBuilder.cs`, `CircusNightAssets.cs`, `CircusNightProps.cs`,
  `CircusCraftBuilder.cs`, `CircusCraftEffects.cs`, `CircusBearPolishBuilder.cs`.
- Базовый сборщик `CircusArenaBuilder.cs` и отдельные сборщики реквизита должны
  сохранять актуальный арт после обычной пересборки.
- Аудиты: `CircusNightAudit.cs`, `CircusBearAudit.cs`.

Меню, подтверждённые в исходном коде:

- `Igruha/Цирк/Оформить шапито — обе сцены`
- `Igruha/Цирк/Проверить оформление шапито`
- `Igruha/Цирк/Гибкость и попадание Бруно — обе сцены`
- `Igruha/Цирк/Проверить фору и удар Бруно`

Кадры: `docs/art/circus-night/Unity_CageWindow_Stopwatch.png`,
`Unity_Crafted_Cans_Closeup.png`, `Unity_Crafted_Overview.png` и
`Gameplay_Bear_*.png`. Сверяй снимки с последними изменениями сцены.
Проверяй обзор мирового табло из клеток, цвета/символы банок и их состояния,
кнопку секундомера без подсказки скрытого времени, видимость действий медведя.

### «Плачущие ангелы»: завершённый ориентир

Стиль: масштабная тёмная галерея, каменная архитектура, скульптуры и читаемый луч.
Тьма и доступная информация различаются по ролям. Это решение конкретной игры.

- Геометрия: `tools/blender/crying_angels.py`, `crying_angels_ruins.py`,
  `crying_angels_monuments.py`; расположение — `crying_angels_layout.py/.json`.
- Исходники: `Art/Source/CryingAngels/`, экспорты: `Art/CryingAngels/` в `_Project`.
- Unity: `CryingAngelsGalleryBuilder.cs`, `CryingAngelsGalleryAssets.cs`,
  `CryingAngelsGalleryLayout.cs`, `CryingAngelsGalleryEffects.cs`.
- Меню: `Igruha/Minigames/Crying Angels/Apply Moonlit Gallery Art`;
  проверка — существующий метод `CryingAngelsGalleryBuilder.Audit()`.
- Кадры: `docs/art/crying-angels/Unity_Night_*.png`,
  `Unity_Scale_KeeperPOV*.png`, `Unity_BeamCaught_*.png`.

В `STATE.md` 3.89–3.92 есть более поздние изменения масштаба, видимости,
высоты укрытий и луча. Учитывай причины этих изменений при новой переработке;
перезапуск старого генератора не должен случайно их отменить. Намеренные изменения
планировки проверяй по игровой функции и сохраняй в актуальном генераторе.
При сравнении укрытия с персонажем учитывай видимый меш в позе
и физическую капсулу: они могут иметь разную высоту.

## Blender → Unity

Прочитай `tools/BLENDER.md` и проверь доступность инструментов. В проекте есть
локальный Blender MCP bridge на `127.0.0.1:9876` и клиент `tools/blender_client.py`.
Команды из корня репозитория:

```sh
python3 tools/blender_client.py get_scene_info
python3 tools/blender_client.py execute_code --file "tools/blender/<generator>.py"
python3 tools/blender_client.py get_viewport_screenshot --params '{"filepath":"/tmp/mg-polish-preview.png","max_size":1200}'
```

`<generator>` — имя фактически созданного генератора. Клиент сам задаёт `__file__`.
Выполнение идёт в активной сцене Blender. Сначала проверь её и сохрани нужную работу;
не запускай поверх неё генератор с `read_factory_settings(use_empty=True)`.
Для такого генератора используй отдельный фоновый процесс Blender с
`--background --python <путь-к-генератору>` и явными путями вывода.
Найди установленный исполняемый файл, не зашивай чужой путь в скрипты проекта.

На macOS запуск закрытого Blender с мостом описан так:

```sh
open -a Blender --args --python "$PWD/tools/blender_start.py"
```

У уже открытого Blender аргументы `open` могут не примениться. Проверь состояние
моста и используй доступный API, UI или фоновый режим по задаче. Отсутствие отдельного
Blender MCP-инструмента в сессии не означает отсутствие локального Blender.

Экспорт FBX в существующих генераторах использует `axis_forward='-Z', axis_up='Y'`;
проверь фактическое направление, масштаб и положение в Unity до экспорта всего набора.
Для анимированного ассета дополнительно проверь rig, клипы, root motion и skinned bounds.
Сохраняй `.blend` с нужными текстурами и игровой экспорт; материалы собирай и проверяй
в URP, не рассчитывая на автоматический перенос Blender-нод.

Unity управляй доступным Unity MCP или проектным Editor/batchmode-инструментом.
Используй реальные обнаруженные команды, не предполагая, что конкретное имя MCP
доступно. При недоступном редакторе можно подготовить исходники, экспорты и билдер,
но Unity-импорт, компиляцию, игровой вид и Play Mode отмечай непроверенными.

Имеющиеся инструменты звука: `tools/sfx_normalize.py`, `tools/wav_probe.py`,
`tools/sfx_pick.py`. Их интерфейс и наличие аудиогенератора проверь перед использованием;
наличие генератора не предполагает необходимости заменять весь звук игры.
