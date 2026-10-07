$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-writing-flow-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$interface = [IO.File]::ReadAllText((Join-Path $repo 'GRBL-Plotter\MachineControl\ControlSerialFormInterface.cs'))
# Compile the actual production CodeBuffer, not a rewritten queue implementation.
$buffer = $interface.Substring($interface.IndexOf('private class CodeBuffer'))
$buffer = $buffer.Substring(0, $buffer.LastIndexOf("`n    }"))
$buffer = $buffer.Replace('private class CodeBuffer', 'internal class CodeBuffer')
[IO.File]::WriteAllText((Join-Path $temp 'CodeBuffer.cs'), "using System; using System.Collections.Generic; namespace GrblPlotter { $buffer }")
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $temp 'WritingFlowTests.exe'
& $csc /nologo /out:$exe (Join-Path $repo 'GRBL-Plotter\AutoWriter\WritingFlowControl.cs') (Join-Path $repo 'GRBL-Plotter\AutoWriter\WritingMotionBudget.cs') (Join-Path $repo 'scripts\WritingFlowTests.cs') (Join-Path $temp 'CodeBuffer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Writing control test compilation failed.' }
$fixtures = @('pause-stop-short-strokes.nc', 'pause-stop-longline-20mm.nc') | ForEach-Object { Join-Path $repo ('artifacts\stage1\pause-stop-acceptance\' + $_) } | Where-Object { Test-Path -LiteralPath $_ }
& $exe @fixtures
if ($LASTEXITCODE -ne 0) { throw 'Writing control tests failed.' }
