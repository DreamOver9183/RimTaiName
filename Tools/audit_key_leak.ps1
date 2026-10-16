<#
.SYNOPSIS
Checks a RimWorld user-data folder for leaked Gemini API keys (v3 section 9 audit).

.DESCRIPTION
Scans Player.log, Player-prev.log and every file under Config (sub-folders and hidden files included)
for "AIza..." and "AQ...." key shapes, decoded as Latin-1 and as UTF-16 at both byte alignments. Files
are streamed in chunks with an overlap, so huge logs do not exhaust memory and a key on a chunk border is
still found. Only file names are reported, never the matched text. Self-contained so it can be copied to
a test PC on its own; keep the patterns in sync with Tools/package_mod.ps1 and Core/KeyRedactor.cs.

Exit codes: 0 = clean, 1 = key found, 2 = data folder not found, 3 = some files could not be read.

.PARAMETER DataDir
RimWorld user-data folder. Defaults to the standard location under LocalLow.

.PARAMETER ExtraPath
Additional files or folders to scan, e.g. the installed mod folder.

.PARAMETER ChunkBytes
Read size per chunk. Only lowered by the self-test to exercise chunk borders.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File audit_key_leak.ps1
#>
param(
    [string]$DataDir = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'),
    [string[]]$ExtraPath = @(),
    [ValidateRange(64, 268435456)]
    [int]$ChunkBytes = 4MB
)

$ErrorActionPreference = 'Stop'
$keyPatterns = @('AIza[0-9A-Za-z_\-]{35}', 'AQ\.[0-9A-Za-z_.\-]{20,}')
# Longer than any key in UTF-16 (2 bytes per character), so a key split by a chunk border is seen whole.
$overlapBytes = 512
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$unicode = [System.Text.Encoding]::Unicode

function Test-KeyBytes([byte[]]$buffer, [int]$length) {
    $texts = @($latin1.GetString($buffer, 0, $length), $unicode.GetString($buffer, 0, $length))
    if ($length -gt 1) { $texts += $unicode.GetString($buffer, 1, $length - 1) }
    foreach ($text in $texts) {
        foreach ($pattern in $keyPatterns) {
            if ([regex]::IsMatch($text, $pattern)) { return $true }
        }
    }
    return $false
}

# Returns $true when the file contains a key shape. Throws when the file cannot be read.
function Test-KeyFile([string]$path) {
    # Shared read (and delete) access: a running game keeps Player.log open and may rotate it.
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $buffer = New-Object byte[] ($ChunkBytes + $overlapBytes)
        $carry = 0
        while ($true) {
            $read = $stream.Read($buffer, $carry, $ChunkBytes)
            if ($read -le 0) { return $false }
            $length = $carry + $read
            if (Test-KeyBytes $buffer $length) { return $true }
            $carry = [Math]::Min($overlapBytes, $length)
            [System.Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry)
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($DataDir) -or -not (Test-Path -LiteralPath $DataDir -PathType Container)) {
    Write-Host "RimWorld data folder not found: '$DataDir'"
    Write-Host 'Run this in PowerShell (not cmd), or pass -DataDir <folder>.'
    exit 2
}

$files = New-Object System.Collections.Generic.List[string]
$unreadable = 0
foreach ($name in @('Player.log', 'Player-prev.log')) {
    $log = Join-Path $DataDir $name
    if (Test-Path -LiteralPath $log -PathType Leaf) { $files.Add($log) }
}
$roots = @(Join-Path $DataDir 'Config') + @($ExtraPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
foreach ($root in $roots) {
    if (Test-Path -LiteralPath $root -PathType Leaf) {
        $files.Add($root)
    }
    elseif (Test-Path -LiteralPath $root -PathType Container) {
        try {
            foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) { $files.Add($file.FullName) }
        }
        catch {
            $unreadable++
            Write-Host "cannot list folder: $root ($($_.Exception.Message))"
        }
    }
    else {
        Write-Host "skip (missing): $root"
    }
}

$hits = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    try {
        if (Test-KeyFile $file) { $hits.Add($file) }
    }
    catch {
        $unreadable++
        Write-Host "cannot read: $file ($($_.Exception.GetType().Name))"
    }
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
