<#
.SYNOPSIS
Builds RimTaiName and copies the distributable files to dist/RimTaiName (plus dist/RimTaiName.zip).

.DESCRIPTION
Only About, LoadFolders.xml, 1.6, Languages, Dictionary and Prompts are copied; missing ones are
skipped. Every copied file (hidden ones included) is scanned for Gemini API keys ("AIza..." and
"AQ...." shapes, decoded as Latin-1 and as UTF-16 at both byte alignments, so string literals inside
the DLL are covered too). If a key is found or anything fails, the output is deleted and the script
exits with code 1. The matched text itself is never printed. Paths are used literally (folders such as
"[Modding]" work). Compatible with Windows PowerShell 5.1.

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

function Remove-Output {
    foreach ($path in @($target, $zip)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

# Resolve against the current directory so relative arguments behave and prefixes line up with FullName.
$cwd = (Get-Location).ProviderPath
$Root = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($cwd, $Root))
if (-not $OutDir) { $OutDir = Join-Path $Root 'dist' }
$OutDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($cwd, $OutDir))
$items = @('About', 'LoadFolders.xml', '1.6', 'Languages', 'Dictionary', 'Prompts')
$keyPatterns = @('AIza[0-9A-Za-z_\-]{35}', 'AQ\.[0-9A-Za-z_.\-]{20,}')
$target = Join-Path $OutDir 'RimTaiName'
$zip = Join-Path $OutDir 'RimTaiName.zip'

# Remove stale output first, so a failed run never leaves an older package behind.
Remove-Output

if (-not $NoBuild) {
    $project = Join-Path $Root 'Source/RimTaiName/RimTaiName.csproj'
    & dotnet build $project -c Release -warnaserror -v q -nologo
    if ($LASTEXITCODE -ne 0) { Fail 'build failed' }
}

try {
    [System.IO.Directory]::CreateDirectory($target) | Out-Null
    foreach ($item in $items) {
        $source = Join-Path $Root $item
        if (-not (Test-Path -LiteralPath $source)) {
            Write-Host "skip (missing): $item"
            continue
        }
        Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath (Join-Path $target '1.6/Assemblies/RimTaiName.dll'))) {
        throw '1.6/Assemblies/RimTaiName.dll is missing; build the mod first'
    }

    $files = @(Get-ChildItem -LiteralPath $target -Recurse -File -Force)
    $prefix = $target.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $unicode = [System.Text.Encoding]::Unicode
    $leaks = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $relative = if ($file.FullName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($prefix.Length)
        } else {
            $file.Name
        }
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
        throw ("API key pattern found; output deleted. Files: " + ($leaks -join ', '))
    }

    # Compress-Archive (PowerShell 5.1) breaks on "[...]" in paths, and ZipFile.CreateFromDirectory on
    # .NET Framework writes "\" separators. Add each file with a "RimTaiName/..." entry name instead.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entry = 'RimTaiName/' + $file.FullName.Substring($prefix.Length).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
}
catch {
    Remove-Output
    Fail $_.Exception.Message
}

$size = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("packaged {0} files ({1:N0} bytes) -> {2}" -f $files.Count, $size, $target)
Write-Host "zip -> $zip"
exit 0
