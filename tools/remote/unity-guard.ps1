<#
  unity-guard.ps1 - watchdog helper for the Unity editor and its MCP bridge.

  Written for Claude Code so a remote session can: see whether Unity is alive,
  read what a blocking dialog says, press its buttons, take a screenshot,
  and start or stop the editor.

  Actions:
    status                      Unity process + MCP bridge + responsiveness
    windows                     all top-level windows of Unity processes
    dialogs                     only windows that look like blocking dialogs
    read    -Hwnd <n>           dump every child control caption of a window
    click   -Hwnd <n> -Button "Yes"      press a button by caption
    click   -Hwnd <n> -X 100 -Y 40       click at client coordinates
    key     -Hwnd <n> -Key Enter|Esc|Y|N press a key into a window
    shot   [-Hwnd <n>] [-Out path] [-Method print|screen]
    start                       launch the editor for the project
    stop                        force-kill the editor

  Everything except `shot` works while the desktop is locked.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('status', 'windows', 'dialogs', 'read', 'click', 'key', 'shot', 'start', 'stop')]
    [string] $Action = 'status',

    [int]    $Hwnd,
    [string] $Button,
    [string] $Key,
    [int]    $X = -1,
    [int]    $Y = -1,
    [string] $Out,
    [ValidateSet('print', 'screen')]
    [string] $Method = 'print',
    [switch] $All,
    [string] $ProjectPath,
    [string] $McpHost = '127.0.0.1',
    [int]    $McpPort = 8080
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is NOT populated while param() defaults are evaluated, so the
# repo root is resolved here instead. Layout assumed: <repo>/tools/remote/*.ps1
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ProjectPath) { $ProjectPath = Join-Path $RepoRoot 'igruha' }

# ---------------------------------------------------------------- win32 -----

if (-not ('UG.Native' -as [type])) {
    Add-Type -Namespace UG -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc cb, IntPtr p);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint ms, out IntPtr res);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
[DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

$WM_NULL = 0x0000; $WM_COMMAND = 0x0111; $WM_GETTEXT = 0x000D; $WM_GETTEXTLENGTH = 0x000E
$WM_KEYDOWN = 0x0100; $WM_KEYUP = 0x0101; $WM_CHAR = 0x0102
$WM_LBUTTONDOWN = 0x0201; $WM_LBUTTONUP = 0x0202
$BM_CLICK = 0x00F5; $BN_CLICKED = 0

function Get-Text([IntPtr] $h) {
    $sb = New-Object System.Text.StringBuilder 1024
    [void][UG.Native]::GetWindowTextW($h, $sb, $sb.Capacity)
    $t = $sb.ToString()
    if ([string]::IsNullOrEmpty($t)) {
        # controls answer WM_GETTEXT even when GetWindowText comes back empty
        $len = [UG.Native]::SendMessage($h, $WM_GETTEXTLENGTH, [IntPtr]::Zero, [IntPtr]::Zero)
        if ([int]$len -gt 0) {
            $sb2 = New-Object System.Text.StringBuilder ([int]$len + 2)
            [void][UG.Native]::SendMessage($h, $WM_GETTEXT, [IntPtr]$sb2.Capacity, $sb2)
            $t = $sb2.ToString()
        }
    }
    return $t
}

function Get-Class([IntPtr] $h) {
    $sb = New-Object System.Text.StringBuilder 256
    [void][UG.Native]::GetClassNameW($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Get-UnityPids {
    $names = 'Unity', 'UnityHub', 'Unity Hub'
    $pids = @()
    foreach ($n in $names) {
        foreach ($p in @(Get-Process -Name $n -ErrorAction SilentlyContinue)) { $pids += $p.Id }
    }
    return $pids
}

# $Pids empty or $null means "every process" - used by -All, so that a Windows
# Update prompt or a crash box blocking the desktop is visible too, not just
# Unity's own dialogs.
function Get-TopWindows([int[]] $Pids) {
    $found = New-Object System.Collections.ArrayList
    $cb = [UG.Native+EnumWindowsProc] {
        param($h, $p)
        $owner = 0
        [void][UG.Native]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($Pids -and $Pids.Count -and ($Pids -notcontains $owner)) { return $true }
        if (-not [UG.Native]::IsWindowVisible($h)) { return $true }
        $r = New-Object UG.Native+RECT
        [void][UG.Native]::GetWindowRect($h, [ref]$r)
        [void]$found.Add([pscustomobject]@{
                Hwnd    = [int64]$h
                Pid     = $owner
                Class   = Get-Class $h
                Title   = Get-Text $h
                Enabled = [UG.Native]::IsWindowEnabled($h)
                W       = $r.Right - $r.Left
                H       = $r.Bottom - $r.Top
                Left    = $r.Left
                Top     = $r.Top
            })
        return $true
    }
    [void][UG.Native]::EnumWindows($cb, [IntPtr]::Zero)
    return $found
}

function Get-Children([IntPtr] $h) {
    $found = New-Object System.Collections.ArrayList
    $cb = [UG.Native+EnumWindowsProc] {
        param($c, $p)
        [void]$found.Add([pscustomobject]@{
                Hwnd    = [int64]$c
                Class   = Get-Class $c
                Text    = Get-Text $c
                CtrlId  = [UG.Native]::GetDlgCtrlID($c)
                Enabled = [UG.Native]::IsWindowEnabled($c)
            })
        return $true
    }
    [void][UG.Native]::EnumChildWindows($h, $cb, [IntPtr]::Zero)
    return $found
}

function Test-Responding([IntPtr] $h, [int] $Ms = 2000) {
    $res = [IntPtr]::Zero
    $r = [UG.Native]::SendMessageTimeout($h, $WM_NULL, [IntPtr]::Zero, [IntPtr]::Zero, 0x2, $Ms, [ref]$res)
    return ($r -ne [IntPtr]::Zero)
}

function Test-Port([string] $TargetHost, [int] $Port, [int] $Ms = 1500) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $ar = $c.BeginConnect($TargetHost, $Port, $null, $null)
        if (-not $ar.AsyncWaitHandle.WaitOne($Ms)) { return $false }
        $c.EndConnect($ar); return $true
    } catch { return $false } finally { $c.Close() }
}

# The reliable modality signal on Win32: while a modal dialog is up, its owner
# window is DISABLED. Size means nothing - "Addressables Report" is small and
# harmless, a full-screen import bar is huge and blocking. Titles only add the
# popups that stall the bridge without formally disabling the editor.
$BlockingTitlePatterns = @(
    'Hold on', 'Safe Mode', 'API Update', 'Update Script', 'Save Changes',
    'Unsaved', 'Compil', 'Importing', 'Restart', 'License', 'Sign in',
    'Crash', 'Confirm', 'Are you sure', 'Quit'
)

function Get-MainWindow($wins) {
    $wins | Where-Object { $_.Class -like '*UnityContainerWndClass*' } |
        Sort-Object -Property @{Expression = { $_.W * $_.H }; Descending = $true } |
        Select-Object -First 1
}

function Get-Blockers($wins) {
    $main = Get-MainWindow $wins
    $wins | Where-Object {
        if ($main -and $_.Hwnd -eq $main.Hwnd) { return $false }
        if ($_.Class -eq '#32770') { return $true }   # native MessageBox
        foreach ($p in $BlockingTitlePatterns) {
            if ($_.Title -like "*$p*") { return $true }
        }
        # any Unity window that is up while the editor itself is disabled
        if ($main -and -not $main.Enabled -and $_.Class -like '*UnityContainerWndClass*') { return $true }
        return $false
    }
}

$VK = @{
    'enter' = 0x0D; 'return' = 0x0D; 'esc' = 0x1B; 'escape' = 0x1B
    'y' = 0x59; 'n' = 0x4E; 'space' = 0x20; 'tab' = 0x09
    'left' = 0x25; 'right' = 0x27; 'up' = 0x26; 'down' = 0x28
}

# --------------------------------------------------------------- actions ----

switch ($Action) {

    'status' {
        $pids = Get-UnityPids
        $procs = @(Get-Process -Id $pids -ErrorAction SilentlyContinue)
        $wins = if ($pids.Count) { Get-TopWindows $pids } else { @() }
        $main = Get-MainWindow $wins
        $blockers = @(Get-Blockers $wins)

        [pscustomobject]@{
            UnityRunning   = [bool]$procs.Count
            Processes      = ($procs | ForEach-Object { "$($_.ProcessName)#$($_.Id)" }) -join ', '
            ProcResponding = ($procs | ForEach-Object { $_.Responding }) -join ', '
            Editor         = if ($main) { $main.Title } else { $null }
            EditorHwnd     = if ($main) { $main.Hwnd } else { $null }
            EditorEnabled  = if ($main) { $main.Enabled } else { $null }
            EditorResponds = if ($main) { Test-Responding ([IntPtr]$main.Hwnd) } else { $null }
            Blocked        = if ($main) { (-not $main.Enabled) -or [bool]$blockers.Count } else { $false }
            Blocking       = if ($blockers.Count) { ($blockers | ForEach-Object { "$($_.Title) [hwnd=$($_.Hwnd)]" }) -join ' | ' } else { 'none' }
            McpBridge      = if (Test-Port $McpHost $McpPort) { "up ($McpHost`:$McpPort)" } else { "DOWN ($McpHost`:$McpPort)" }
        } | Format-List
    }

    'windows' {
        $pids = Get-UnityPids
        if (-not $pids.Count) { 'Unity is not running.'; break }
        Get-TopWindows $pids | Sort-Object -Property @{Expression = { $_.W * $_.H }; Descending = $true } |
            Format-Table Hwnd, Pid, Enabled, W, H, Class, Title -AutoSize
    }

    'dialogs' {
        $pids = Get-UnityPids
        if (-not $pids.Count) { 'Unity is not running.'; break }
        $d = @(Get-Blockers (Get-TopWindows $pids))
        if (-not $d.Count) { 'No blocking dialog found.'; break }
        foreach ($w in $d) {
            "=== hwnd=$($w.Hwnd)  class=$($w.Class)  size=$($w.W)x$($w.H)  enabled=$($w.Enabled)  title: $($w.Title)"
            foreach ($c in Get-Children ([IntPtr]$w.Hwnd)) {
                if ($c.Text) { "    [$($c.Class)] id=$($c.CtrlId) enabled=$($c.Enabled) : $($c.Text)" }
            }
        }
    }

    'read' {
        if (-not $Hwnd) { throw 'read requires -Hwnd' }
        $h = [IntPtr]$Hwnd
        "title: $(Get-Text $h)"
        "class: $(Get-Class $h)"
        foreach ($c in Get-Children $h) {
            "[$($c.Class)] id=$($c.CtrlId) enabled=$($c.Enabled) hwnd=$($c.Hwnd) : $($c.Text)"
        }
    }

    'click' {
        if (-not $Hwnd) { throw 'click requires -Hwnd' }
        $h = [IntPtr]$Hwnd

        if ($Button) {
            $target = Get-Children $h | Where-Object {
                $_.Class -match 'Button' -and $_.Text -and
                ($_.Text -replace '&', '') -like "*$Button*"
            } | Select-Object -First 1

            if (-not $target) {
                "No button matching '$Button'. Available:"
                Get-Children $h | Where-Object { $_.Text } | ForEach-Object { "    [$($_.Class)] $($_.Text)" }
                break
            }
            $bh = [IntPtr]$target.Hwnd
            [void][UG.Native]::PostMessage($bh, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
            $wp = [IntPtr](($BN_CLICKED -shl 16) -bor ($target.CtrlId -band 0xFFFF))
            [void][UG.Native]::PostMessage($h, $WM_COMMAND, $wp, $bh)
            "clicked: $($target.Text)  (id=$($target.CtrlId))"
            break
        }

        if ($X -ge 0 -and $Y -ge 0) {
            $lp = [IntPtr](($Y -shl 16) -bor ($X -band 0xFFFF))
            [void][UG.Native]::PostMessage($h, $WM_LBUTTONDOWN, [IntPtr]1, $lp)
            Start-Sleep -Milliseconds 60
            [void][UG.Native]::PostMessage($h, $WM_LBUTTONUP, [IntPtr]0, $lp)
            "clicked at client ($X,$Y)"
            break
        }
        throw 'click requires -Button <caption> or -X <n> -Y <n>'
    }

    'key' {
        if (-not $Hwnd) { throw 'key requires -Hwnd' }
        if (-not $Key) { throw 'key requires -Key' }
        $code = $VK[$Key.ToLower()]
        if (-not $code) { throw "unknown key '$Key'. Known: $($VK.Keys -join ', ')" }
        $h = [IntPtr]$Hwnd
        [void][UG.Native]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 120
        [void][UG.Native]::PostMessage($h, $WM_KEYDOWN, [IntPtr]$code, [IntPtr]0)
        Start-Sleep -Milliseconds 40
        [void][UG.Native]::PostMessage($h, $WM_KEYUP, [IntPtr]$code, [IntPtr]0)
        if ($code -eq 0x0D -or $code -eq 0x59 -or $code -eq 0x4E) {
            [void][UG.Native]::PostMessage($h, $WM_CHAR, [IntPtr]$code, [IntPtr]0)
        }
        "sent $Key to hwnd=$Hwnd"
    }

    'shot' {
        Add-Type -AssemblyName System.Drawing
        if (-not $Out) { $Out = Join-Path $env:TEMP ('unity-shot-' + (Get-Random) + '.png') }

        if ($Hwnd) {
            $h = [IntPtr]$Hwnd
            $r = New-Object UG.Native+RECT
            [void][UG.Native]::GetWindowRect($h, [ref]$r)
            $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
            if ($w -le 0 -or $ht -le 0) { throw "window $Hwnd has no size" }
            $bmp = New-Object System.Drawing.Bitmap $w, $ht
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            if ($Method -eq 'print') {
                $hdc = $g.GetHdc()
                # 2 = PW_RENDERFULLCONTENT, needed for DWM/GPU windows
                $ok = [UG.Native]::PrintWindow($h, $hdc, 2)
                $g.ReleaseHdc($hdc)
                if (-not $ok) { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size) }
            } else {
                [void][UG.Native]::ShowWindow($h, 9)
                [void][UG.Native]::SetForegroundWindow($h)
                Start-Sleep -Milliseconds 350
                [void][UG.Native]::GetWindowRect($h, [ref]$r)
                $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
            }
            $g.Dispose()
        } else {
            Add-Type -AssemblyName System.Windows.Forms
            $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
            $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $bmp.Size)
            $g.Dispose()
        }
        $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $Out
    }

    'start' {
        if (Get-UnityPids) { 'Unity is already running.'; break }
        $verFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
        if (-not (Test-Path $verFile)) { throw "ProjectVersion.txt not found under $ProjectPath" }
        $ver = (Select-String -Path $verFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
        $exe = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
        if (-not (Test-Path $exe)) {
            $cand = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' } |
                Where-Object { Test-Path $_ } | Select-Object -First 1
            if (-not $cand) { throw "Unity $ver not found and no fallback editor under C:\Program Files\Unity\Hub\Editor" }
            $exe = $cand
        }
        Start-Process -FilePath $exe -ArgumentList '-projectPath', $ProjectPath
        "launched: $exe`nproject : $ProjectPath"
    }

    'stop' {
        $pids = Get-UnityPids
        if (-not $pids.Count) { 'Unity is not running.'; break }
        foreach ($p in $pids) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }
        "stopped pids: $($pids -join ', ')"
    }
}
