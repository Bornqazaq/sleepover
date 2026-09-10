# Настройка среды разработки — проект «Комната» (`sleepover`)

Цель: за один вечер собрать на новой машине среду, идентичную основной, и
работать над игрой параллельно через Claude Code. Инструкция написана так,
чтобы её мог выполнить **Claude Code целиком сам**: команды точные, проверки
после каждого шага, известные грабли собраны в конце.

Обновлено **10.09.2026** по факту двух живых установок: macOS (Apple Silicon,
основная машина) и Windows (26.08). Что не проверено руками — помечено.

Доступ к Git-репозиторию и Linear выдаётся отдельно, здесь не настраивается.

---

## 0. Если читает Claude Code

1. Сначала прочитай `CLAUDE.md` (корень) и `igruha/CLAUDE.md` — процесс и
   правила кода, включая раздел 🔒 «Заморожено». Потом `STATE.md` — единый
   статус проекта, сверху самое свежее.
2. Выполняй разделы по порядку, после каждого — блок «Проверка». Не переходи
   дальше с красной проверкой.
3. Всё, что зависит от машины (пути, версии, ключи), кладётся **в конфиги
   пользователя**, а не в репозиторий: `~/.claude.json`, `.claude/settings.local.json`
   (он в `.gitignore`). В репозитории лежат только общие `.claude/settings.json`
   и скиллы `.claude/skills/`.
4. Не трогай `main` напрямую и не делай `git pull` при открытом Unity —
   раздел 3.3.

Стек: Unity **6000.3.11f1**, URP 17.3, Netcode for GameObjects 1.10 + Relay +
Lobby, Cinemachine 3.1.7, Multiplayer Play Mode 2.0.2, MCP for Unity
(`com.coplaydev.unity-mcp`, ветка `main` с GitHub).

---

## 1. Базовые инструменты

### 1.1. Пакетный менеджер

**macOS — Homebrew:**

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

**Windows — winget** встроен. В неинтерактивной оболочке при первом запуске
падает с `0x8a150042`, поэтому ко всем командам ниже добавлять
`--accept-source-agreements --accept-package-agreements`.

### 1.2. Инструменты

| Инструмент | macOS | Windows | Зачем |
|---|---|---|---|
| Git | `brew install git` | `winget install Git.Git` | репозиторий |
| Git LFS | `brew install git-lfs` | в установщике Git | модели, текстуры, звук — **обязателен** |
| Node.js LTS | `brew install node` | `winget install OpenJS.NodeJS.LTS` | Claude Code |
| Python 3 | `brew install python` | `winget install Python.Python.3.13` | инструменты в `tools/` |
| uv | `brew install uv` | `winget install astral-sh.uv` | `uvx` для MCP-серверов Blender и Unity |

.NET SDK **не нужен**: компиляция без редактора делается самим Unity в
batchmode (раздел 5). На основной машине dotnet не стоит.

После установки Git:

```bash
git lfs install
```

Проверка (в новом окне терминала — PATH читается при старте оболочки):

```bash
git --version && git lfs version && node --version && python3 --version && uv --version && which uvx
```

Запомни абсолютный путь `uvx` (macOS: `~/.local/bin/uvx` или
`/opt/homebrew/bin/uvx`) — он понадобится, если поднимать Unity MCP по stdio.

---

## 2. Unity

### 2.1. Unity Hub

macOS: https://unity.com/download. Windows:

```powershell
winget install --id Unity.UnityHub --exact --silent --accept-source-agreements --accept-package-agreements
```

### 2.2. Unity Editor 6000.3.11f1 — ТОЧНО эта версия

Версия и ченджсет — в `igruha/ProjectSettings/ProjectVersion.txt`:
`6000.3.11f1`, ченджсет `3000ef702840`. Другая версия пересоберёт Library и
разъедется с напарником.

GUI: Unity Hub → Installs → Install Editor → 6000.3.11f1. CLI:

macOS:

```bash
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install \
  --version 6000.3.11f1 --changeset 3000ef702840 \
  --module windows-mono windows-il2cpp --childModules
```

Windows:

```powershell
& 'C:\Program Files\Unity Hub\Unity Hub.exe' -- --headless install `
  --version 6000.3.11f1 --changeset 3000ef702840 `
  --module windows-il2cpp mac-mono --childModules
```

Модули — **кросс-платформенные**: раздатка для плейтестов собирается сразу
под Mac и Windows, поэтому маковая машина ставит Windows-модули, а
Windows-машина — Mac-модуль. ~11 ГБ, 20–40 минут.

Где лежит редактор (нужно для раздела 5):

- macOS: `~/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity`
  (на основной машине именно так, не в `/Applications`);
- Windows: `C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe`.

Проверка: `Unity Hub -- --headless editors --installed` показывает 6000.3.11f1.

### 2.3. Клонирование

```bash
git clone <URL-репозитория> sleepover
cd sleepover
git lfs pull
```

LFS-объектов больше 5 ГБ — первый `pull` долгий, это норма. Проверка: любой
файл в `igruha/Assets/_Project/Art/` весит сотни килобайт, а не ~130 байт
(указатель).

**Проект Unity — вложенная папка `sleepover/igruha`** (где `Assets`). В Hub
добавлять именно её, не корень репозитория.

### 2.4. Первый запуск

1. Войти в аккаунт Unity в Hub — без лицензии редактор не стартует.
2. Hub → Add → Add project from disk → `sleepover/igruha`.
3. Первый импорт долгий: несколько ГБ ассетов, Package Manager тянет
   `com.coplaydev.unity-mcp` с GitHub (нужен `git` в PATH).
4. Console (Window → General → Console): **0 ошибок компиляции**.
5. Диалог про Addressables / Legacy Bundles — Ignore.

---

## 3. Git-процесс — коротко, но обязательно

Полная формулировка — `CLAUDE.md`, раздел 5. Здесь то, без чего сломаешь
работу второму.

### 3.1. Ветки

- **В `main` напрямую не коммитить никогда**, даже однострочный фикс.
- Работа — в ветке на фичу/фазу. Закрыл, протестировал → мердж в `main`.
- `main` подтягивать в свою ветку часто, а не раз в неделю.
- Правишь `Core/`, общие префабы, аниматоры, ввод — **предупреди второго до
  мерджа**, в топике `warnings` (3.4).
- Коммит на русском, с ID тикета: `IGR-151: сетевой синхрон выстрелов Duck Hunt`.

### 3.2. Сцены и префабы

Одна сцена = один владелец в моменте. `.unity` и `.prefab` не мержатся
по-человечески. Кто делает свою мини-игру, чужую сцену не открывает на запись.

### 3.3. Перед `git pull` / `merge` — закрыть Unity

Иначе редактор и git пишут в одни файлы: Unity пересоздаёт `.meta` с новыми
GUID, ссылки в сценах и префабах рвутся. Проверено на практике 15.08.

### 3.4. Топик `warnings` в общей ТГ-группе

Всё, что второй должен узнать **раньше, чем сделает pull**, идёт туда:
затронутый Core, переделанная общая сцена, изменённые правила. Запись в
`STATE.md` приезжает тем же мерджем, о котором предупреждает, поэтому файлом
предупредить нельзя by design.

### 3.5. Git LFS — новые ассеты

Новые анимации класть **без модели внутри** (клип ~1 МБ вместо ~85 МБ).
Что идёт через LFS — в `.gitattributes`.

---

## 4. Claude Code и MCP

### 4.1. Claude Code

```bash
npm install -g @anthropic-ai/claude-code
claude doctor
cd sleepover && claude
```

Войти в аккаунт Anthropic. На основной машине — Claude Code 2.1.x.

### 4.2. Разрешения

`.claude/settings.json` в репозитории уже выдаёт разрешения на
`mcp__unity__*`, `mcp__UnityMCP__*`, `mcp__linear__*` и безопасные git-команды.
Имена MCP-серверов ниже должны совпадать **буква в букву**: `unity`,
`UnityMCP`, `blender`, `linear`, `higgsfield`.

Свои локальные разрешения и пути — в `.claude/settings.local.json` (не
коммитится).

### 4.3. Unity MCP — рабочая схема: HTTP

Мост со стороны Unity уже в проекте (`Packages/manifest.json` →
`com.coplaydev.unity-mcp`, версия пакета 10.x). В версии 10 мост **сам
поднимает HTTP-сервер**, отдельный Python-процесс не нужен.

В Unity: **Window → MCP for Unity** → транспорт **HTTP**, порт **8080** →
Start Server. Статус — зелёный `Session Active`.

Клиент (два имени на один адрес — под оба префикса разрешений):

```bash
claude mcp add --scope user --transport http unity    http://127.0.0.1:8080/mcp
claude mcp add --scope user --transport http UnityMCP http://127.0.0.1:8080/mcp
```

Проверка: новая сессия `claude` → `/mcp` → `unity` Connected при открытом
редакторе с запущенным сервером. Без редактора — Disconnected, это норма.

**Запасная схема — stdio через uvx** (если HTTP не поднялся). Версию пина
взять из `igruha/Library/PackageCache/com.coplaydev.unity-mcp*/package.json`,
путь `uvx` — абсолютный:

```bash
claude mcp add --scope user UnityMCP -- /Users/<user>/.local/bin/uvx --from mcpforunityserver==10.1.0 mcp-for-unity
```

Проверка сервера отдельно: `uvx --from mcpforunityserver==10.1.0 mcp-for-unity --help`
печатает справку. Обновился пакет в манифесте — перечитать версию, иначе мост
и сервер разъедутся.

Правила работы через MCP (из `CLAUDE.md`):

- после любых правок C# — `refresh_unity`, затем `read_console` (errors);
- **не делать `refresh_unity`, пока пользователь в плей-моде** — перекомпиляция
  перезагрузит домен и убьёт его прогон; сначала `manage_editor` → проверить
  `isPlaying`;
- сцены, префабы, ScriptableObject'ы правит агент через MCP, пользователю
  остаётся плейтест.

### 4.4. Blender MCP

**Blender 5.2 LTS работает** (проверено на основной машине с локальным
аддоном, `tools/BLENDER.md`). Windows-установка 26.08 делалась на 4.5 — тоже
работает. Брать актуальный LTS с https://www.blender.org/download/ или:

```powershell
winget install --id BlenderFoundation.Blender --exact --silent --accept-source-agreements --accept-package-agreements
```

Сервер и аддон — одной парой команд, чтобы версии протокола совпали:

```bash
claude mcp add --scope user blender -- uvx blender-mcp
uvx blender-mcp install-addon
```

Не качать `addon.py` с GitHub руками: две копии аддона дерутся за порт 9876.

В Blender: Edit → Preferences → Add-ons → включить «Blender MCP». Сервер
надо стартовать при каждом запуске Blender: либо **N** → вкладка
**BlenderMCP** → Start MCP Server, либо запускать Blender скриптом из
репозитория, который включает аддон и поднимает сервер сам:

```bash
open -a Blender --args --python "$PWD/tools/blender_start.py"
```

(Windows: `& 'C:\Program Files\Blender Foundation\Blender 4.5\blender.exe' --python tools\blender_start.py`.)

Без MCP-клиента Blender тоже управляем: `python3 tools/blender_client.py
get_scene_info`, `execute_code --file <скрипт>`, `get_viewport_screenshot`.
Генераторы арта лежат в `tools/blender/` (пример — арена «Плачущих ангелов»).

### 4.5. Linear MCP

```bash
claude mcp add --scope user --transport http linear https://mcp.linear.app/mcp
```

Именно `/mcp`: старый `/sse` отдаёт 404. На голый запрос сервер отвечает 401 —
это приглашение к OAuth, не ошибка. В `claude` → `/mcp` → войти в Linear через
браузер.

Команда **IGRUHA**, проект **«Комната (Party-game)»**, эпики EPIC 0–24.
Статусы двигает агент сам, по факту работы: взял — `In Progress`, закрыл —
`Done`. Локальных копий тикетов не заводим. Если Linear MCP не поднялся —
сказать вслух, а не пропустить молча.

### 4.6. Higgsfield MCP (промо-графика, опционально)

```bash
claude mcp add --scope user --transport http higgsfield https://mcp.higgsfield.ai/mcp
```

`/mcp` → авторизация в браузере. Без авторизации сервер числится
«requires authentication» — на работу с Unity не влияет.

### 4.7. Проверка всех разом

```bash
claude mcp list
```

Ожидаемо: `blender` Connected (при запущенном Blender с сервером), `unity` и
`UnityMCP` Connected при запущенном редакторе, `linear` и `higgsfield` —
после авторизации. **Новые серверы видны только со следующего запуска
сессии** `claude`.

---

## 5. Компиляция без редактора — Unity batchmode

Когда редактор закрыт (или MCP отвалился), проверять C# так. Нужно, чтобы
Unity не была открыта на этом проекте.

macOS:

```bash
"$HOME/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -quit -projectPath "$PWD/igruha" -logFile /tmp/unity_compile.log
grep -c "error CS" /tmp/unity_compile.log   # 0 = чисто
grep "error CS" /tmp/unity_compile.log | head
```

Windows (PowerShell):

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe' `
  -batchmode -nographics -quit -projectPath "$PWD\igruha" -logFile "$env:TEMP\unity_compile.log"
Select-String -Path "$env:TEMP\unity_compile.log" -Pattern 'error CS'
```

Первый прогон на свежей машине долгий (импорт), дальше — 1–2 минуты. Код
возврата Unity может быть ненулевым и при чистой компиляции — смотреть на
`error CS` в логе, а не на код. Ошибки — в `Editor.log`, а не в консоли.

---

## 6. Сетевые прогоны

### 6.1. В редакторе — Multiplayer Play Mode

Пакет `com.unity.multiplayer.playmode` 2.0.2 в проекте. Сценарий
`igruha/Assets/Settings/PlayMode/Host + Client.asset`: главный редактор —
хост, виртуальные игроки Player 2 и Player 3 — клиенты. Роль решает
`NetworkRoleResolver`: главный редактор → Host, виртуальный игрок без тега →
Client; тегами `Host`/`Client` в сценарии можно переопределить.

Window → Multiplayer → Play Mode Scenarios → активировать «Host + Client» →
Play. Виртуальные игроки стартуют 20–40 с, их логи — в
`igruha/Library/VP/<id>/Logs/Editor.log`, у Player 2 включён стрим в консоль
главного редактора. Управлять виртуальным игроком можно, кликнув в его окно.

Редактор восьмерых не тянет (2 кадра в минуту) — для 5+ игроков билды.

### 6.2. Билды и автопрогон

Тестовый билд — `igruha/Builds/Autotest/`, отдельно от раздаточного.
Аргументы запуска (`Core/Session/LaunchArguments.cs`): `--autostart <Игра>`,
`--wait-players N`, `--bot` (персонажем управляет болванка). Скрипты стенда —
`tools/autorun-*.sh` и `tools/autorun-report.sh` (сличение логов). Подробности
и грабли — `STATE.md`, поиск по слову «стенд».

Раздатка для плейтестов: `sleepover-Mac.zip` и `sleepover-Windows.zip`, сборка
через `tools/dist-mac` / `tools/dist-windows`. Старые сборки после пересборки
удалять, чтобы не раздать по ошибке.

---

## 7. Скиллы

### 7.1. Скиллы репозитория (приезжают с git)

`.claude/skills/` — конвейер мини-игры, пять фаз строго по порядку:
`/mg-spec` → `/mg-tickets` → `/mg-blockout` → `/mg-net` → `/mg-art`, плюс
`/mg-check` перед закрытием тикета. Какая модель и уровень рассуждения под
какую фазу — таблица в `CLAUDE.md`, раздел 3.

### 7.2. Плагин mattpocock/skills

```bash
claude plugin marketplace add mattpocock/skills
claude plugin install mattpocock-skills@mattpocock
```

Проверка: `/mattpocock-skills` показывает список (`code-review`, `grilling`,
`tdd`, `diagnosing-bugs` и др.).

---

## Итоговый чеклист

- [ ] Homebrew / winget
- [ ] Git + Git LFS (`git lfs install`), Node.js, Python 3, uv (`uvx` найден)
- [ ] Unity Hub, вход в аккаунт (лицензия)
- [ ] Unity Editor 6000.3.11f1 + кросс-платформенные модули
- [ ] Репозиторий склонирован, `git lfs pull`, файлы не указатели
- [ ] Проект `igruha` открыт, Console без ошибок
- [ ] Прочитаны `CLAUDE.md` (оба), `STATE.md` сверху
- [ ] Claude Code (`claude doctor` ок), вход в Anthropic
- [ ] Unity MCP: Window → MCP for Unity → HTTP 8080 → Start; `unity` и `UnityMCP` Connected
- [ ] Blender + аддон через `uvx blender-mcp install-addon`, `blender` Connected
- [ ] Linear MCP авторизован (`/mcp`), тикеты видны
- [ ] Higgsfield MCP (опционально)
- [ ] `claude mcp list` — все серверы на месте
- [ ] Плагин mattpocock-skills установлен, `/mg-spec` и остальные скиллы видны
- [ ] Batchmode-компиляция из раздела 5 отработала, `error CS` = 0
- [ ] Прочитан раздел 3: ветки, «закрыть Unity перед pull», топик `warnings`

---

## Частые проблемы

### Общие

- **`/mcp` пишет «No MCP servers configured»** — серверы добавлены не в тот
  scope или в другой папке. `claude mcp list` покажет правду; переставить с
  `--scope user`. Новые серверы видны только в новой сессии.
- **`unity` не Connected** — не нажат Start Server в окне MCP for Unity, не
  тот транспорт/порт (нужен HTTP 8080), либо редактор закрыт.
- **`refresh_unity` отвечает timeout 60 s** — редактор занят (импорт,
  компиляция большого проекта). Подождать и проверить `read_console`; сам
  refresh обычно уже прошёл.
- **После `refresh_unity` Unity открыла чужую сцену** — билдеры арен сносят
  `_Arena` активной сцены; перед сборкой арены сверять активную сцену.
- **Blender MCP отвалился** — сервер живёт, пока живёт Blender; перезапустить
  Start MCP Server или стартовать Blender через `tools/blender_start.py`.
- **Git подтянул файлы на ~130 байт** — забыт `git lfs install` до клона или
  `git lfs pull` после.
- **Розовые материалы** — ассет не под URP; конвертировать или заменить.
- **Unity не открывает проект: «already opened»** при закрытом редакторе —
  остался `igruha/Temp/UnityLockfile` после аварийного выхода; удалить.
- **В `main` после мерджа появились «изменения» в `.meta`/сценах, которых не
  делал** — pull делался при открытом Unity (раздел 3.3).

### Только Windows

- **«Unity is running as administrator»** — отключён UAC:

  ```powershell
  (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System').EnableLUA
  ```

  Если `0`, любой процесс админа получает полный токен, и кнопка «Restart as a
  standard user» не помогает. Вернуть `EnableLUA` в `1` и перезагрузиться.
  Unity исполняет скрипты стор-ассетов — под админом это полный доступ к системе.

- **Unity Hub CLI: `Cannot find module '--headless'`** — оболочка унаследовала
  `ELECTRON_RUN_AS_NODE=1` от VS Code / Cursor. Снять:

  ```powershell
  Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue
  ```

- **winget `0x8a150042`** — добавить `--accept-source-agreements --accept-package-agreements`.
- **Свежую программу не видно в терминале** — открыть новое окно.
- **PowerShell-скрипт с кириллицей даёт кракозябры / ParserError** — файл в
  UTF-8 без BOM, PowerShell 5.1 читает системную кодировку. Сохранять с BOM или
  писать вывод латиницей.
- **Пути в `igruha/.claude/settings.local.json` вида `C:/Users/...`** — это
  локальный файл конкретной машины, он в `.gitignore`; на другой машине свой.
