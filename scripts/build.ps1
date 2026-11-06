param(
    [string]$Generator = "Visual Studio 18 2026"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (Test-Path (Join-Path $root ".git")) {
        Write-Host "Updating submodules..." -ForegroundColor Cyan
        git submodule update --init --recursive
    }

    Write-Host "Configuring native core..." -ForegroundColor Cyan
    cmake -S . -B build/native -G $Generator -A x64

    Write-Host "Building AirPlayCore.dll..." -ForegroundColor Cyan
    cmake --build build/native --config Release --target AirPlayCore --parallel

    Write-Host "Restoring and building the .NET app..." -ForegroundColor Cyan
    dotnet restore AirPlaySender.slnx
    dotnet build AirPlaySender.slnx -c Release --no-restore

    Write-Host "Build complete." -ForegroundColor Green
    Write-Host "Run: src\AirPlaySender.App\bin\Release\net10.0-windows\AirPlaySender.exe"
}
finally {
    Pop-Location
}
