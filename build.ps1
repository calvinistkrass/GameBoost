# Baut GameBoost als einzelne, eigenständige .exe (kein installiertes .NET nötig).
# Aufruf:  powershell -ExecutionPolicy Bypass -File build.ps1
# Ergebnis: publish\GameBoost.exe und publish\GameBoost-<Version>-win-x64.zip
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "publish"

# dotnet finden: zuerst PATH (mit SDK), sonst benutzerlokale Installation (%USERPROFILE%\.dotnet)
$dotnet = "dotnet"
$hasSdk = $false
try { $hasSdk = [bool](& dotnet --list-sdks 2>$null | Select-String "^8\.") } catch { }
if (-not $hasSdk) {
    $local = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $local) { $dotnet = $local }
    else { throw ".NET 8 SDK nicht gefunden. Installation: https://dotnet.microsoft.com/download/dotnet/8.0" }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

if (-not $SkipTests) {
    Write-Host "==> Tests" -ForegroundColor Cyan
    & $dotnet test (Join-Path $root "GameBoost.sln") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen" }
}

Write-Host "==> Publish ($Runtime, self-contained, single file)" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& $dotnet publish (Join-Path $root "src\GameBoost\GameBoost.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish fehlgeschlagen" }

Copy-Item (Join-Path $root "docs\README-Download.txt") (Join-Path $out "README.txt") -Force

[xml]$proj = Get-Content (Join-Path $root "src\GameBoost\GameBoost.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$zip = Join-Path $out "GameBoost-$version-$Runtime.zip"
Compress-Archive -Path (Join-Path $out "GameBoost.exe"), (Join-Path $out "README.txt") -DestinationPath $zip -Force

$exe = Get-Item (Join-Path $out "GameBoost.exe")
Write-Host ""
Write-Host ("Fertig: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
Write-Host ("ZIP:    {0}" -f $zip) -ForegroundColor Green
