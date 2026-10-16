<#
.SYNOPSIS
Self-test for package_mod.ps1: a clean tree must package, and a fake key in any supported shape or
encoding must abort the packaging and leave no output behind.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test_package_mod.ps1
#>
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'package_mod.ps1'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rtn_pkg_test_' + [guid]::NewGuid().ToString('N'))

# Fake keys are assembled at runtime so no key-shaped literal is stored in the repository.
$fakeAiza = 'AI' + 'za' + ('x' * 31) + '90ab'
$fakeAq = 'AQ' + '.' + 'Ab' + ('Z' * 48)

function New-Tree([string]$name) {
    $root = Join-Path $work $name
    foreach ($dir in @('About', '1.6/Assemblies', 'Languages/English/Keyed')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root $dir) | Out-Null
    }
    Set-Content -Path (Join-Path $root 'About/About.xml') -Value '<ModMetaData />'
    Set-Content -Path (Join-Path $root 'LoadFolders.xml') -Value '<loadFolders />'
    Set-Content -Path (Join-Path $root 'Languages/English/Keyed/Test.xml') -Value '<LanguageData />'
    [System.IO.File]::WriteAllBytes((Join-Path $root '1.6/Assemblies/RimTaiName.dll'), [byte[]](1..64))
    return $root
}

$cases = @(
    @{ Name = 'clean'; Expect = 0; Setup = { param($r) } },
    @{ Name = 'aiza in xml'; Expect = 1; Setup = { param($r) Set-Content -Path (Join-Path $r 'Languages/English/Keyed/Leak.xml') -Value "<k>$fakeAiza</k>" } },
    @{ Name = 'aq in xml'; Expect = 1; Setup = { param($r) Set-Content -Path (Join-Path $r 'About/About.xml') -Value "<d>key $fakeAq.</d>" } },
    @{ Name = 'aq utf16 in dll'; Expect = 1; Setup = { param($r) [System.IO.File]::WriteAllBytes((Join-Path $r '1.6/Assemblies/RimTaiName.dll'), [System.Text.Encoding]::Unicode.GetBytes("xx$fakeAq")) } },
    @{ Name = 'aiza utf16 odd offset'; Expect = 1; Setup = { param($r) [System.IO.File]::WriteAllBytes((Join-Path $r '1.6/Assemblies/RimTaiName.dll'), [byte[]]@(0) + [System.Text.Encoding]::Unicode.GetBytes($fakeAiza)) } },
    @{ Name = 'key file name'; Expect = 1; Setup = { param($r) Set-Content -Path (Join-Path $r 'About/RimTaiName_KEY_01.txt') -Value 'x' } },
    @{ Name = 'missing dll'; Expect = 1; Setup = { param($r) Remove-Item (Join-Path $r '1.6/Assemblies/RimTaiName.dll') } }
)

$failed = 0
try {
    foreach ($case in $cases) {
        $root = New-Tree ($case.Name -replace '\W', '_')
        & $case.Setup $root
        $out = Join-Path $root 'dist'
        # PowerShell 5.1 turns a child's stderr into error records; with 'Stop' they would abort this script.
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Root $root -OutDir $out -NoBuild *> $null
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $leftover = Test-Path (Join-Path $out 'RimTaiName')
        $zipped = Test-Path (Join-Path $out 'RimTaiName.zip')
        $ok = ($code -eq $case.Expect) -and ($(if ($case.Expect -eq 0) { $leftover -and $zipped } else { -not $leftover -and -not $zipped }))
        if (-not $ok) { $failed++ }
        Write-Host ("{0,-24} exit={1} output={2} zip={3} -> {4}" -f $case.Name, $code, $leftover, $zipped, $(if ($ok) { 'ok' } else { 'FAIL' }))
    }
}
finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
}

if ($failed -gt 0) {
    Write-Host "$failed case(s) failed"
    exit 1
}
Write-Host "all $($cases.Count) cases passed"
exit 0
