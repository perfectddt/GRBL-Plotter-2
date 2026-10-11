$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-page-task-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$core = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\PageTaskService.cs'
$testExe = Join-Path $tempRoot 'PageTaskTests.exe'
& $csc /nologo /target:exe /out:$testExe $core (Join-Path $PSScriptRoot 'PageTaskTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Page task compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Page task checks failed.' }

if ($args.Count -gt 0) {
    $uiExe = Join-Path $tempRoot 'PageTaskUiTests.exe'
    & $csc /nologo /target:exe /out:$uiExe /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'PageTaskUiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Page task UI compilation failed.' }
    & $uiExe $args[0]
    if ($LASTEXITCODE -ne 0) { throw 'Page task UI checks failed.' }
}
