param([Parameter(Mandatory=$true)][string]$AppExe)
$ErrorActionPreference = 'Stop'
$AppExe = (Resolve-Path -LiteralPath $AppExe).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path (Split-Path $AppExe -Parent) 'svg-style-tests.exe'
& $csc /nologo /target:exe /out:$harness /r:System.Xml.Linq.dll (Join-Path $PSScriptRoot 'SvgStyleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'SVG style tests compilation failed' }
& $harness $AppExe
if ($LASTEXITCODE -ne 0) { throw 'SVG style tests failed' }
