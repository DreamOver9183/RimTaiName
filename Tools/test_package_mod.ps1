<#
.SYNOPSIS
Self-test for package_mod.ps1: a clean tree must package (also from a "[bracketed]" folder and with
relative paths), and a fake key in any supported shape, encoding or hidden file must abort the
packaging and leave no output behind.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test_package_mod.ps1
#>
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'package_mod.ps1'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rtn_pkg_test_' + [guid]::NewGuid().ToString('N'))

# Fake keys are assembled at runtime so no key-shaped literal is stored in the repository.
$fakeAiza = 'AI' + 'za' + ('x' * 31) + '90ab'
$fakeAq = 'AQ' + '.' + 'Ab' + ('Z' * 48)

function Write-Text([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text)
}

function New-Tree([string]$name) {
    $root = Join-Path $work $name
    foreach ($dir in @('About', '1.6/Assemblies', 'Languages/English/Keyed')) {
        [System.IO.Directory]::CreateDirectory((Join-Path $root $dir)) | Out-Null
    }
    Write-Text (Join-Path $root 'About/About.xml') '<ModMetaData />'
    Write-Text (Join-Path $root 'LoadFolders.xml') '<loadFolders />'
    Write-Text (Join-Path $root 'Languages/English/Keyed/Test.xml') '<LanguageData />'
    [System.IO.File]::WriteAllBytes((Join-Path $root '1.6/Assemblies/RimTaiName.dll'), [byte[]](1..64))
    return $root
}

# Every entry must sit under "RimTaiName/" and use "/" separators (ZIP spec), and the DLL must be there.
function Test-ZipLayout([string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        $layoutOk = @($names | Where-Object { $_.Contains('\') -or -not $_.StartsWith('RimTaiName/') }).Count -eq 0
        return $layoutOk -and ($names -contains 'RimTaiName/1.6/Assemblies/RimTaiName.dll')
    }
    finally {
        $archive.Dispose()
    }
}

$cases = @(
    @{ Name = 'clean'; Expect = 0; Setup = { param($r) } },
    @{ Name = 'bracket folder'; Folder = '[Modding] tree'; Expect = 0; Setup = { param($r) } },
    @{ Name = 'relative paths'; Relative = $true; Expect = 0; Setup = { param($r) } },
    @{ Name = 'aiza in xml'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'Languages/English/Keyed/Leak.xml') "<k>$fakeAiza</k>" } },
    @{ Name = 'aq in xml'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'About/About.xml') "<d>key $fakeAq.</d>" } },
    @{ Name = 'aq utf16 in dll'; Expect = 1; Setup = { param($r) [System.IO.File]::WriteAllBytes((Join-Path $r '1.6/Assemblies/RimTaiName.dll'), [System.Text.Encoding]::Unicode.GetBytes("xx$fakeAq")) } },
    @{ Name = 'aiza utf16 odd offset'; Expect = 1; Setup = { param($r) [System.IO.File]::WriteAllBytes((Join-Path $r '1.6/Assemblies/RimTaiName.dll'), [byte[]]@(0) + [System.Text.Encoding]::Unicode.GetBytes($fakeAiza)) } },
    @{ Name = 'hidden file with key'; Expect = 1; Setup = {
        param($r)
        $hidden = Join-Path $r 'Languages/English/Keyed/.backup.xml'
        Write-Text $hidden "<k>$fakeAq</k>"
        [System.IO.File]::SetAttributes($hidden, [System.IO.FileAttributes]::Hidden)
    } },
    @{ Name = 'key in bracket folder'; Folder = '[Modding] leak'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'Languages/English/Keyed/Leak.xml') "<k>$fakeAiza</k>" } },
    @{ Name = 'key file name'; Expect = 1; Setup = { param($r) Write-Text (Join-Path $r 'About/RimTaiName_KEY_01.txt') 'x' } },
    @{ Name = 'missing dll'; Expect = 1; Setup = { param($r) Remove-Item -LiteralPath (Join-Path $r '1.6/Assemblies/RimTaiName.dll') } }
)

$failed = 0
try {
    foreach ($case in $cases) {
        $folder = if ($case.Folder) { $case.Folder } else { $case.Name -replace '\W', '_' }
        $root = New-Tree $folder
        & $case.Setup $root
        $out = Join-Path $root 'dist'
        # PowerShell 5.1 turns a child's stderr into error records; with 'Stop' they would abort this script.
        $ErrorActionPreference = 'Continue'
        if ($case.Relative) {
            Push-Location -LiteralPath $root
            & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Root '.' -OutDir 'dist' -NoBuild *> $null
            $code = $LASTEXITCODE
            Pop-Location
        } else {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Root $root -OutDir $out -NoBuild *> $null
            $code = $LASTEXITCODE
        }
        $ErrorActionPreference = 'Stop'
        $leftover = Test-Path -LiteralPath (Join-Path $out 'RimTaiName')
        $zipped = Test-Path -LiteralPath (Join-Path $out 'RimTaiName.zip')
        $outputOk = if ($case.Expect -eq 0) { $leftover -and $zipped -and (Test-ZipLayout (Join-Path $out 'RimTaiName.zip')) } else { -not $leftover -and -not $zipped }
        $ok = ($code -eq $case.Expect) -and $outputOk
        if (-not $ok) { $failed++ }
        Write-Host ("{0,-24} exit={1} output={2} zip={3} -> {4}" -f $case.Name, $code, $leftover, $zipped, $(if ($ok) { 'ok' } else { 'FAIL' }))
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
