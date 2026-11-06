$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot "build.ps1")
$exe = Join-Path $root "src\AirPlaySender.App\bin\Release\net10.0-windows\AirPlaySender.exe"
if (!(Test-Path $exe)) { throw "Build output not found: $exe" }
Start-Process -FilePath $exe
