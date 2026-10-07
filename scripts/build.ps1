# Builds PaxPanel.exe into the project folder (next to config.json and web\) and, unless -NoShortcut,
# puts a "paxpanel" shortcut on the desktop. Launch it by hand: it asks for administrator rights (UAC)
# because the CPU and fan sensors need them.
param([switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = Join-Path $repo 'PaxPanel.exe'
$out = Join-Path $repo 'build'

if (Get-Process PaxPanel -ErrorAction SilentlyContinue) {
    throw 'paxpanel is running: close it first (right-click the panel > Exit), then build again.'
}
if (-not (Test-Path (Join-Path $repo 'web\assets'))) {
    Write-Warning 'web\assets is missing: run scripts\fetch-assets.ps1 first for fonts and logos.'
}

dotnet publish (Join-Path $repo 'src\PaxPanel\PaxPanel.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $out 'PaxPanel.exe') $exe -Force
"Built $exe"

if (-not $NoShortcut) {
    $link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'paxpanel.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $repo
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'paxpanel sensor panel'
    $shortcut.Save()
    "Shortcut: $link"
}
