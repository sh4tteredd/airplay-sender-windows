param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

& (Join-Path $PSScriptRoot "publish.ps1") -Runtime $Runtime

$source = Join-Path $root "artifacts\publish\$Runtime"
$dest = Join-Path $env:LOCALAPPDATA "Programs\AirPlaySender"
$exe = Join-Path $dest "AirPlaySender.exe"

Stop-Process -Name "AirPlaySender" -ErrorAction SilentlyContinue

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $source "*") $dest -Recurse -Force

$programs = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programs "AirPlay Sender.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.Description = "Stream Windows audio to AirPlay / RAOP receivers"
$shortcut.Save()

Write-Host "Installed to $dest" -ForegroundColor Green
Write-Host "Start Menu shortcut: $shortcutPath" -ForegroundColor Green

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    & (Join-Path $PSScriptRoot "install-firewall.ps1") -ExePath $exe
} else {
    Write-Host "Run scripts\install-firewall.ps1 from an elevated prompt to allow inbound RAOP UDP." -ForegroundColor Yellow
}
