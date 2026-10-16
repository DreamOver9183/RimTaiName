<#
.SYNOPSIS
Self-test for audit_key_leak.ps1 using throw-away RimWorld data folders.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test_audit_key_leak.ps1
#>
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'audit_key_leak.ps1'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rtn_audit_test_' + [guid]::NewGuid().ToString('N'))

# Fake keys are assembled at runtime so no key-shaped literal is stored in the repository.
$fakeAiza = 'AI' + 'za' + ('x' * 31) + '90ab'
$fakeAq = 'AQ' + '.' + 'Ab' + ('Z' * 48)

function Write-Text([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text)
}

function New-DataDir([string]$name) {
    $root = Join-Path $work $name
    [System.IO.Directory]::CreateDirectory((Join-Path $root 'Config\SomeMod')) | Out-Null
    Write-Text (Join-Path $root 'Player.log') "[RimTaiName] loaded`n[RimTaiName] connection test: None (HTTP 200)"
    Write-Text (Join-Path $root 'Config\Mod_RimTaiName_RimTaiNameMod.xml') '<SettingsBlock><modelId>gemini-3.5-flash-lite</modelId></SettingsBlock>'
    Write-Text (Join-Path $root 'Config\SomeMod\settings.xml') '<x />'
    [System.IO.File]::WriteAllBytes((Join-Path $root 'Config\RimTaiName_ApiKey.dat'), [byte[]](1..200))
    return $root
}

$cases = @(
    @{ Name = 'clean with sub-folders'; Expect = 0; Setup = { param($r) } },
    @{ Name = 'bracket folder'; Folder = '[Data] dir'; Expect = 0; Setup = { param($r) } },
    @{ Name = 'aq in Player.log'; Expect = 1; Setup = { param($r) Add-Content -LiteralPath (Join-Path $r 'Player.log') -Value "header $fakeAq." } },
    @{ Name = 'aiza in Player-prev.log'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'Player-prev.log') "k=$fakeAiza" } },
    @{ Name = 'key in mod settings xml'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'Config\Mod_RimTaiName_RimTaiNameMod.xml') "<k>$fakeAq</k>" } },
    @{ Name = 'key in nested config'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'Config\SomeMod\settings.xml') "<k>$fakeAiza</k>" } },
    @{ Name = 'utf16 hidden file'; Expect = 1; Setup = {
        param($r)
        $hidden = Join-Path $r 'Config\cache.bin'
        [System.IO.File]::WriteAllBytes($hidden, [System.Text.Encoding]::Unicode.GetBytes("xx$fakeAq"))
        [System.IO.File]::SetAttributes($hidden, [System.IO.FileAttributes]::Hidden)
    } },
    @{ Name = 'utf16 odd offset'; Expect = 1; Setup = {
        param($r)
        [System.IO.File]::WriteAllBytes((Join-Path $r 'Config\odd.bin'), [byte[]]@(0) + [System.Text.Encoding]::Unicode.GetBytes($fakeAiza))
    } },
    @{ Name = 'key across chunk border'; Expect = 1; Arguments = @('-ChunkBytes', '1024'); Setup = {
        param($r)
        # The key starts 20 bytes before the first 1024-byte chunk ends.
        Write-Text (Join-Path $r 'Player.log') (('.' * 1004) + $fakeAq + ('.' * 3000))
    } },
    @{ Name = 'clean multi-chunk log'; Expect = 0; Arguments = @('-ChunkBytes', '1024'); Setup = {
        param($r)
        Write-Text (Join-Path $r 'Player.log') ('[RimTaiName] line' * 2000)
    } },
    @{ Name = 'missing data folder'; Missing = $true; Expect = 2; Setup = { param($r) } },
    # PowerShell 5.1 drops '' when calling a native program, so a blank string stands in for "empty".
    @{ Name = 'blank data folder arg'; Blank = $true; Expect = 2; Setup = { param($r) } },
    @{ Name = 'locked log'; Lock = 'Player.log'; Expect = 3; Setup = { param($r) } }
)

$failed = 0
try {
    foreach ($case in $cases) {
        $folder = if ($case.Folder) { $case.Folder } else { $case.Name -replace '\W', '_' }
        $root = if ($case.Missing) { Join-Path $work 'does_not_exist' } elseif ($case.Blank) { '   ' } else { New-DataDir $folder }
        & $case.Setup $root
        $extra = if ($case.Arguments) { $case.Arguments } else { @() }
        $lock = $null
        try {
            if ($case.Lock) {
                $lock = New-Object System.IO.FileStream((Join-Path $root $case.Lock), [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            }
            # PowerShell 5.1 turns a child's stderr into error records; with 'Stop' they would abort this script.
            $ErrorActionPreference = 'Continue'
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -DataDir $root @extra 2>&1 | Out-String
            $code = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = 'Stop'
            if ($lock) { $lock.Dispose() }
        }
        # The report may name files but must never echo the key itself.
        $echoed = $output.Contains($fakeAiza) -or $output.Contains($fakeAq)
        $ok = ($code -eq $case.Expect) -and -not $echoed
        if (-not $ok) { $failed++ }
        Write-Host ("{0,-26} exit={1} echoedKey={2} -> {3}" -f $case.Name, $code, $echoed, $(if ($ok) { 'ok' } else { 'FAIL' }))
    }
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

if ($failed -gt 0) {
    Write-Host "$failed case(s) failed"
    exit 1
}
Write-Host "all $($cases.Count) cases passed"
exit 0
