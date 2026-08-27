<#
  remote-sessions.ps1
  Поднимает якорную сессию Claude Code с Remote Control (spawn mode) на этой машине.
  Каждая сессия видна в приложении Claude на телефоне и на claude.ai/code,
  но выполняется здесь, на ПК (Unity MCP, git, файлы — локальные).

  Запуск вручную:
    powershell -ExecutionPolicy Bypass -File "$env:USERPROFILE\.claude\remote-sessions.ps1"
  Пока она открыта, новые сессии можно создавать с телефона или с claude.ai/code.
  Несколько преднагретых сессий:
    ... -Sessions pc-1,pc-2,pc-3

  ВАЖНО: Remote Control работает только под подпиской claude.ai. Если в окружении
  есть ANTHROPIC_API_KEY, Claude Code уходит на API-ключ и Remote Control отказывает.
  Ключ перенесён в ANTHROPIC_API_KEY_BACKUP; скрипт дополнительно снимает переменную
  в дочерних оболочках на случай устаревшего окружения.
#>
param(
    [string]   $Repo,
    [string[]] $Sessions = @('pc-anchor'),
    [string]   $Window   = 'rc'
)

# $PSScriptRoot is NOT populated while param() defaults are evaluated, so the
# repo root is resolved here instead. Layout assumed: <repo>/tools/remote/*.ps1
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Repo) { $Repo = $RepoRoot }

# Resolve claude.exe from PATH first so this works on any machine whatever
# install method was used; fall back to known install locations.
# A native install puts claude.exe on PATH. An npm -g install does NOT: it only
# creates claude.cmd/.ps1 shims in the npm root, while the real binary sits
# inside the package. Looking on PATH alone made this script throw on a machine
# where Claude Code was perfectly healthy (26.08, вторая машина).
$claude = (Get-Command claude.exe -ErrorAction SilentlyContinue).Source
if (-not $claude) {
    $claude = @(
        (Join-Path $env:USERPROFILE '.local\bin\claude.exe'),
        (Join-Path $env:APPDATA 'npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
# Last resort: the npm shim. Start-Process handles .cmd, it just costs a shell.
if (-not $claude) { $claude = (Get-Command claude -ErrorAction SilentlyContinue).Source }
if (-not $claude) { throw 'claude executable not found - install Claude Code first' }
if (-not (Test-Path $Repo))   { throw "directory not found: $Repo" }

Remove-Item Env:ANTHROPIC_API_KEY -ErrorAction SilentlyContinue

# Launch claude.exe directly. Going through wt.exe looked nicer but broke twice:
# wt treats ';' as its own subcommand separator, and it re-parses the trailing
# command line with its own quoting rules - the tab opened, claude never
# started, and wt still exited 0. No shell in between means no quoting layer to
# get wrong; neither the exe path nor a session name contains a space.
# Truth about which sessions are up comes from the process table, NOT from
# `claude agents --json` - that listing omits plain interactive sessions
# (a working `claude rc` is missing from it), so using it as the check reports
# a healthy launch as a failure.
function Get-LiveAnchors {
    @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty CommandLine) | Where-Object { $_ -like '*--remote-control*' }
}

$before = Get-LiveAnchors
foreach ($name in $Sessions) {
    if ($before | Where-Object { $_ -like "*--remote-control $name*" }) {
        Write-Host "skip   $name (already running)" -ForegroundColor DarkGray
        continue
    }
    Start-Process -FilePath $claude -ArgumentList '--remote-control', $name -WorkingDirectory $Repo
    Start-Sleep -Seconds 8
}

$after = Get-LiveAnchors
$missing = @($Sessions | Where-Object { $n = $_; -not ($after | Where-Object { $_ -like "*--remote-control $n*" }) })
if ($missing.Count) {
    Write-Host ("WARNING - not running: " + ($missing -join ', ')) -ForegroundColor Yellow
} else {
    Write-Host ("OK - anchors live: " + ($Sessions -join ', ')) -ForegroundColor Green
    Write-Host "They appear in the Claude mobile app within a few seconds." -ForegroundColor Green
}
