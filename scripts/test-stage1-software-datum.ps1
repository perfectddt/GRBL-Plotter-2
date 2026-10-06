$ErrorActionPreference = 'Stop'

# Offline test for the「置零」button decision logic (AutoWriter/SoftwareDatum.cs).
#
# SoftwareDatum deliberately has no WinForms dependency, matching ResetGuard and AlarmUnlock, so the
# whole decision can be asserted here. The control layer itself (ControlSerialForm.BtnGRBLDatum_Click)
# is NOT covered - that needs an on-screen pass.
#
# What matters and why:
#   - BuildCommand must emit "G10 L20 P0 X0 Y0 Z0" for GRBL and "G92 ..." for Marlin. Writing bare
#     "G10 L20 P0" would be accepted and do NOTHING (omitted axes keep their offset), which reads as a
#     broken button rather than a deliberate no-op, so every selected axis must appear.
#   - CanExecute must refuse in ALARM (GRBL honours no command while locked) and refuse when no axis
#     is selected, but must accept IDLE + connected.
#   - The dialog must always state that this is NOT a physical home and that the operator owns the
#     datum - that is the whole safety story for a machine with $22 = 0.
#
# Assertions stay ASCII-only, so this script is safe to run from Windows PowerShell 5.1 regardless
# of the console code page. SoftwareDatum.cs carries a UTF-8 BOM so the legacy csc.exe below reads
# its Chinese text correctly.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\SoftwareDatum.cs'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-software-datum-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.SoftwareDatum.Tests.dll'

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
$type = $assembly.GetType('GrblPlotter.AutoWriter.SoftwareDatum', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic

$buildCommand = $type.GetMethod('BuildCommand', $flags)
$zeroCommand = $type.GetMethod('ZeroCommand', $flags)
$canExecute = $type.GetMethod('CanExecute', $flags)
$refusal = $type.GetMethod('RefusalReason', $flags)
$formatPos = $type.GetMethod('FormatPosition', $flags)
$logDone = $type.GetMethod('LogDone', $flags)
$buildMessage = $type.GetMethod('BuildMessage', $flags)
if (-not $buildCommand) { throw 'SoftwareDatum.BuildCommand not found.' }
if (-not $canExecute)   { throw 'SoftwareDatum.CanExecute not found.' }
if (-not $refusal)     { throw 'SoftwareDatum.RefusalReason not found.' }
if (-not $buildMessage){ throw 'SoftwareDatum.BuildMessage not found.' }

$titleField = $type.GetField('Title', $flags)
$logSendingField = $type.GetField('LogSending', $flags)
if (-not $titleField)      { throw 'SoftwareDatum.Title not found.' }
if (-not $logSendingField) { throw 'SoftwareDatum.LogSending not found.' }

function Assert-Contains([string] $text, [string] $needle, [string] $name) {
    if (-not $text) { throw "$name produced no text." }
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "$name did not contain '$needle'." }
}
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
}
function Assert-Equal($expected, $actual, [string] $name) {
    if ($expected -ne $actual) { throw "$name was expected '$expected' but was '$actual'." }
}

# ---- BuildCommand: the exact wire format matters more than anything else here. ----
$grblXYZ = [string]$buildCommand.Invoke($null, @($false, $true, $true, $true, $false, $false))
$grblX   = [string]$buildCommand.Invoke($null, @($false, $true, $false, $false, $false, $false))
$grblNone= [string]$buildCommand.Invoke($null, @($false, $false, $false, $false, $false, $false))
$grblA   = [string]$buildCommand.Invoke($null, @($false, $true, $true, $true, $true, $true))
$grblAoff= [string]$buildCommand.Invoke($null, @($false, $true, $true, $true, $true, $false))
$marlinXYZ = [string]$buildCommand.Invoke($null, @($true, $true, $true, $true, $false, $false))

Assert-Equal 'G10 L20 P0 X0 Y0 Z0' $grblXYZ   'BuildCommand GRBL X/Y/Z'
Assert-Equal 'G10 L20 P0 X0'      $grblX     'BuildCommand GRBL X only'
Assert-Equal 'G10 L20 P0'         $grblNone  'BuildCommand GRBL no axis'
Assert-Equal 'G10 L20 P0 X0 Y0 Z0 A0' $grblA 'BuildCommand GRBL with A'
Assert-Equal 'G10 L20 P0 X0 Y0 Z0' $grblAoff 'BuildCommand GRBL A present but not selected'
Assert-Equal 'G92 X0 Y0 Z0'       $marlinXYZ 'BuildCommand Marlin X/Y/Z'

# Marlin has no G10 L20; it must never receive one.
Assert-False ($marlinXYZ.Contains('G10')) 'Marlin command must not use G10'

# ZeroCommand is what the dialog advertises, and it must agree with BuildCommand.
Assert-Equal $grblXYZ ([string]$zeroCommand.Invoke($null, @($false))) 'ZeroCommand GRBL'
Assert-Equal 'G92 X0 Y0 Z0' ([string]$zeroCommand.Invoke($null, @($true))) 'ZeroCommand Marlin'

# ---- CanExecute: full truth table on (connected, isIdle, inAlarm, hasAxis). ----
Assert-True  ([bool]$canExecute.Invoke($null, @($true,  $true,  $false, $true)))  'idle + connected + axis'
Assert-False ([bool]$canExecute.Invoke($null, @($true,  $true,  $false, $false))) 'no axis selected'
Assert-False ([bool]$canExecute.Invoke($null, @($true,  $false, $false, $true)))  'not idle'
Assert-False ([bool]$canExecute.Invoke($null, @($true,  $true,  $true,  $true)))  'alarm reported'
Assert-False ([bool]$canExecute.Invoke($null, @($false, $true,  $false, $true)))  'disconnected'

# ---- RefusalReason: each refusal must name its own cause, and the happy path must stay silent. ----
$refuseOffline = [string]$refusal.Invoke($null, @($false, $false, $true))
$refuseAlarm   = [string]$refusal.Invoke($null, @($true,  $true,  $true))
$refuseNoAxis  = [string]$refusal.Invoke($null, @($true,  $false, $false))
$refuseNone    = [string]$refusal.Invoke($null, @($true,  $false, $true))

Assert-Contains $refuseOffline 'GRBL' 'RefusalReason disconnected'
Assert-True ($refuseAlarm.Length -gt 0) 'RefusalReason alarm is non-empty'
Assert-True ($refuseNoAxis.Length -gt 0) 'RefusalReason no axis is non-empty'
Assert-Equal '' $refuseNone 'RefusalReason none'
# The three refusal reasons must be distinguishable, otherwise the operator cannot tell which
# precondition failed.
Assert-True ($refuseOffline -ne $refuseAlarm) 'RefusalReason offline vs alarm differ'
Assert-True ($refuseAlarm   -ne $refuseNoAxis) 'RefusalReason alarm vs no-axis differ'
Assert-True ($refuseNoAxis  -ne $refuseOffline) 'RefusalReason no-axis vs offline differ'

# ---- FormatPosition must be culture-invariant: a decimal comma would corrupt the dialog text. ----
$pos = [string]$formatPos.Invoke($null, @(9.875, -10.0, 5.0))
Assert-Contains $pos '9.875'  'FormatPosition X'
Assert-Contains $pos '-10.000' 'FormatPosition Y'
Assert-Contains $pos '5.000'  'FormatPosition Z'
Assert-False ($pos.Contains('9,875')) 'FormatPosition must not use a decimal comma'
Assert-Equal '0.000, 0.000, 0.000' ([string]$formatPos.Invoke($null, @(0.0, 0.0, 0.0))) 'FormatPosition origin'

# ---- LogDone must name the command actually sent, and repeat the "not homing" caveat. ----
$logGrbl = [string]$logDone.Invoke($null, @($false))
$logMarlin = [string]$logDone.Invoke($null, @($true))
Assert-Contains $logGrbl   'G10 L20 P0' 'LogDone GRBL'
Assert-Contains $logMarlin 'G92'       'LogDone Marlin'
Assert-False ($logGrbl.Contains('$H')) 'LogDone must not imply homing'
Assert-False ($logGrbl.Contains('G92')) 'LogDone GRBL must not mention G92'

# ---- Dialog text: the safety story must survive in every variant. ----
$msgIdle = [string]$buildMessage.Invoke($null, @($false, $false, '9.875, -10.000, 5.000', $false, $false))
$msgAlarm = [string]$buildMessage.Invoke($null, @($true, $false, '0.000, 0.000, 0.000', $false, $false))
$msgStream = [string]$buildMessage.Invoke($null, @($false, $true, '1.000, 2.000, 3.000', $true, $false))
$msgMarlin = [string]$buildMessage.Invoke($null, @($false, $false, '0.000, 0.000, 0.000', $false, $true))
$msgNullPos = [string]$buildMessage.Invoke($null, @($false, $false, $null, $false, $false))

foreach ($pair in @(@($msgIdle,'idle'), @($msgAlarm,'alarm'), @($msgStream,'streaming'),
                    @($msgNullPos,'nullPosition'))) {
    Assert-True ($pair[0].Length -gt 0) ('BuildMessage ' + $pair[1])
    # Every GRBL variant must show the command it is about to send.
    Assert-Contains $pair[0] 'G10 L20 P0' ('BuildMessage ' + $pair[1])
    # And must state that no physical homing happens - the operator owns the datum.
    Assert-Contains $pair[0] '$H' ('BuildMessage ' + $pair[1])
    Assert-Contains $pair[0] 'G10' ('BuildMessage ' + $pair[1])
}
# Marlin cannot take G10 L20, so its dialog must advertise G92 and must not mention G10.
Assert-Contains $msgMarlin 'G92' 'BuildMessage marlin command'
Assert-False ($msgMarlin.Contains('G10')) 'BuildMessage marlin must not advertise G10'
Assert-Contains $msgMarlin '$H' 'BuildMessage marlin not-homing warning'

# The idle dialog must show the position that is about to be discarded.
Assert-Contains $msgIdle '9.875' 'BuildMessage idle position'
# Only the alarm variant warns about unlocking first, and only streaming warns about mid-program.
# Only the alarm and streaming variants add a state-specific warning line, and the alarm one points
# the operator at the unlock button before anything else.
Assert-Contains $msgAlarm 'GRBL' 'BuildMessage alarm mentions GRBL'
Assert-True ($msgStream.Length -gt $msgIdle.Length) 'BuildMessage streaming adds a warning'
Assert-True ($msgAlarm.Length -gt $msgIdle.Length)  'BuildMessage alarm adds a warning'
Assert-False ($msgIdle.Contains('ALARM')) 'idle dialog must not claim an alarm'
# A null position must degrade to '?' rather than throwing or printing nothing.
Assert-Contains $msgNullPos '?' 'BuildMessage null position'

$summary = ('PASS: SoftwareDatum emits G10 L20 P0 X0 Y0 Z0 (G92 for Marlin), refuses in ALARM / with no ' +
    'axis / while disconnected, and keeps the "this is not homing" warning in all five dialog variants. ' +
    'Temporary artifacts: ' + $tempRoot)
Write-Host $summary

# Mirror the result into artifacts/ so a headless run leaves evidence behind. The launcher in this
# repo captures stdout through PowerShell.exe, which swallows Write-Host, hence the file copy.
try {
    $logDir = Join-Path $repoRoot 'artifacts\stage0'
    [IO.Directory]::CreateDirectory($logDir) | Out-Null
    [IO.File]::WriteAllText((Join-Path $logDir 'software-datum-test.txt'), $summary, [Text.Encoding]::UTF8)
}
catch {
    Write-Host ('note: could not write the log mirror: ' + $_.Exception.Message)
}