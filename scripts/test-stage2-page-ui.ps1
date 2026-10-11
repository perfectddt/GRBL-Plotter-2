$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-page-ui-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $tempRoot 'PageUiTests.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'PageUiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
& $testExe (Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe')
if ($LASTEXITCODE -ne 0) { throw 'Page UI checks failed.' }
