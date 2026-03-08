#Requires -Version 5.1
<#
.SYNOPSIS
    Build script per creare gli artefatti di release di GangDrogaCity Launcher.
.DESCRIPTION
    Produce 3 artefatti identici alla release GitHub:
      1. GangDrogaCity.exe         - EXE self-contained single-file (win-x86)
      2. GangDrogaCity.7z          - L'EXE compresso in 7z
      3. GangDrogaCity-full_{ver}-Windows_x64.7z - Pacchetto completo (debug + publish + runtime)
.PARAMETER Version
    Versione della release (es. "2.2.0"). Se omesso, viene letto da Settings.settings.
.EXAMPLE
    .\build-release.ps1
    .\build-release.ps1 -Version "2.3.0"
#>
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
$projectFile = Join-Path $projectDir "GangDrogaCity.vbproj"
$releaseDir = Join-Path $projectDir "release"
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

# --- Verifica prerequisiti ---
if (-not (Test-Path $msbuild)) {
    # Fallback: cerca MSBuild in altre edizioni
    $editions = @("Enterprise", "Professional", "BuildTools")
    foreach ($ed in $editions) {
        $alt = "C:\Program Files\Microsoft Visual Studio\2022\$ed\MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $alt) { $msbuild = $alt; break }
    }
    if (-not (Test-Path $msbuild)) {
        Write-Error "MSBuild non trovato. Installa Visual Studio 2022 o Build Tools."
        exit 1
    }
}
Write-Host "[OK] MSBuild: $msbuild" -ForegroundColor Green

# --- Leggi versione da Settings.settings se non specificata ---
if (-not $Version) {
    $settingsPath = Join-Path $projectDir "My Project\Settings.settings"
    if (Test-Path $settingsPath) {
        [xml]$settings = Get-Content $settingsPath
        $versionNode = $settings.SettingsFile.Settings.Setting | Where-Object { $_.Name -eq "version" }
        if ($versionNode) {
            $Version = $versionNode.Value.'#text'
            if (-not $Version) { $Version = $versionNode.Value }
        }
    }
    if (-not $Version) {
        Write-Error "Impossibile determinare la versione. Specifica -Version."
        exit 1
    }
}
Write-Host "[OK] Versione: $Version" -ForegroundColor Green

# --- Pulizia ---
Write-Host "`n=== Pulizia ===" -ForegroundColor Cyan
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$debugOut = Join-Path $projectDir "bin\Debug\net8.0-windows"
$publishSingleFile = Join-Path $releaseDir "_publish-singlefile"
$publishFull = Join-Path $releaseDir "_publish-full"
$stagingDir = Join-Path $releaseDir "_staging-full"

# --- Step 1: Build Debug (framework-dependent) ---
Write-Host "`n=== Step 1: Build Debug ===" -ForegroundColor Cyan
& $msbuild $projectFile /t:Rebuild /p:Configuration=Debug /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Build Debug fallita."; exit 1 }
Write-Host "[OK] Build Debug completata." -ForegroundColor Green

# --- Step 2: Publish self-contained single-file (win-x86) ---
Write-Host "`n=== Step 2: Publish SingleFile (win-x86) ===" -ForegroundColor Cyan
& $msbuild $projectFile /restore /t:Publish /p:Configuration=Release /p:RuntimeIdentifier=win-x86 /p:SelfContained=true /p:PublishSingleFile=true /p:PublishDir="$publishSingleFile\" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Publish SingleFile fallita."; exit 1 }
Write-Host "[OK] Publish SingleFile completata." -ForegroundColor Green

# --- Step 3: Publish self-contained full (win-x86, no single file) ---
Write-Host "`n=== Step 3: Publish Full (win-x86) ===" -ForegroundColor Cyan
& $msbuild $projectFile /restore /t:Publish /p:Configuration=Release /p:RuntimeIdentifier=win-x86 /p:SelfContained=true /p:PublishSingleFile=false /p:PublishDir="$publishFull\" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Error "Publish Full fallita."; exit 1 }
Write-Host "[OK] Publish Full completata." -ForegroundColor Green

# --- Step 4: Scarica 7zr.exe ---
Write-Host "`n=== Step 4: Download 7zr.exe ===" -ForegroundColor Cyan
$sevenZr = Join-Path $releaseDir "7zr.exe"
if (-not (Test-Path $sevenZr)) {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri "https://www.7-zip.org/a/7zr.exe" -OutFile $sevenZr -UseBasicParsing
    Write-Host "[OK] 7zr.exe scaricato." -ForegroundColor Green
} else {
    Write-Host "[OK] 7zr.exe gia presente." -ForegroundColor Green
}

# --- Step 5: Copia l'EXE standalone ---
Write-Host "`n=== Step 5: Copia EXE standalone ===" -ForegroundColor Cyan
$singleExe = Get-ChildItem -Path $publishSingleFile -Filter "GangDrogaCity.exe" -Recurse | Select-Object -First 1
if (-not $singleExe) {
    Write-Error "GangDrogaCity.exe non trovato nell'output SingleFile."
    exit 1
}
$finalExe = Join-Path $releaseDir "GangDrogaCity.exe"
Copy-Item $singleExe.FullName $finalExe -Force
$exeSizeMB = [math]::Round((Get-Item $finalExe).Length / 1MB, 1)
Write-Host "[OK] GangDrogaCity.exe ($exeSizeMB MB)" -ForegroundColor Green

# --- Step 6: Crea GangDrogaCity.7z (solo l'EXE) ---
Write-Host "`n=== Step 6: Crea GangDrogaCity.7z ===" -ForegroundColor Cyan
$archive7z = Join-Path $releaseDir "GangDrogaCity.7z"
& $sevenZr a -mx=9 -mmt=on $archive7z $finalExe
if ($LASTEXITCODE -ne 0) { Write-Error "Creazione GangDrogaCity.7z fallita."; exit 1 }
$archiveSizeMB = [math]::Round((Get-Item $archive7z).Length / 1MB, 1)
Write-Host "[OK] GangDrogaCity.7z ($archiveSizeMB MB)" -ForegroundColor Green

# --- Step 7: Assembla e crea il pacchetto full ---
Write-Host "`n=== Step 7: Crea pacchetto full ===" -ForegroundColor Cyan

# Crea struttura staging
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stagingDir "publish\win-x86") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stagingDir "win-x86") | Out-Null

# Root: build Debug output + 7zr.exe
$debugFiles = @(
    "GangDrogaCity.exe",
    "GangDrogaCity.dll",
    "GangDrogaCity.pdb",
    "GangDrogaCity.deps.json",
    "GangDrogaCity.dll.config",
    "GangDrogaCity.runtimeconfig.json",
    "AxInterop.SHDocVw.dll",
    "AxInterop.WMPLib.dll",
    "Interop.SHDocVw.dll",
    "Interop.WMPLib.dll",
    "Newtonsoft.Json.dll",
    "Octokit.dll"
)
foreach ($f in $debugFiles) {
    $src = Join-Path $debugOut $f
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $stagingDir $f) -Force
    } else {
        Write-Warning "File Debug mancante: $f"
    }
}
# 7zr.exe nella root
Copy-Item $sevenZr (Join-Path $stagingDir "7zr.exe") -Force

# publish\win-x86\: single-file publish output
Copy-Item "$publishSingleFile\*" (Join-Path $stagingDir "publish\win-x86") -Recurse -Force

# win-x86\: full self-contained publish
Copy-Item "$publishFull\*" (Join-Path $stagingDir "win-x86") -Recurse -Force

# Crea l'archivio full
$fullArchiveName = "GangDrogaCity-full_$Version-Windows_x64.7z"
$fullArchive = Join-Path $releaseDir $fullArchiveName
Push-Location $stagingDir
& $sevenZr a -mx=5 -ms=on -mmt=on $fullArchive *
Pop-Location
if ($LASTEXITCODE -ne 0) { Write-Error "Creazione pacchetto full fallita."; exit 1 }
$fullSizeMB = [math]::Round((Get-Item $fullArchive).Length / 1MB, 1)
Write-Host "[OK] $fullArchiveName ($fullSizeMB MB)" -ForegroundColor Green

# --- Pulizia cartelle temporanee ---
Write-Host "`n=== Pulizia temporanei ===" -ForegroundColor Cyan
Remove-Item $publishSingleFile -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishFull -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue

# --- Riepilogo ---
Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host " RELEASE $Version - Artefatti pronti" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""
Get-ChildItem $releaseDir -File | Where-Object { $_.Name -ne "7zr.exe" } | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($sizeMB MB)" -ForegroundColor White
}
Write-Host ""
Write-Host "Cartella output: $releaseDir" -ForegroundColor Cyan
