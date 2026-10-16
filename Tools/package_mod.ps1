<#
.SYNOPSIS
Builds RimTaiName and copies the distributable files to dist/RimTaiName (plus dist/RimTaiName.zip).

.DESCRIPTION
Only About, LoadFolders.xml, 1.6, Languages, Dictionary and Prompts are copied; missing ones are
skipped. Every copied file is scanned for Gemini API keys ("AIza..." and "AQ...." shapes, decoded as
Latin-1 and as UTF-16 at both byte alignments, so string literals inside the DLL are covered too).
If a key is found, the output is deleted and the script exits with code 1. The matched text itself is
never printed. Compatible with Windows PowerShell 5.1.

.PARAMETER Root
Project root. Defaults to the folder above this script.

.PARAMETER OutDir
Output folder. Defaults to <Root>/dist.

.PARAMETER NoBuild
Skip "dotnet build" and package the DLL that is already in 1.6/Assemblies.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/package_mod.ps1
#>
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutDir,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    [Console]::Error.WriteLine("package_mod: $message")
    exit 1
}

if (-not $OutDir) { $OutDir = Join-Path $Root 'dist' }
$items = @('About', 'LoadFolders.xml', '1.6', 'Languages', 'Dictionary', 'Prompts')
$keyPatterns = @('AIza[0-9A-Za-z_\-]{35}', 'AQ\.[0-9A-Za-z_.\-]{20,}')
$target = Join-Path $OutDir 'RimTaiName'
$zip = Join-Path $OutDir 'RimTaiName.zip'

if (-not $NoBuild) {
    $project = Join-Path $Root 'Source/RimTaiName/RimTaiName.csproj'
    & dotnet build $project -c Release -warnaserror -v q -nologo
    if ($LASTEXITCODE -ne 0) { Fail 'build failed' }
}

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null

foreach ($item in $items) {
    $source = Join-Path $Root $item
    if (-not (Test-Path $source)) {
        Write-Host "skip (missing): $item"
        continue
    }
    Copy-Item -Path $source -Destination $target -Recurse -Force
}

$dll = Join-Path $target '1.6/Assemblies/RimTaiName.dll'
if (-not (Test-Path $dll)) {
    Remove-Item $target -Recurse -Force
    Fail '1.6/Assemblies/RimTaiName.dll is missing; build the mod first'
}

$files = @(Get-ChildItem -Path $target -Recurse -File)
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$unicode = [System.Text.Encoding]::Unicode
$leaks = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $relative = $file.FullName.Substring($target.Length + 1)
    if ($file.Name -like '*_KEY_*' -or $file.Extension -eq '.tmp') {
        $leaks.Add("$relative (forbidden file name)")
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $texts = @($latin1.GetString($bytes), $unicode.GetString($bytes))
    if ($bytes.Length -gt 1) { $texts += $unicode.GetString($bytes, 1, $bytes.Length - 1) }
    $found = $false
    foreach ($text in $texts) {
        foreach ($pattern in $keyPatterns) {
            if ([regex]::IsMatch($text, $pattern)) { $found = $true; break }
        }
        if ($found) { break }
    }
    if ($found) { $leaks.Add($relative) }
}

if ($leaks.Count -gt 0) {
    Remove-Item $target -Recurse -Force
    Fail ("API key pattern found; output deleted. Files: " + ($leaks -join ', '))
}

Compress-Archive -Path $target -DestinationPath $zip -Force
$size = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("packaged {0} files ({1:N0} bytes) -> {2}" -f $files.Count, $size, $target)
Write-Host "zip -> $zip"
exit 0
