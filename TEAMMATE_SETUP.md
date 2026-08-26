# Настройка среды разработки — проект «Комната»

Цель: собрать на второй машине среду, идентичную основной, чтобы работать над
игрой параллельно. Доступ к Git-репозиторию и Linear настраивать не нужно — он
выдаётся отдельно.

Инструкция покрывает **macOS и Windows**. Где команды различаются, приведены обе;
где нет — блок один на обе платформы. Windows-колонка написана по факту реальной
установки 26.08 и включает грабли, на которые там наступаешь гарантированно
(раздел «Частые проблемы»).

Стек проекта: Unity 6.3 (6000.3.11f1), URP, C#, онлайн-мультиплеер (NGO + Relay +
Lobby).

---

## Часть 1. Базовые инструменты

Ставь по порядку, уже установленное пропускай.

### 1.1. Пакетный менеджер

**macOS — Homebrew.** Если ещё нет:

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

**Windows — winget.** Уже встроен в Windows 10/11, ставить нечего. При первом
запуске он просит принять соглашения источников, а в неинтерактивной оболочке
падает с `0x8a150042`. Поэтому ниже везде добавлены флаги
`--accept-source-agreements --accept-package-agreements`.

### 1.2. Всё остальное одной таблицей

| Инструмент | macOS | Windows |
|---|---|---|
| Git | `brew install git` | `winget install Git.Git` |
| Git LFS | `brew install git-lfs` | входит в установщик Git |
| Node.js | `brew install node` | `winget install OpenJS.NodeJS.LTS` |
| Python | `brew install python` | `winget install Python.Python.3.13` |
| uv | `brew install uv` | `winget install astral-sh.uv` |
| .NET SDK | `brew install dotnet-sdk` | `winget install Microsoft.DotNet.SDK.9` |

Windows-команды нужно дополнить флагами из 1.1, например:

```powershell
winget install --id OpenJS.NodeJS.LTS --exact --silent `
  --accept-source-agreements --accept-package-agreements
```

После установки Git на обеих платформах:

```bash
git lfs install
```

Зачем что нужно:

- **Git LFS обязателен** — проект хранит модели, текстуры и аудио через LFS. Без
  него при клоне вместо файлов подтянутся текстовые указатели на ~130 байт.
- **Node.js** — для Claude Code.
- **uv** — запускает MCP-сервер Blender.
- **.NET SDK** — им проверяется компиляция C#, когда Unity закрыт
  (`dotnet build`). В инструкции его раньше не было, но в работе он используется —
  см. `STATE.md`, раздел 3.7.

Проверка, что всё встало (пути в новой оболочке, старая PATH не подхватит):

```bash
git --version && git lfs version && node --version && python --version && uv --version && dotnet --version
```

---

## Часть 2. Unity

### 2.1. Unity Hub

**macOS:** скачать с https://unity.com/download и установить.

**Windows:**

```powershell
winget install --id Unity.UnityHub --exact --silent `
  --accept-source-agreements --accept-package-agreements
```

### 2.2. Unity Editor 6000.3.11f1 — ТОЧНО эта версия

Версия и ченджсет зафиксированы в `igruha/ProjectSettings/ProjectVersion.txt`:
`6000.3.11f1`, ченджсет `3000ef702840`. Другая версия даст рассинхрон проекта.

**Через GUI:** Unity Hub → Installs → Install Editor → `6000.3.11f1`.

**Через CLI** (быстрее и не требует кликов):

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

**Модули нужны кросс-платформенные на обеих машинах** — раздатка для плейтестов
собирается сразу под Mac и Windows (`igruha/Builds/sleepover-Mac.zip` и
`sleepover-Windows.zip`). Поэтому маковая машина берёт Windows-модули, а
Windows-машина — Mac-модуль. Свою родную платформу редактор умеет из коробки.

Займёт ~11 ГБ и 20–40 минут. Проверка:

```
Unity Hub -- --headless editors --installed
```

### 2.3. Клонирование проекта

```bash
git clone <URL-репозитория>
cd sleepover
git lfs pull
```

Важно: проект Unity лежит во вложенной папке — открывать в Unity Hub нужно
`sleepover/igruha` (там, где `Assets`), а не корень репозитория.

Проверка, что LFS отработал: любой файл из `igruha/Assets/_Project/Art/Textures/`
должен весить сотни килобайт, а не ~130 байт.

### 2.4. Первый запуск

- Войти в аккаунт Unity в Hub — **без лицензии редактор не стартует.**
- Hub → Add → Add project from disk → выбрать `sleepover/igruha`.
- Дождаться импорта. Первый раз он долгий: ассетов на несколько гигабайт плюс
  Package Manager тянет `com.coplaydev.unity-mcp` с GitHub (нужен git в PATH).
- Проверить **Console (Window → General → Console)** — должно быть 0 ошибок
  компиляции.
- При диалоге про Addressables/Legacy Bundles — Ignore.

---

## Часть 3. Claude Code

### 3.1. Установка

```bash
npm install -g @anthropic-ai/claude-code
claude doctor
```

### 3.2. Первый запуск и вход

```bash
cd путь/к/sleepover
claude
```

Войти в аккаунт Anthropic.

### 3.3. Прочитать CLAUDE.md

В корне репозитория `CLAUDE.md` — навигация и процесс, в `igruha/CLAUDE.md` —
правила кода (SOLID, `[SerializeField]`, server-authority NGO) и раздел
🔒 «Заморожено». Claude Code читает их сам, но прочти глазами: там сетевые
конвенции и список того, что править запрещено.

---

## Часть 4. MCP-серверы

Ставим **в user scope** (`--scope user`) — тогда серверы работают из любой папки,
а не только из той, где выполнялась команда. Это отличается от старой редакции
инструкции и снимает добрую половину вопросов «`/mcp` ничего не видит».

### 4.1. Unity MCP (CoplayDev)

Пакет уже прописан в `igruha/Packages/manifest.json` и приедет с git. Подключаем
клиента:

```bash
claude mcp add --scope user --transport http unity http://127.0.0.1:8080/mcp
```

Сервер поднимается со стороны Unity: **Window → MCP for Unity → Start Server**,
статус должен стать зелёным `Session Active`. Пока редактор не запущен, сервер
будет числиться отключённым — это нормально.

Имя сервера должно быть ровно `unity`: в `.claude/settings.json` проекта
разрешения выданы на префикс `mcp__unity__`.

### 4.2. Blender MCP

Нужен Blender **4.x, не 5.x** — аддон заявляет поддержку до четвёртой ветки.

macOS: скачать 4.5 LTS с https://www.blender.org/download/

Windows:

```powershell
winget install --id BlenderFoundation.Blender --exact --version 4.5.5 --silent `
  --accept-source-agreements --accept-package-agreements
```

Дальше подключаем сервер и ставим аддон **его же командой** — так версия
протокола аддона гарантированно совпадёт с версией сервера:

```bash
claude mcp add --scope user blender -- uvx blender-mcp
uvx blender-mcp install-addon
```

Не качать `addon.py` с GitHub вручную: ветка `main` там может уйти вперёд
опубликованного пакета, а две копии аддона в папке подерутся за порт 9876.

В Blender: Edit → Preferences → Add-ons → включить «Interface: Blender MCP».
Затем в 3D-вьюпорте клавиша **N** → вкладка **BlenderMCP** → Start MCP Server.

**Сервер приходится включать при каждом запуске Blender.** Чтобы не жать вручную,
можно положить автостарт в `scripts/startup/` пользовательского конфига Blender
(`%APPDATA%\Blender Foundation\Blender\4.5\scripts\startup\` на Windows,
`~/Library/Application Support/Blender/4.5/scripts/startup/` на macOS):

```python
"""Автозапуск сокет-сервера BlenderMCP при старте Blender."""

import bpy

_START_DELAY_SECONDS = 1.0


def _start_server():
    try:
        bpy.ops.blendermcp.start_server()
    except Exception as error:  # noqa: BLE001 - стартап не должен ронять Blender
        print(f"[blendermcp] автозапуск не удался: {error}")
    return None


def register():
    bpy.app.timers.register(_start_server, first_interval=_START_DELAY_SECONDS)


def unregister():
    if bpy.app.timers.is_registered(_start_server):
        bpy.app.timers.unregister(_start_server)
```

Файл достаточно удалить, чтобы вернуть ручной режим.

### 4.3. Higgsfield MCP

```bash
claude mcp add --scope user --transport http higgsfield https://mcp.higgsfield.ai/mcp
```

Затем `claude` → `/mcp` → авторизация через браузер (регистрация бесплатная).

### 4.4. Linear MCP

```bash
claude mcp add --scope user --transport http linear https://mcp.linear.app/mcp
```

Затем `claude` → `/mcp` → вход в Linear. Использовать именно `/mcp`-эндпоинт:
старый `/sse` устарел и даёт ошибку авторизации.

### 4.5. Проверка всех разом

```bash
claude mcp list
```

Ожидаемо: `blender` — Connected; `unity` — Connected, если редактор запущен и
сервер стартован; `higgsfield` и `linear` — Connected после авторизации.

---

## Часть 5. Skills (плагин для Claude Code)

Сначала добавить маркетплейс, потом плагин — одной командой `plugin install` он
не найдётся:

```bash
claude plugin marketplace add mattpocock/skills
claude plugin install mattpocock-skills@mattpocock
```

Проверка: в сессии набрать `/mattpocock-skills` — появится список команд
(`code-review`, `grill-me`, `to-spec`, `improve-codebase-architecture` и др.).

---

## Часть 6. Опционально — сетевая зона

- **Netcode for GameObjects (NGO)** — основной сетевой фреймворк, уже в проекте.
- **Unity Relay + Lobby** — через Unity Gaming Services, нужен вход в аккаунт
  Unity и привязка проекта к UGS.

---

## Итоговый чеклист

- [ ] Пакетный менеджер (Homebrew / winget)
- [ ] Git + Git LFS (`git lfs install`)
- [ ] Node.js
- [ ] Python + uv
- [ ] .NET SDK
- [ ] Unity Hub
- [ ] Unity Editor 6000.3.11f1 + кросс-платформенные модули сборки
- [ ] Клонирован репозиторий + `git lfs pull`, файлы не указатели
- [ ] Вход в аккаунт Unity (лицензия)
- [ ] Проект `igruha` открывается, Console без ошибок
- [ ] Claude Code установлен (`claude doctor` ок), вход в Anthropic
- [ ] Прочитан `CLAUDE.md` (корневой и `igruha/`)
- [ ] Unity MCP подключён (Connected при запущенном редакторе)
- [ ] Blender 4.x + аддон через `uvx blender-mcp install-addon`
- [ ] Higgsfield MCP авторизован
- [ ] Linear MCP авторизован
- [ ] `claude plugin install mattpocock-skills@mattpocock`
- [ ] `claude mcp list` показывает все четыре сервера

---

## Частые проблемы

### Общие

- **`/mcp` пишет «No MCP servers configured»** — серверы поставлены в local scope
  вместо user. `claude mcp list` покажет реальное состояние. Лечится повторной
  установкой с `--scope user`.
- **Unity MCP не Connected** — не нажат Start Server в окне Unity, либо редактор
  не запущен. Без запущенного редактора подключения не будет по определению.
- **Blender MCP отвалился** — сервер останавливается вместе с Blender; при новом
  запуске нажать Start MCP Server заново или положить автостарт из 4.2.
- **Git подтянул файлы на ~130 байт вместо моделей** — забыт `git lfs install`
  до клона или `git lfs pull` после.
- **Розовые материалы в сцене** — ассет не под URP; конвертировать или заменить.

### Только Windows

- **Unity ругается «Unity is running as administrator».** Проверить UAC:

  ```powershell
  (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System').EnableLUA
  ```

  Если `0` — UAC отключён, и тогда **любой** процесс у члена группы
  «Администраторы» получает полный админский токен. Галочка «Запускать от имени
  администратора» в свойствах ярлыка при этом не стоит, и кнопка Unity «Restart
  as a standard user» не помогает: понижать права не во что. Лечится возвратом
  `EnableLUA` в `1` и перезагрузкой.

  Почему это важно: Unity исполняет скрипты из проекта и стор-ассетов, и под
  админом любой из них получает полный доступ к системе.

- **Unity Hub CLI падает с `Cannot find module '--headless'`.** Оболочка
  унаследовала переменную `ELECTRON_RUN_AS_NODE=1` (её выставляют VS Code и
  Cursor), и Hub запускается как голый Node вместо Electron. Запускать Hub из
  обычного терминала либо снять переменную:

  ```powershell
  Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue
  ```

- **winget падает с `0x8a150042`** — не приняты соглашения источников; добавить
  `--accept-source-agreements --accept-package-agreements`.

- **Только что установленную программу не видно в терминале** — PATH читается при
  старте оболочки. Открыть новое окно терминала.

- **PowerShell-скрипт с кириллицей выдаёт кракозябры или ParserError** — файл
  сохранён в UTF-8 без BOM, а PowerShell 5.1 читает его в системной кодировке.
  Сохранять с BOM либо писать вывод скриптов латиницей.
