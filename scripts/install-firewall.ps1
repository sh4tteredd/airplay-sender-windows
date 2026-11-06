param(
    [string]$ExePath = ""
)

$ErrorActionPreference = "Stop"

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must run from an elevated PowerShell window."
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $root = Split-Path $PSScriptRoot -Parent
    $ExePath = Join-Path $root "src\AirPlaySender.App\bin\Release\net10.0-windows\AirPlaySender.exe"
}
$ExePath = (Resolve-Path $ExePath).Path

$ruleName = "AirPlay Sender"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

New-NetFirewallRule `
    -DisplayName $ruleName `
    -Direction Inbound `
    -Program $ExePath `
    -Protocol UDP `
    -Action Allow `
    -Profile Private, Domain `
    -Description "Allow AirPlay RAOP timing/control traffic to reach the sender."

Write-Host "Created inbound UDP firewall rule for $ExePath" -ForegroundColor Green
Write-Host "AP1 receivers may also send TCP control traffic; Windows already permits outbound TCP." -ForegroundColor DarkGray
