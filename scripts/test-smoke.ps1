$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot "build.ps1")
    dotnet run --project tests/AirPlaySender.Smoke/AirPlaySender.Smoke.csproj -c Release
}
finally {
    Pop-Location
}
