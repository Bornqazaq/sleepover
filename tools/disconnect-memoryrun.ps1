# Дисконнект одного участника стенда «Рейса на память» (Windows).
#
# Спека 10.2 разводит четыре ветки ухода, и различаются они не моментом,
# а тем, КЕМ был ушедший: ожидающий, тот-чей-сейчас-ход, уже дошедший
# и предпоследний. Кого убирать, выбирает вызывающий; скрипт только
# доводит дело до конкретного процесса и не путает его с сервером.
#
# ⚠️ ГЛАВНАЯ СТРАХОВКА. В pids.txt хост записан наравне с клиентами и его
# id — ноль, потому что у NGO сервер тоже держит clientId 0. Скрипт,
# ищущий процесс по id, находил на этом месте хост и убивал матч целиком
# вместо одного участника (грабли из STATE 3.18). Поэтому здесь ищут не
# по id, а по РОЛИ, и роль 'host' отбивается отдельной проверкой.
#
# 🔴 ПОЧЕМУ ЗАМОРОЗКА, А НЕ УБИЙСТВО — замерено 26.08 на «Рейсе на память».
#
# Убитый процесс отдаёт свой UDP-порт системе, и первая же датаграмма хоста
# в этот порт возвращается ICMP «port unreachable». На Windows это всплывает
# ошибкой на СОКЕТЕ ХОСТА, и хост перестаёт слать всем — не только ушедшему.
# Выглядит это так: убиваешь одного из четверых, очередь встаёт на живом
# игроке, а через тридцать секунд разом отваливаются все трое с одинаковым
# «хост больше не отвечает». Воспроизведено дважды подряд; в игре при этом
# ни одного исключения, и на macOS того же не происходит.
#
# Замороженный процесс сокет держит открытым и просто молчит. Хост не
# получает от него ничего, через m_DisconnectTimeoutMS (30 с) объявляет
# его ушедшим — и остальных не задевает. Это и ближе к настоящему обрыву:
# в жизни у клиента отваливается Wi-Fi, а не закрывается порт.
#
# Использование:
#   powershell -NoProfile -File tools/disconnect-memoryrun.ps1 2
#   powershell -NoProfile -File tools/disconnect-memoryrun.ps1 2 -Kill
#   powershell -NoProfile -File tools/disconnect-memoryrun.ps1 2 -Logs <каталог>

param(
    [Parameter(Mandatory = $true)][int]$ClientId,
    [string]$Logs = "",
    [switch]$Kill
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrEmpty($Logs)) {
    $Logs = Join-Path $Root "igruha\Builds\Autotest\logs\latest"
}

$PidsFile = Join-Path $Logs "pids.txt"
if (-not (Test-Path $PidsFile)) {
    Write-Error "Нет pids.txt: $PidsFile"
    exit 1
}

if ($ClientId -lt 1) {
    Write-Error "id $ClientId — это сервер, а не участник. Хост этим скриптом не трогаем: см. шапку файла."
    exit 1
}

$target = $null
foreach ($line in Get-Content $PidsFile) {
    if ($line.StartsWith("#") -or [string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split "\s+"
    if ($parts.Length -lt 3) { continue }

    # Роль, а не id: строка хоста тоже несёт id 0.
    if ($parts[0] -eq "host") { continue }
    if ([int]$parts[2] -eq $ClientId) { $target = $parts; break }
}

if ($null -eq $target) {
    Write-Error "В pids.txt нет клиента с id $ClientId"
    exit 1
}

$targetPid = [int]$target[1]
$proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
if ($null -eq $proc) {
    Write-Host "Клиент $ClientId (pid $targetPid) уже не жив — трогать нечего"
    exit 0
}

if ($Kill) {
    Stop-Process -Id $targetPid -Force
    Write-Host "💀 клиент $ClientId (роль $($target[0]), pid $targetPid) УБИТ — на Windows это заодно роняет сокет хоста, см. шапку"
    exit 0
}

Add-Type -Namespace Igruha -Name Proc -MemberDefinition @'
[DllImport("ntdll.dll", SetLastError = true)]
public static extern int NtSuspendProcess(IntPtr processHandle);
'@

$rc = [Igruha.Proc]::NtSuspendProcess($proc.Handle)
if ($rc -ne 0) {
    Write-Error "Не удалось заморозить pid $targetPid (NTSTATUS $rc)"
    exit 1
}

Write-Host "🔌 клиент $ClientId (роль $($target[0]), pid $targetPid) заморожен — хост объявит его ушедшим через ~30 с"
