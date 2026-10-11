param([string]$ApplicationPath = "")
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-regions-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files = @('Models\MachineProfile.cs','Models\WritingStroke.cs','Services\BuiltinGlyphLibrary.cs','Services\TextLayoutService.cs','Services\WritingProgramService.cs','Services\WritingProjectService.cs','Services\MachineSetupStateService.cs','Services\RegionLayoutService.cs','Services\WritingEditorText.cs','RegionCanvas.cs') | ForEach-Object { Join-Path $repoRoot ('GRBL-Plotter\AutoWriter\' + $_) }
$testExe = Join-Path $tempRoot 'RegionTests.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Runtime.Serialization.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll $files (Join-Path $PSScriptRoot 'RegionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Region tests compilation failed.' }
& $testExe $tempRoot
if ($LASTEXITCODE -ne 0) { throw 'Region checks failed.' }
$uiExe = Join-Path $tempRoot 'RegionIntegrationTests.exe'
& $csc /nologo /target:exe /out:$uiExe /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'RegionIntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Region UI compilation failed.' }
if (!$ApplicationPath) { $ApplicationPath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe' }
& $uiExe $ApplicationPath (Join-Path $tempRoot 'ui-state.json')
if ($LASTEXITCODE -ne 0) { throw 'Region UI checks failed.' }
