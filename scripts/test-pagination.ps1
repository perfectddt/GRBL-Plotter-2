$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-pagination-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files = @('Models\WritingStroke.cs','Services\BuiltinGlyphLibrary.cs','Services\TextLayoutService.cs') | ForEach-Object { Join-Path $repoRoot ('GRBL-Plotter\AutoWriter\' + $_) }
$testExe = Join-Path $tempRoot 'PaginationTests.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Runtime.Serialization.dll $files (Join-Path $PSScriptRoot 'PaginationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Pagination test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Pagination checks failed.' }

$uiExe = Join-Path $tempRoot 'PaginationUiTests.exe'
& $csc /nologo /target:exe /out:$uiExe /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'PaginationUiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Pagination UI compilation failed.' }
& $uiExe (Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe')
if ($LASTEXITCODE -ne 0) { throw 'Pagination UI checks failed.' }
