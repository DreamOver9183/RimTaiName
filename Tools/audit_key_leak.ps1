<#
.SYNOPSIS
Checks a RimWorld user-data folder for leaked Gemini API keys (v3 section 9 audit).

.DESCRIPTION
Scans Player.log, Player-prev.log and every file under Config (sub-folders and hidden files included)
for "AIza..." and "AQ...." key shapes, decoded as Latin-1 and as UTF-16 at both byte alignments. Only
file names are reported, never the matched text. Self-contained so it can be copied to a test PC on its
own; keep the patterns in sync with Tools/package_mod.ps1 and Core/KeyRedactor.cs.

Exit codes: 0 = clean, 1 = key found, 2 = data folder not found, 3 = some files could not be read.

.PARAMETER DataDir
RimWorld user-data folder. Defaults to the standard location under LocalLow.

.PARAMETER ExtraPath
Additional files or folders to scan, e.g. the installed mod folder.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File audit_key_leak.ps1
#>
param(
    [string]$DataDir = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'),
    [string[]]$ExtraPath = @()
)

$ErrorActionPreference = 'Stop'
$keyPatterns = @('AIza[0-9A-Za-z_\-]{35}', 'AQ\.[0-9A-Za-z_.\-]{20,}')

if (-not (Test-Path -LiteralPath $DataDir -PathType Container)) {
    Write-Host "RimWorld data folder not found: $DataDir"
    Write-Host 'Run this in PowerShell (not cmd), or pass -DataDir <folder>.'
    exit 2
}

$files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($name in @('Player.log', 'Player-prev.log')) {
    $log = Join-Path $DataDir $name
    if (Test-Path -LiteralPath $log -PathType Leaf) { $files.Add((Get-Item -LiteralPath $log -Force)) }
}
$roots = @(Join-Path $DataDir 'Config') + $ExtraPath
foreach ($root in $roots) {
    if (Test-Path -LiteralPath $root -PathType Leaf) {
        $files.Add((Get-Item -LiteralPath $root -Force))
    }
    elseif (Test-Path -LiteralPath $root -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) { $files.Add($file) }
    }
    else {
        Write-Host "skip (missing): $root"
    }
}

$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$unicode = [System.Text.Encoding]::Unicode
$hits = New-Object System.Collections.Generic.List[string]
$unreadable = 0
foreach ($file in $files) {
    try {
        # Player.log is held open by a running game; open with sharing so it can still be read.
        $stream = New-Object System.IO.FileStream($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $buffer = New-Object byte[] ([int]$stream.Length)
            $read = 0
            while ($read -lt $buffer.Length) {
                $n = $stream.Read($buffer, $read, $buffer.Length - $read)
                if ($n -le 0) { break }
                $read += $n
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        $unreadable++
        Write-Host "cannot read: $($file.FullName)"
        continue
    }
    $texts = @($latin1.GetString($buffer), $unicode.GetString($buffer))
    if ($buffer.Length -gt 1) { $texts += $unicode.GetString($buffer, 1, $buffer.Length - 1) }
    $found = $false
    foreach ($text in $texts) {
        foreach ($pattern in $keyPatterns) {
            if ([regex]::IsMatch($text, $pattern)) { $found = $true; break }
        }
        if ($found) { break }
    }
    if ($found) { $hits.Add($file.FullName) }
}

$keyFile = Join-Path $DataDir 'Config\RimTaiName_ApiKey.dat'
Write-Host ("key file (DPAPI): {0}" -f $(if (Test-Path -LiteralPath $keyFile) { 'present' } else { 'absent' }))
Write-Host ("scanned {0} files, unreadable {1}, files with a key pattern: {2}" -f $files.Count, $unreadable, $hits.Count)
if ($hits.Count -gt 0) {
    foreach ($hit in $hits) { Write-Host "  KEY FOUND IN: $hit" }
    exit 1
}
if ($unreadable -gt 0) {
    Write-Host 'INCOMPLETE: close the game and run again'
    exit 3
}
Write-Host 'OK: no API key found'
exit 0
