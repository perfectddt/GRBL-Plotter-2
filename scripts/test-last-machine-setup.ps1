param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-last-setup-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $tempRoot 'LastSetupTests.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'LastSetupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Last setup test compilation failed.' }
if (!$AssemblyPath) { $AssemblyPath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe' }
& $testExe $AssemblyPath (Join-Path $tempRoot 'state.json')
if ($LASTEXITCODE -ne 0) { throw 'Last setup checks failed.' }

