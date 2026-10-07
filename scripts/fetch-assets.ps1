# Copies the GeForce fonts and logos (not in git) into web/assets.
param([string]$Source = 'D:\Drive\programs\aida64\icons')
$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot '..\web\assets'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $Source 'fonts\geforce_light.otf'), (Join-Path $Source 'fonts\geforce_bold.otf') $dest -Force
Get-ChildItem $Source -Filter '*LOGO.png' | Copy-Item -Destination $dest -Force
Get-ChildItem $dest | Select-Object Name, Length
