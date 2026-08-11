# Настройка среды разработки — проект «Комната» (для напарника, macOS)

Цель: собрать среду, идентичную основной, чтобы работать над игрой параллельно. У тебя уже есть доступ к Git-репозиторию и Linear — их настраивать не нужно. Ниже — всё остальное по шагам.

Стек проекта: Unity 6.3 (6000.3.11f1), URP, C#, онлайн-мультиплеер (NGO + Relay + Lobby). Твоя основная зона — сеть/бэкенд/девопс.

---

## Часть 1. Базовые инструменты

Ставь по порядку. Если что-то уже стоит — пропускай.

### 1.1. Homebrew (пакетный менеджер macOS)
Если ещё нет — установи, через него удобно ставить остальное:
```
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

### 1.2. Git + Git LFS
```
brew install git git-lfs
git lfs install
```
Git LFS обязателен — проект хранит бинарные ассеты (модели, текстуры, аудио) через LFS. Без него при клоне вместо файлов подтянутся пустые указатели.

### 1.3. Node.js (нужен для Claude Code)
```
brew install node
```

### 1.4. Python + uv
```
brew install python
brew install uv
```
`uv` нужен для запуска некоторых MCP-серверов (Blender).

---

## Часть 2. Unity

### 2.1. Unity Hub
Скачай с https://unity.com/download → установи Unity Hub.

### 2.2. Unity Editor 6.3 (ТОЧНО эта версия)
В Unity Hub → Installs → Install Editor → выбери **6000.3.11f1** (именно её, чтобы не было рассинхрона проекта).
При установке отметь модули:
- **Mac Build Support** (по умолчанию)
- **Windows Build Support (Mono/IL2CPP)** — билд под основную платформу (Steam/PC)
- Documentation (опционально)

### 2.3. Клонирование проекта
```
git clone <URL-репозитория>
cd sleepover
git lfs pull
```
Важно: проект Unity лежит во вложенной папке — открывай в Unity Hub именно папку `sleepover/igruha` (там, где `Assets`), а не корень репозитория.

### 2.4. Первый запуск
- Открой `igruha` через Unity Hub → дождись импорта пакетов (Addressables, Input System и др. подтянутся сами из manifest).
- Проверь **Console (Window → General → Console)** — должно быть 0 ошибок компиляции.
- При диалоге про Addressables/Legacy Bundles — нажимай Ignore.

---

## Часть 3. Claude Code

### 3.1. Установка
```
npm install -g @anthropic-ai/claude-code
```
Проверь:
```
claude doctor
```

### 3.2. Первый запуск и вход
```
cd путь/к/sleepover/igruha
claude
```
Войди в свой аккаунт Anthropic (Pro-подписка нужна).

### 3.3. Прочитать CLAUDE.md
В корне проекта лежит `CLAUDE.md` — правила кода (SOLID, SerializeField, NGO server-authority и т.д.). Claude Code читает его автоматически. Прочти сам глазами — там сетевые конвенции, которые тебе важны.

---

## Часть 4. MCP-серверы

Все команды выполняются в терминале. После добавления — запусти `claude`, потом `/mcp` внутри сессии для проверки/авторизации.

### 4.1. Unity MCP (CoplayDev)
Пакет уже установлен в проекте (приедет с git). Нужно только подключить клиента:

Сначала в Unity: **Window → MCP for Unity → Start Server** (запускает сервер на 127.0.0.1:8080, статус должен стать зелёным "Session Active").

Потом в терминале (в папке `igruha`):
```
claude mcp add --transport http unity http://127.0.0.1:8080/mcp
```
Проверка: `claude mcp list` → unity должен быть Connected.

Примечание: мастер "Configure All Detected Clients" в окне Unity может записать конфиг не в ту папку при вложенной структуре — надёжнее ручная команда выше.

### 4.2. Blender MCP
Нужен установленный Blender (4.x) — скачай с https://www.blender.org/download/

1. В Blender: Edit → Preferences → Add-ons → стрелка ⌄ вверху справа → Install from Disk → выбери `addon.py` (скачать с https://github.com/ahujasid/blender-mcp).
2. Включи галочку аддона "Blender MCP".
3. В 3D-вьюпорте: клавиша **N** → вкладка **BlenderMCP** → Connect to MCP server (порт 9876).
4. В терминале:
```
claude mcp add blender -- uvx blender-mcp
```
Проверка: `claude mcp list` → blender Connected.

Важно: сервер в Blender нужно запускать заново при каждом старте Blender (Connect в панели).

### 4.3. Higgsfield MCP
```
claude mcp add --transport http --scope user higgsfield https://mcp.higgsfield.ai/mcp
```
Затем `claude` → `/mcp` → авторизуйся через браузер (аккаунт Higgsfield, регистрация бесплатная).

### 4.4. Linear MCP
```
claude mcp add --transport http --scope user linear https://mcp.linear.app/mcp
```
Затем `claude` → `/mcp` → авторизуйся через браузер (вход в Linear).
Важно: используй именно `/mcp` HTTP-эндпоинт. Старый `/sse` устарел и даёт ошибку авторизации.

Проверка всех разом:
```
claude mcp list
```
Должны быть Connected: unity, blender, higgsfield, linear.

---

## Часть 5. Skills (плагин для Claude Code)

```
claude plugins install mattpocock-skills
```
Даёт команды в сессии Claude Code: `/mattpocock-skills:code-review`, `grill-me`, `to-spec`, `improve-codebase-architecture` и др. Проверка: в сессии набери `/mattpocock-skills` — увидишь список.

---

## Часть 6. Опционально — под твою зону (сеть)

Эти пакеты будут ставиться в рамках EPIC 2 (сетевой каркас) — можешь заранее ознакомиться:
- **Netcode for GameObjects (NGO)** — основной сетевой фреймворк.
- **Unity Relay + Lobby** — через Unity Gaming Services (UGS), нужен вход в аккаунт Unity и привязка проекта к UGS.
- Стек выбран как дефолт; если предложишь замену (Photon Fusion / Mirror) с обоснованием — обсудим до начала EPIC 2.

---

## Итоговый чеклист (отметь по мере готовности)

- [ ] Homebrew
- [ ] Git + Git LFS (`git lfs install`)
- [ ] Node.js
- [ ] Python + uv
- [ ] Unity Hub
- [ ] Unity Editor 6000.3.11f1 + Windows Build Support
- [ ] Клонирован репозиторий + `git lfs pull`
- [ ] Проект `igruha` открывается, Console без ошибок
- [ ] Claude Code установлен (`claude doctor` ок)
- [ ] Вошёл в аккаунт Anthropic
- [ ] Прочитал CLAUDE.md
- [ ] Unity MCP подключён (Connected)
- [ ] Blender + Blender MCP подключён
- [ ] Higgsfield MCP подключён (авторизован)
- [ ] Linear MCP подключён (авторизован)
- [ ] mattpocock-skills установлен
- [ ] `claude mcp list` показывает все 4 сервера Connected

---

## Частые проблемы

- **`/mcp` пишет "No MCP servers configured"** — сессию запускай в папке `igruha`, а не в другой директории. Если не помогло — `claude mcp list` покажет реальное состояние; сервер мог записаться в конфиг другой папки.
- **Unity MCP не Connected** — сначала нажми Start Server в окне Unity (без запущенного сервера подключения не будет).
- **Blender MCP отвалился** — сервер в Blender останавливается при закрытии; при новом запуске Blender нажми Connect заново.
- **Git подтянул пустые файлы вместо моделей** — забыл `git lfs pull` или `git lfs install` до клона.
- **Розовые материалы в сцене** — ассет не под URP; конвертировать или заменить.
