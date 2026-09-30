param(
    [ValidateRange(2,8)][int]$Players = 4,
    [ValidateSet('observe','flight-boundaries','entry-timeout','sleep','timeout','round-timeout','all-dead','giant-leaves','mosquitoes-leave')][string]$Scenario = 'observe',
    [int]$Giant = 1,
    [int]$Port = 17773,
    [string]$BuildDirectory = 'igruha\Builds\Autotest'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$appDirectory = if ([IO.Path]::IsPathRooted($BuildDirectory)) { $BuildDirectory } else { Join-Path $repoRoot $BuildDirectory }
$appPath = Join-Path $appDirectory 'sleepover.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw "Build is missing: $appPath" }
$logDir = Join-Path $appDirectory ('logs\mosquitoes-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Scenario)
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$processes = @()
$arguments = @('--autostart','Mosquitoes','--wait-players',"$Players",'--bot','--port',"$Port",'--mosquito-giant',"$Giant",'--mosquito-check',$Scenario,'-batchmode','-nographics','-logFile',('"' + (Join-Path $logDir 'host.log') + '"'))
$hostProcess = Start-Process -FilePath $appPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$processes += [pscustomobject]@{Role='host';Pid=$hostProcess.Id;ExpectedId=0}
Start-Sleep -Seconds 5
for ($index=1; $index -lt $Players; $index++) {
    $arguments = @('--client','--host','127.0.0.1','--port',"$Port",'--bot','--mosquito-check',$Scenario,'-batchmode','-nographics','-logFile',('"' + (Join-Path $logDir "client-$index.log") + '"'))
    $clientProcess = Start-Process -FilePath $appPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $processes += [pscustomobject]@{Role="client-$index";Pid=$clientProcess.Id;ExpectedId=$index}
    Start-Sleep -Milliseconds 1500
}
$processes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logDir 'processes.json') -Encoding utf8
Write-Output $logDir
$processes | Format-Table
