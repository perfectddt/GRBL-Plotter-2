# Convert an SVG file with the GRBL-Plotter importer and print the resulting G-code,
# so an import result can be inspected without starting the GUI.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\test-svg-import.ps1 -Svg <path>
#   powershell -ExecutionPolicy Bypass -File scripts\test-svg-import.ps1 -Svg <path> -Device Router
#   ... -Device Router -ZUp 0 -ZDown 5 -ZFeed 10000 -Sync
#
# -Device : Laser | Plotter | Router | Individual   (default Laser)
# -ZUp/-ZDown/-ZFeed : override the selected device's pen up/down/feed Z (default: from Settings)
# -Sync   : use AutoWriter's temporary pen scope for this conversion (no device or tool-table persistence)
#
# (Keep this file ASCII only - PowerShell 5.1 reads files without BOM as ANSI.)
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Svg,
    [string]$Device = 'Laser',
    [string]$ZUp = '-',
    [string]$ZDown = '-',
    [string]$ZFeed = '-',
    [switch]$Sync,
    [string]$ApplicationPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$debugDir = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug'
$appExe = Join-Path $debugDir 'GRBL-Plotter.exe'
if (!(Test-Path -LiteralPath $appExe)) { throw "Build Debug first, missing $appExe" }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harnessExe = Join-Path $debugDir 'svg-import-harness.exe'
& $csc /nologo /target:exe /out:$harnessExe /r:"$appExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'SvgImportTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'SVG import harness compilation failed.' }

$syncArg = if ($Sync) { 'sync' } else { '-' }

Push-Location $debugDir
try { & $harnessExe $Svg $Device $ZUp $ZDown $ZFeed $syncArg } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "SVG import harness failed with exit code $LASTEXITCODE." }
