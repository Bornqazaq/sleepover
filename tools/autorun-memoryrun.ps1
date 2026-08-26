# Автопрогон «Рейса на память» на нескольких процессах одной машины (Windows).
#
# Зачем. Живьём восьмерых собрать можно раз в неделю, а проверять сеть надо
# каждый день. Редактор восьмерых не рисует, поэтому хост — билд в окне,
# а клиенты — headless: они вообще ничего не рисуют.
#
# Чего этот стенд НЕ проверяет и проверить не может: разный пинг и потери,
# macOS-билд, и всё, что живёт «по машине на игрока». Это остаётся живой катке.
#
# Windows-двойник tools/autorun-exam.sh. Отдельным файлом, а не флагом
# в общем скрипте: пути, запуск и убийство процессов различаются целиком,
# и «универсальный» скрипт на две системы врал бы в обеих.
#
# Использование:
#   powershell -NoProfile -File tools/autorun-memoryrun.ps1 [всего] [очередь]
#   powershell -NoProfile -File tools/autorun-memoryrun.ps1 8 MemoryRun
#   powershell -NoProfile -File tools/autorun-memoryrun.ps1 4 MemoryRun,Exam

param(
    [int]$Players = 8,
    [string]$Games = "MemoryRun",
    [string]$HostAddress = "127.0.0.1"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$App = Join-Path $Root "igruha\Builds\Autotest\sleepover.exe"

if (-not (Test-Path $App)) {
    Write-Error "Нет тестового билда: $App`nСобрать отдельно от раздаточного — Builds\Autotest, не Builds\Windows."
    exit 1
}

if ($Players -lt 2 -or $Players -gt 8) {
    Write-Error "Участников должно быть от 2 до 8, а не $Players"
    exit 1
}

$Run = Get-Date -Format "yyyyMMdd-HHmmss"
$Logs = Join-Path $Root "igruha\Builds\Autotest\logs\$Run"
New-Item -ItemType Directory -Force -Path $Logs | Out-Null

# Прошлый прогон мог не дожить до конца: висящий хост держит порт 7777,
# и новый молча не поднимется.
Get-Process -Name "sleepover" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$Latest = Join-Path $Root "igruha\Builds\Autotest\logs\latest"
if (Test-Path $Latest) { Remove-Item $Latest -Recurse -Force }
New-Item -ItemType Junction -Path $Latest -Target $Logs | Out-Null

$Pids = Join-Path $Logs "pids.txt"

# ⚠️ Хост пишется в pids.txt ПЕРВЫМ и с ролью в первой колонке, а его id — 0.
# Ноль здесь означает «сервер», а не «клиент номер ноль»: у NGO хост тоже
# держит clientId 0, и скрипт дисконнектов, ищущий процесс по id, находил
# именно хост и убивал матч целиком вместо одного участника (грабли из
# STATE 3.18). Роль в первой колонке — то, по чему это различается.
"# роль pid id — роль 'host' убивать нельзя, это сервер, а не участник" |
    Out-File -FilePath $Pids -Encoding utf8

$hostArgs = @(
    "--autostart", $Games,
    "--wait-players", "$Players",
    "--bot",
    "-screen-fullscreen", "0",
    "-screen-width", "1280",
    "-screen-height", "720",
    "-logFile", (Join-Path $Logs "host.log")
)

$hostProc = Start-Process -FilePath $App -ArgumentList $hostArgs -PassThru
"host $($hostProc.Id) 0" | Out-File -FilePath $Pids -Encoding utf8 -Append
Write-Host "🟢 хост поднят (pid $($hostProc.Id)), лог $Logs\host.log"

# Хосту нужно поднять сервер и загрузить хаб раньше, чем постучится первый.
Start-Sleep -Seconds 8

# Клиенты стартуют по одному с паузой: NGO раздаёт clientId в порядке
# подключения, и без паузы соответствие «процесс → id» разъезжается,
# а без него не адресовать дисконнект нужному участнику.
for ($i = 1; $i -lt $Players; $i++) {
    $clientArgs = @(
        "--client",
        "--host", $HostAddress,
        "--bot",
        "-batchmode", "-nographics",
        "-logFile", (Join-Path $Logs "client-$i.log")
    )

    $proc = Start-Process -FilePath $App -ArgumentList $clientArgs -PassThru
    "client-$i $($proc.Id) $i" | Out-File -FilePath $Pids -Encoding utf8 -Append
    Write-Host "   клиент $i (ожидаемый id=$i, pid $($proc.Id)), лог $Logs\client-$i.log"
    Start-Sleep -Milliseconds 1500
}

Write-Host ""
Write-Host "Логи прогона:  $Logs"
Write-Host "Процессы:      $Pids"
Write-Host "Остановить:    Get-Process sleepover | Stop-Process -Force"
