# Host + client fullscreen on one machine for voice test.
$ErrorActionPreference = "Stop"
$Root = "C:\Users\ASRock\Desktop\sleepover"
$Exe = Join-Path $Root "igruha\Builds\Release\Windows\Komnata.exe"
if (-not (Test-Path $Exe)) { Write-Error "No build: $Exe" }

$Logs = Join-Path $Root "igruha\Builds\Release\voice-test-logs"
New-Item -ItemType Directory -Force -Path $Logs | Out-Null
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$HostLog = Join-Path $Logs "host-$Stamp.log"
$ClientLog = Join-Path $Logs "client-$Stamp.log"

Add-Type -AssemblyName System.Windows.Forms
$Screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$Width = $Screen.Width
$Height = $Screen.Height

Get-Process Komnata -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Host fullscreen $Width x $Height"
Start-Process -FilePath $Exe -ArgumentList @(
    "--wait-players", "2",
    "-screen-fullscreen", "1",
    "-screen-width", "$Width",
    "-screen-height", "$Height",
    "-logFile", $HostLog
)

Write-Host "Wait 12s..."
Start-Sleep -Seconds 12

Write-Host "Client fullscreen -> 127.0.0.1"
Start-Process -FilePath $Exe -ArgumentList @(
    "--client",
    "--host", "127.0.0.1",
    "-screen-fullscreen", "1",
    "-screen-width", "$Width",
    "-screen-height", "$Height",
    "-logFile", $ClientLog
)

Write-Host ""
Write-Host "Ready. Two fullscreen windows. Switch with Alt+Tab."
Write-Host "Headphones -> focus one window -> hold V -> speak -> Alt+Tab to other."
Write-Host "Esc = pause + volume. Logs: $Logs"
