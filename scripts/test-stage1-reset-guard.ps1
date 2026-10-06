$ErrorActionPreference = 'Stop'

# Offline test for candidate G (AutoWriter/ResetGuard.cs).
#
# ResetGuard only builds the text of the "confirm soft reset" prompt; it deliberately has no
# WinForms dependency so it can be compiled and asserted here. The dialog itself lives at the
# two call sites (MainForm.OnRaiseCmdEvent for the raw 0x18 byte, ControlSerialForm.BtnGRBLReset_Click
# for the full soft reset) and is NOT covered by this script - that still needs an on-screen pass.
#
# Assertions stay ASCII-only, so this script is safe to run from Windows PowerShell 5.1
# regardless of the console code page. ResetGuard.cs itself carries a UTF-8 BOM so the
# legacy csc.exe used below reads its Chinese prompt text correctly.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\ResetGuard.cs'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-reset-guard-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.ResetGuard.Tests.dll'

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    $command = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($command) { $csc = $command.Source }
}
if (-not $csc) { throw 'Could not find the .NET Framework C# compiler (csc.exe).' }

& $csc /nologo /target:library /out:$assemblyPath $sourceFile
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('GrblPlotter.AutoWriter.ResetGuard', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic

$buildMethod = $type.GetMethod('BuildMessage', $flags)
if (-not $buildMethod) { throw 'ResetGuard.BuildMessage(bool) not found.' }
$titleField = $type.GetField('Title', $flags)
if (-not $titleField) { throw 'ResetGuard.Title not found.' }

$idle = [string]$buildMethod.Invoke($null, @($false))
$streaming = [string]$buildMethod.Invoke($null, @($true))
$title = [string]$titleField.GetValue($null)

function Assert-Contains([string] $text, [string] $needle, [string] $name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "$name did not contain '$needle'." }
}
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}

# The prompt must name the control sequence and stay non-empty for both call sites.
Assert-True ($title.Length -gt 0) 'Title'
Assert-True ($idle.Length -gt 0) 'BuildMessage(false)'
Assert-True ($streaming.Length -gt 0) 'BuildMessage(true)'

# It must state the facts established by R4 on 2026-10-05, so the dialog cannot silently lose them.
Assert-Contains $idle 'Ctrl-X'    'BuildMessage(false)'
Assert-Contains $idle 'ALARM:3'   'BuildMessage(false)'
Assert-Contains $idle '$H'        'BuildMessage(false)'
Assert-Contains $idle '$X'        'BuildMessage(false)'

# The streaming variant must add the "program is interrupted" warning, never replace the rest.
Assert-True ($streaming.Length -gt $idle.Length) 'BuildMessage(true) adds a streaming warning'
Assert-Contains $streaming 'Ctrl-X'  'BuildMessage(true)'
Assert-Contains $streaming 'ALARM:3' 'BuildMessage(true)'

Write-Host ('PASS: ResetGuard builds a non-empty confirmation prompt for idle and streaming states (idle ' +
    $idle.Length + ' chars, streaming ' + $streaming.Length + ' chars). Temporary artifacts: ' + $tempRoot)
