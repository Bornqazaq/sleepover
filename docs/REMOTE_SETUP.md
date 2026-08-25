# Удалённый режим — настройка на новой машине

Инструкция для Claude Code, поднимающего проект на втором ПК. Цель: разработка
с телефона, без ситуаций «нужна рука за компом».

Проверено 25.08.2026 на Windows 11 Pro 26200, Claude Code 2.1.239, Unity 6.3
(6000.3.11f1). Всё, что здесь написано, отработано вживую, а не по документации.

---

## 0. Что должно получиться

1. На ПК висит якорная сессия Claude Code с Remote Control в режиме **spawn**.
2. С телефона видно эту сессию и можно **создавать новые** в этом проекте.
3. ПК не засыпает; после перезагрузки якорь поднимается сам.
4. Unity-мост чинится агентом без участия человека.

---

## 1. Что нужно на машине

| Компонент | Требование |
|---|---|
| Claude Code | 2.1.239 или новее |
| Аккаунт | **подписка claude.ai**, вход через `/login`. API-ключ и токены из `claude setup-token` не годятся |
| Unity | версия строго из `igruha/ProjectSettings/ProjectVersion.txt` |
| Git + Git LFS | LFS обязателен, в репозитории больше 5 ГБ объектов |
| Телефон | приложение Claude под тем же аккаунтом |

---

## 2. Репозиторий

```powershell
git clone https://github.com/Bornqazaq/sleepover.git
cd sleepover
git lfs pull     # долго: объектов больше 5 ГБ
```

---

## 3. Первое, что ломает всё остальное — `ANTHROPIC_API_KEY`

**Remote Control отказывает от самого факта наличия этой переменной в
окружении** — даже если ключ отклонён и сессия идёт по OAuth. Ошибка выглядит
так:

```
Error: Remote Control requires claude.ai subscription auth.
ANTHROPIC_API_KEY is set, so this session is using API-key auth
```

Проверить и убрать, сохранив значение:

```powershell
[Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','User')   # есть?
[Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY_BACKUP',
    [Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','User'), 'User')
[Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', $null, 'User')
```

После этого **нужен новый терминал**, а для расширения VSCode — перезапуск
VSCode: уже запущенные процессы держат старую копию окружения.

---

## 4. Включить Remote Control

```powershell
claude rc          # короткий алиас для claude remote-control
```

Спросит режим spawn. **Выбирать `1` (same-dir).** Причины, не абстрактные:

- Unity MCP работает с одним открытым редактором в `igruha`; сессия в отдельном
  worktree окажется в другой папке и до редактора не дотянется;
- каждый worktree — полная выкладка дерева с LFS (>5 ГБ) плюс пересборка
  `Library/`, это десятки минут и гигабайты на сессию.

Плата за same-dir: одну сцену или префаб нельзя править из двух сессий разом.
Это то же правило «одна сцена — один владелец» из `CLAUDE.md`.

Дальше в окне сессии: `space` — QR-код для телефона, `w` — переключить режим
spawn.

Чтобы Remote Control включался в каждой сессии, в `~/.claude/settings.json`:

```json
{ "remoteControlAtStartup": true }
```

Ключ читается только из пользовательских настроек; в настройках проекта он
игнорируется.

---

## 5. Питание

```powershell
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
powercfg /change disk-timeout-ac 0
```

Из Git Bash это не запускать: MSYS превращает `/change` в путь и команда падает
с «неправильные параметры». Только из PowerShell.

---

## 6. Автозапуск после перезагрузки

Два шага, оба обязательны.

**6.1. Ярлык в автозагрузке.** `Win+R` → `shell:startup`, создать ярлык:

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<путь к репозиторию>\tools\remote\remote-sessions.ps1"
```

Агент сможет создать его сам только после выдачи широкого правила `Bash`
(см. раздел 7) — до этого классификатор блокирует запись в «Автозагрузку» как
persistence.

**6.2. Автовход после обновления.** Параметры → Учётные записи → Варианты входа
→ «Использовать мои данные для входа для автоматического завершения настройки
после перезапуска».

Без этого пункта первый бесполезен: «Автозагрузка» срабатывает при **входе в
систему**, а не при загрузке. Если Windows перезагрузится ночью от обновления и
никто не войдёт, якорь не поднимется и подключаться с телефона будет не к чему.
Активные часы помогают лишь частично — Windows не даёт окно шире 18 часов.

Проверить, что включилось (должен быть SID текущего пользователя):

```powershell
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoLogonSID
([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
```

---

## 7. Права

**Агент не может выдать права сам себе** — это защита, а не сбой. Попытка
записать `permissions` блокируется и через shell, и через файловый инструмент.
Раздаёт права только человек, двумя способами:

- скопировать готовый состав в `.claude/settings.local.json` (файл игнорируется
  гитом, права остаются локальными);
- либо просто работать: при первом запросе каждого типа выбирать на телефоне
  «разрешить всегда», и список соберётся сам.

**В `.claude/settings.json` широкие права не класть** — он отслеживается гитом и
общий со вторым разработчиком.

Рабочий состав `.claude/settings.local.json`:

```json
{
  "permissions": {
    "defaultMode": "acceptEdits",
    "allow": [
      "Bash", "Read", "Edit", "Write", "NotebookEdit", "Glob", "Grep",
      "WebFetch", "WebSearch", "Agent", "Task", "TodoWrite", "Skill",
      "SendUserFile", "mcp__unity", "mcp__blender", "mcp__linear",
      "mcp__memory-graph"
    ],
    "deny": [
      "Bash(git push --force:*)",
      "Bash(git push --force-with-lease:*)",
      "Bash(git push -f:*)",
      "Bash(git push origin main:*)",
      "Bash(git push -u origin main:*)",
      "Bash(git reset --hard:*)",
      "Bash(git clean -fdx:*)",
      "Bash(git filter-branch:*)",
      "Bash(git lfs prune:*)",
      "Bash(rm -rf:*)",
      "Bash(reg delete:*)"
    ]
  }
}
```

Почему запреты именно такие, а не «разрешить всё»:

- у каждого есть безопасный эквивалент: вместо `reset --hard` — `git stash`,
  вместо `rm -rf` — `tools/remote/unity-clean.ps1`;
- владелец работает с телефона, откатывать разрушительную операцию некому;
- запрет пуша в `main` — это правило веток из `CLAUDE.md`. Слияние делается
  через PR: `gh pr create` и `gh pr merge`, слияние идёт на стороне GitHub.

Дыра, о которой надо знать: `git push` без аргументов, стоя на `main`, под
правило не попадает — правила сопоставляются по префиксу команды. Надёжно
закрывается git-хуком `pre-push`.

---

## 8. Unity и мост MCP

Unity MCP — это **HTTP-сервер на `http://127.0.0.1:8080/mcp`**, который поднимает
сам редактор. В Unity должно быть открыто окно «MCP for Unity», в статус-баре —
`MCP-FOR-UNITY: Session connected`.

Два следствия, оба неприятных:

1. **Unity должен быть запущен до старта сессии Claude.** Сессия сама редактор
   не поднимет через MCP — нечем.
2. **Уже открытая сессия не увидит только что поднятый MCP-сервер.** Если мост
   перезапустили, нужна новая сессия — её как раз можно создать с телефона.

Живость моста проверяется TCP-пробой порта, не дожидаясь таймаута инструментов:

```powershell
Test-NetConnection 127.0.0.1 -Port 8080 -InformationLevel Quiet
```

---

## 9. Инструменты в `tools/remote/`

Пути внутри скриптов вычисляются от их расположения, ничего править не нужно.

### `unity-guard.ps1` — глаза и руки в редакторе

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\remote\unity-guard.ps1 <действие>
```

| Действие | Что делает |
|---|---|
| `status` | процессы, заголовок и hwnd редактора, `Blocked`, живость моста |
| `windows` | все видимые окна процессов Unity |
| `dialogs` | блокирующие окна и подписи их кнопок |
| `read -Hwnd N` | все подписи дочерних контролов окна |
| `click -Hwnd N -Button "Yes"` | нажать кнопку по подписи (или `-X -Y` по координатам) |
| `key -Hwnd N -Key Enter` | послать клавишу: enter, esc, y, n, space, tab, стрелки |
| `shot -Hwnd N -Out f.png` | скриншот окна |
| `start` / `stop` | поднять редактор по версии из `ProjectVersion.txt` / прибить |

**Признак блокировки — не размер окна, а `EditorEnabled: False`.** Пока висит
модальный диалог, Win32 делает окно-владельца disabled. Ловля «по маленькому
окну» даёт ложные срабатывания: `Addressables Report` безобиден, а полоса
импорта на весь экран блокирует.

`shot -Method print` использует `PrintWindow` с флагом `PW_RENDERFULLCONTENT` —
на Unity с DX12 картинка приходит нормальная, а не чёрная. Всё остальное
работает и при заблокированном рабочем столе.

Ограничение: подписи кнопок читаются у системных диалогов (класс `#32770`).
Собственные модальные окна Unity рисует сама, их кнопок Win32 не видит — там
остаются `Enter`/`Esc` или клик по координатам, снятым со скриншота.

### `unity-clean.ps1` — безопасная замена `rm -rf`

Удаляет **только** регенерируемые папки: `Library`, `Temp`, `obj`, `Logs`,
`.vs`, `ShaderCache`. Всё остальное отбивает, даже если передать явно. Именно
поэтому `rm -rf` может оставаться в запретах.

```powershell
... -File tools\remote\unity-clean.ps1 -What Library -WhatIf   # показать размер
... -File tools\remote\unity-clean.ps1 -What Library           # снести
```

Без `-Force` откажется работать, пока Unity запущен.

### `remote-sessions.ps1` — поднять якорную сессию

Идемпотентен: если якорь с таким именем уже работает, пропускает его.

```powershell
... -File tools\remote\remote-sessions.ps1
... -File tools\remote\remote-sessions.ps1 -Sessions pc-1,pc-2
```

---

## 10. Что делать, когда Unity встал

1. `unity-guard.ps1 status` — понять природу: диалог, зависание или лёг мост.
2. Диалог → `dialogs` прочитать, `shot` посмотреть, `click`/`key` закрыть.
3. Зависание или мёртвый мост → `stop`, затем `start`.
4. Битый импорт, странные ошибки компиляции → закрыть Unity,
   `unity-clean.ps1 -What Library`, запустить снова. Первый импорт долгий.
5. Если сессия не подхватила поднятый мост — создать новую с телефона.

---

## 11. Грабли, на которых уже потеряно время

**PowerShell 5.1 читает `.ps1` без BOM как ANSI.** Кириллица в комментариях
превращается в мусор и ломает разбор. Скрипты в `tools/remote/` сохранены в
UTF-8 с BOM — сохранять так же.

**`wt.exe` трактует `;` как свой разделитель подкоманд.** Команда для вкладки
режется пополам: вкладка открывается, программа не стартует, а `wt` возвращает
0. Поэтому `remote-sessions.ps1` зовёт `claude.exe` напрямую через
`Start-Process`, без промежуточной оболочки.

**`claude agents --json` не перечисляет обычные интерактивные сессии.**
Работающая `claude rc` в этом списке отсутствует. Проверять живость по нему —
значит принимать успешный запуск за провал. Достоверный источник — таблица
процессов:

```powershell
Get-CimInstance Win32_Process -Filter "Name='claude.exe'" |
  Where-Object { $_.CommandLine -like '*--remote-control*' }
```

**`powershell -File` передаёт `a,b,c` одной строкой,** а не массивом. Скрипты,
принимающие списки, разбивают их сами.

**`$PSScriptRoot` не заполнен внутри `param()`** — дефолты вычисляются раньше.
Путь к репозиторию вычисляется после блока параметров.

**Git Bash ломает аргументы вида `/change`,** превращая их в путь. Команды с
такими ключами (`powercfg`) запускать из PowerShell.

**Классификатор блокирует три вещи независимо от прав:** выдать себе права,
поставить автозапуск до выдачи `Bash`, породить отсоединённый процесс. Это не
обходится и обходить не нужно — первые две делает человек один раз.

---

## 12. Проверочный список

```powershell
# 1. Ключа в окружении нет
[Environment]::GetEnvironmentVariable('ANTHROPIC_API_KEY','User')   # пусто

# 2. Якорь живой
Get-CimInstance Win32_Process -Filter "Name='claude.exe'" |
  Where-Object { $_.CommandLine -like '*--remote-control*' -or $_.CommandLine -like '* rc*' } |
  Select-Object ProcessId, CommandLine

# 3. Unity и мост
powershell -File tools\remote\unity-guard.ps1 status     # McpBridge: up

# 4. Автовход после обновления включён
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoLogonSID

# 5. Ярлык автозагрузки на месте
Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\claude-remote.lnk"
```

С телефона: приложение Claude → раздел Code → проект виден, новая сессия
создаётся, `unity-guard.ps1 status` из неё отвечает.

---

## 13. Что всё равно требует человека

| Что | Как часто |
|---|---|
| Ярлык автозагрузки и галочка автовхода | один раз при настройке |
| Выдача прав в `settings.local.json` | один раз |
| Авторизация MCP-серверов через OAuth (например higgsfield) | один раз, если нужны |
| Лицензия Unity | редко |
| Плейтест и оценка «смешно / не смешно» | постоянно, это работа геймдизайнера |

Всё остальное делается с телефона.
