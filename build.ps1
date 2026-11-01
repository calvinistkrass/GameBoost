# Builds GameBoost as a single self-contained .exe (no installed .NET required).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
# Output: publish\GameBoost-<version>-win-x64.zip (portable) and publish\GameBoost-Setup-<version>.exe (installer)
# Needs the .NET 8 SDK and Inno Setup 6.
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "publish"

# Find dotnet: first PATH (with SDK), otherwise a per-user install (%USERPROFILE%\.dotnet)
$dotnet = "dotnet"
$hasSdk = $false
try { $hasSdk = [bool](& dotnet --list-sdks 2>$null | Select-String "^8\.") } catch { }
if (-not $hasSdk) {
    $local = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $local) { $dotnet = $local }
    else { throw ".NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

if (-not $SkipTests) {
    Write-Host "==> Tests" -ForegroundColor Cyan
    & $dotnet test (Join-Path $root "GameBoost.sln") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
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
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Copy-Item (Join-Path $root "docs\README-Download.txt") (Join-Path $out "README.txt") -Force

[xml]$proj = Get-Content (Join-Path $root "src\GameBoost\GameBoost.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$zip = Join-Path $out "GameBoost-$version-$Runtime.zip"
Compress-Archive -Path (Join-Path $out "GameBoost.exe"), (Join-Path $out "README.txt") -DestinationPath $zip -Force

Write-Host "==> Installer (Inno Setup)" -ForegroundColor Cyan
$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php" }
& $iscc /Q "/DMyAppVersion=$version" "/DSourceDir=$out" (Join-Path $root "installer\GameBoost.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
$setup = Join-Path $out "GameBoost-Setup-$version.exe"

$exe = Get-Item (Join-Path $out "GameBoost.exe")
Write-Host ""
Write-Host ("Done:  {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
Write-Host ("ZIP:   {0}" -f $zip) -ForegroundColor Green
Write-Host ("Setup: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
