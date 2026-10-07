#Requires -RunAsAdministrator
# Builds paxpanel into <repo>\publish and registers an elevated logon task named "paxpanel".
param([string]$InstallDir = (Join-Path $PSScriptRoot '..\publish'))
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$userConfig = Join-Path $InstallDir 'config.json'
$saved = Join-Path $env:TEMP 'paxpanel-config.json'

if (-not (Test-Path (Join-Path $repo 'web\assets'))) {
    Write-Warning 'web\assets is missing: run scripts\fetch-assets.ps1 first for fonts and logos.'
}
Remove-Item $saved -ErrorAction SilentlyContinue
$hadConfig = Test-Path $userConfig
if ($hadConfig) { Copy-Item $userConfig $saved -Force }

Get-Process PaxPanel -ErrorAction SilentlyContinue | Stop-Process -Force
try {
    dotnet publish (Join-Path $repo 'src\PaxPanel\PaxPanel.csproj') -c Release -r win-x64 --self-contained false -o $InstallDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
}
finally {
    # Keep the user's edited config.json, whether or not the build succeeded.
    if ($hadConfig) { Copy-Item $saved $userConfig -Force; Remove-Item $saved }
}

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute (Join-Path $InstallDir 'PaxPanel.exe') -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
# Re-run every 5 minutes after logon: a no-op while the panel runs (single instance), a restart if it crashed or exited.
$trigger.Repetition = (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)).Repetition
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'paxpanel' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'paxpanel'
"paxpanel installed to $InstallDir and started."
"Edit $userConfig, then right-click the panel > Reload."
