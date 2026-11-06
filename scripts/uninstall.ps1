$ErrorActionPreference = "Stop"

$dest = Join-Path $env:LOCALAPPDATA "Programs\AirPlaySender"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "AirPlay Sender.lnk"

Stop-Process -Name "AirPlaySender" -ErrorAction SilentlyContinue

if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Get-NetFirewallRule -DisplayName "AirPlay Sender" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}

Write-Host "AirPlay Sender uninstalled." -ForegroundColor Green
