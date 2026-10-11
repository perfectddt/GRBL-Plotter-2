$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-page-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files = @('Models\MachineProfile.cs','Models\WritingStroke.cs','Services\BuiltinGlyphLibrary.cs','Services\TextLayoutService.cs','Services\WritingProgramService.cs','Services\WritingProjectService.cs','Services\RegionLayoutService.cs','Services\WritingEditorText.cs') | ForEach-Object { Join-Path $repoRoot ('GRBL-Plotter\AutoWriter\' + $_) }
$testExe = Join-Path $tempRoot 'PageProjectTests.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Runtime.Serialization.dll $files (Join-Path $PSScriptRoot 'PageProjectTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Page test compilation failed.' }
& $testExe $tempRoot
if ($LASTEXITCODE -ne 0) { throw 'Page/project checks failed.' }
