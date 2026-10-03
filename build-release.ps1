#Requires -Version 5.1
<#
.SYNOPSIS
    Build locale degli artefatti di release di GangDrogaCity Launcher (tutte le piattaforme).
.DESCRIPTION
    Richiede solo il .NET 8 SDK (niente Visual Studio). Produce nella cartella release\:
      GangDrogaCity.exe                  Windows x64
      GangDrogaCity.7z                   exe compresso (aggiornamento launcher <= 2.2.5), se 7z e' disponibile
      GangDrogaCity-windows-x64.zip      exe in zip (aggiornamento launcher >= 2.2.6)
      GangDrogaCity-linux-x64.tar.gz     Linux x64
      GangDrogaCity-linux-arm64.tar.gz   Linux arm64
      GangDrogaCity-macos-x64.zip        macOS Intel (bundle .app)
      GangDrogaCity-macos-arm64.zip      macOS Apple Silicon (bundle .app)
    La release ufficiale viene prodotta dal workflow GitHub Actions (.github/workflows/release.yml)
    al push di un tag: questo script serve per build e test locali.
.PARAMETER Version
    Versione (es. "2.2.6.0"). Se omessa viene letta dal csproj.
.PARAMETER Rids
    Runtime identifier da pubblicare. Default: tutti.
.EXAMPLE
    .\build-release.ps1
    .\build-release.ps1 -Rids win-x64
#>
param(
    [string]$Version,
    [string[]]$Rids = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$appProject = Join-Path $root "src\GangDrogaCity.App\GangDrogaCity.App.csproj"
$appDir = Join-Path $root "src\GangDrogaCity.App"
$outRoot = Join-Path $root "out"
$releaseDir = Join-Path $root "release"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK non trovato. Installa il .NET 8 SDK: https://dotnet.microsoft.com/download"
    exit 1
}

if (-not $Version) {
    $csproj = Get-Content $appProject -Raw
    if ($csproj -match "<Version>([^<]+)</Version>") { $Version = $Matches[1] }
}
if (-not $Version) { Write-Error "Impossibile determinare la versione. Specifica -Version."; exit 1 }
Write-Host "[OK] Versione: $Version" -ForegroundColor Green

if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
if (Test-Path $outRoot) { Remove-Item $outRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

foreach ($rid in $Rids) {
    Write-Host "`n=== Publish $rid ===" -ForegroundColor Cyan
    $outDir = Join-Path $outRoot $rid
    & dotnet publish $appProject -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:DebugSymbols=false -o $outDir -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish $rid fallita."; exit 1 }
}

function Find-SevenZip {
    foreach ($c in @("7z", "7za", "7zz", "7zr")) {
        $cmd = Get-Command $c -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    foreach ($p in @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

# --- Windows ---
if ($Rids -contains "win-x64") {
    Copy-Item (Join-Path $outRoot "win-x64\GangDrogaCity.exe") (Join-Path $releaseDir "GangDrogaCity.exe") -Force
    Compress-Archive -Path (Join-Path $releaseDir "GangDrogaCity.exe") -DestinationPath (Join-Path $releaseDir "GangDrogaCity-windows-x64.zip") -CompressionLevel Optimal -Force
    $sevenZip = Find-SevenZip
    if ($sevenZip) {
        Push-Location $releaseDir
        & $sevenZip a -mx=9 "GangDrogaCity.7z" "GangDrogaCity.exe" | Out-Null
        Pop-Location
    } else {
        Write-Warning "7-Zip non trovato: GangDrogaCity.7z non creato (serve solo ai launcher <= 2.2.5 per aggiornarsi)."
    }
}

# --- Linux ---
foreach ($arch in @("x64", "arm64")) {
    $rid = "linux-$arch"
    if ($Rids -contains $rid) {
        $src = Join-Path $outRoot $rid
        & tar -czf (Join-Path $releaseDir "GangDrogaCity-linux-$arch.tar.gz") -C $src GangDrogaCity
        if ($LASTEXITCODE -ne 0) { Write-Error "tar $rid fallito."; exit 1 }
    }
}

# --- macOS: bundle .app ---
foreach ($arch in @("x64", "arm64")) {
    $rid = "osx-$arch"
    if ($Rids -contains $rid) {
        $bundleRoot = Join-Path $outRoot "bundle-$arch"
        $bundle = Join-Path $bundleRoot "GangDrogaCity.app"
        New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents\MacOS") | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents\Resources") | Out-Null
        Copy-Item (Join-Path $outRoot "$rid\GangDrogaCity") (Join-Path $bundle "Contents\MacOS\GangDrogaCity") -Force
        Copy-Item (Join-Path $appDir "macos\launcher.icns") (Join-Path $bundle "Contents\Resources\launcher.icns") -Force
        (Get-Content (Join-Path $appDir "macos\Info.plist") -Raw).Replace("APP_VERSION", $Version) | Set-Content (Join-Path $bundle "Contents\Info.plist") -NoNewline
        Set-Content (Join-Path $bundle "Contents\PkgInfo") "APPL????" -NoNewline
        # Lo zip deve conservare i permessi di esecuzione: su Windows Compress-Archive non li conserva,
        # quindi il bundle prodotto da Windows richiede "chmod +x" dopo l'estrazione. La release ufficiale
        # viene creata su Linux dal workflow, dove "zip -y" conserva i permessi.
        $zipCmd = Get-Command zip -ErrorAction SilentlyContinue
        if ($zipCmd) {
            Push-Location $bundleRoot
            & zip -q -9 -r -y (Join-Path $releaseDir "GangDrogaCity-macos-$arch.zip") "GangDrogaCity.app"
            Pop-Location
        } else {
            Compress-Archive -Path $bundle -DestinationPath (Join-Path $releaseDir "GangDrogaCity-macos-$arch.zip") -CompressionLevel Optimal -Force
            Write-Warning "zip non trovato: usato Compress-Archive (il bundle macOS non conserva i permessi di esecuzione)."
        }
    }
}

Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host " RELEASE $Version - Artefatti pronti" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Get-ChildItem $releaseDir -File | ForEach-Object {
    Write-Host ("  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
