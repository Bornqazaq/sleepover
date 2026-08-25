<#
  unity-clean.ps1 - delete ONLY regenerable Unity folders.

  Exists so that `rm -rf` can stay denied in permissions while the most common
  Unity repair (wipe Library/Temp/obj and let the editor rebuild) is still
  available to an agent working remotely.

  Anything not on the allowlist below is refused, no matter what is passed in.
  Nothing under Assets/, ProjectSettings/, Packages/ or .git/ can be touched.

    unity-clean.ps1                       -> wipe Library, Temp, obj, Logs
    unity-clean.ps1 -What Library         -> wipe just one
    unity-clean.ps1 -WhatIf               -> show sizes, delete nothing
    unity-clean.ps1 -Force                -> proceed even if Unity is running
#>
[CmdletBinding()]
param(
    [string[]] $What = @('Library', 'Temp', 'obj', 'Logs'),
    [string]   $Project,
    [switch]   $WhatIf,
    [switch]   $Force
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is NOT populated while param() defaults are evaluated, so the
# repo root is resolved here instead. Layout assumed: <repo>/tools/remote/*.ps1
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Project) { $Project = Join-Path $RepoRoot 'igruha' }

# powershell.exe -File hands "a,b,c" over as ONE string instead of an array,
# so split it here and accept both call styles.
$What = @($What | ForEach-Object { $_ -split '[,;]' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# The only folders this script will ever remove. Unity rebuilds every one of
# them on next launch; none holds authored content.
$Allowed = @{
    'Library'     = 'asset import cache and compiled assemblies'
    'Temp'        = 'scratch space from the running editor'
    'obj'         = 'MSBuild intermediates'
    'Logs'        = 'editor logs'
    '.vs'         = 'Visual Studio cache'
    'ShaderCache' = 'compiled shader variants (inside Library)'
}

if (-not (Test-Path $Project)) { throw "project not found: $Project" }
$root = (Resolve-Path $Project).Path

foreach ($name in $What) {
    if (-not $Allowed.ContainsKey($name)) {
        throw "refusing '$name' - not a regenerable folder. Allowed: $($Allowed.Keys -join ', ')"
    }
}

if (-not $Force -and -not $WhatIf) {
    $running = @(Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)
    if ($running.Count) {
        throw "Unity is running (pid $($running.Id -join ', ')). Close it first, or pass -Force."
    }
}

function Get-FolderSize([string] $Path) {
    try {
        $b = (Get-ChildItem $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
                Measure-Object -Property Length -Sum).Sum
        if (-not $b) { return '0 B' }
        if ($b -ge 1GB) { return ('{0:N1} GB' -f ($b / 1GB)) }
        if ($b -ge 1MB) { return ('{0:N0} MB' -f ($b / 1MB)) }
        return ('{0:N0} KB' -f ($b / 1KB))
    } catch { return 'unknown' }
}

foreach ($name in $What) {
    $target = Join-Path $root $name

    # Belt and braces: the resolved path must still sit inside the project.
    if (-not (Test-Path $target)) { "skip   $name  (absent)"; continue }
    $full = (Resolve-Path $target).Path
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing '$full' - resolves outside $root"
    }
    if ($full.TrimEnd('\') -ieq $root.TrimEnd('\')) {
        throw "refusing to delete the project root"
    }

    $size = Get-FolderSize $full
    if ($WhatIf) { "would delete  $name  ($size)  - $($Allowed[$name])"; continue }

    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
    "deleted  $name  ($size)"
}

if ($WhatIf) { '', 'nothing was deleted (-WhatIf)' }
else { '', 'done - Unity will rebuild these on next launch (first import takes a while)' }
