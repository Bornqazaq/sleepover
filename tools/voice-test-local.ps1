# Поднять хост + клиент на одной машине для проверки голоса.
# Хост — окно 1, клиент — окно 2. Оба на localhost.

$ErrorActionPreference = "Stop"
$Root = "C:\Users\ASRock\Desktop\sleepover"
$Exe = Join-Path $Root "igruha\Builds\Release\Windows\Komnata.exe"
if (-not (Test-Path $Exe)) {
    Write-Error "Нет билда: $Exe. Сначала собери Windows."
}

$Logs = Join-Path $Root "igruha\Builds\Release\voice-test-logs"
New-Item -ItemType Directory -Force -Path $Logs | Out-Null
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$HostLog = Join-Path $Logs "host-$Stamp.log"
$ClientLog = Join-Path $Logs "client-$Stamp.log"

Get-Process Komnata -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Хост поднимаю..."
# --wait-players пропускает экран входа и сразу делает хост.
Start-Process -FilePath $Exe -ArgumentList @(
    "--wait-players", "2",
    "-screen-fullscreen", "0",
    "-screen-width", "1280",
    "-screen-height", "720",
    "-logFile", $HostLog
)

Write-Host "Жду 12 сек..."
Start-Sleep -Seconds 12

Write-Host "Клиент → 127.0.0.1"
Start-Process -FilePath $Exe -ArgumentList @(
    "--client",
    "--host", "127.0.0.1",
    "-screen-fullscreen", "0",
    "-screen-width", "1280",
    "-screen-height", "720",
    "-logFile", $ClientLog
)

Write-Host ""
Write-Host "Готово. Два окна Komnata в хабе."
Write-Host ""
Write-Host "КАК ПРОВЕРИТЬ ГОЛОС:"
Write-Host "1. Надень наушники (без них будет эхо)."
Write-Host "2. Кликни в одно окно (фокус)."
Write-Host "3. M — включить микрофон (если выключен)."
Write-Host "4. Зажми V и говори."
Write-Host "5. Во ВТОРОМ окне должен быть слышен твой голос."
Write-Host "6. Esc — пауза и ползунок громкости голоса."
Write-Host "7. F4 — расширенные настройки."
Write-Host ""
Write-Host "Логи: $Logs"
