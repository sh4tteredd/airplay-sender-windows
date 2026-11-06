param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot "build.ps1")

    $out = Join-Path $root "artifacts\publish\$Runtime"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }

    dotnet publish src/AirPlaySender.App/AirPlaySender.App.csproj `
        -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -o $out

    $native = Join-Path $root "build\native\bin\Release\AirPlayCore.dll"
    Copy-Item $native (Join-Path $out "AirPlayCore.dll") -Force

    foreach ($file in @("README.md", "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md")) {
        Copy-Item (Join-Path $root $file) (Join-Path $out $file) -Force
    }

    $licenseDir = Join-Path $out "licenses\airplay2-sender-cpp"
    New-Item -ItemType Directory -Force $licenseDir | Out-Null
    Copy-Item (Join-Path $root "third_party\airplay2-sender-cpp\LICENSE") $licenseDir -Force
    Copy-Item (Join-Path $root "third_party\airplay2-sender-cpp\NOTICE") $licenseDir -Force
    Copy-Item (Join-Path $root "third_party\airplay2-sender-cpp\licenses\THIRD-PARTY-NOTICES.txt") $licenseDir -Force

    $zip = Join-Path $root "artifacts\AirPlaySender-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

    Write-Host "Published: $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}
