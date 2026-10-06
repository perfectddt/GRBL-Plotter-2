$ErrorActionPreference = 'Stop'

# Offline test for the streaming throttle + feed-hold pause/stop (AutoWriter/StreamThrottle.cs).
#
# Why this exists (candidates E + F, user-approved 2026-10-06): grbl's RX buffer plus planner
# used to hold ~24 short writing lines, so the software pause needed ~10 s to drain and the
# stop point was uncontrollable (R2), while the stop button left the machine drawing ~2 more
# characters (R3). Injecting '!' fixes the latency but leaves queued pen-down strokes in the
# planner, which would air-draw after the pen's ~0.5 s spring-back. The approved compromise
# caps in-flight (sent-but-unconfirmed) lines at 4 and combines it with '!' on pause/stop
# and '~' on resume/stop-release, plus settling the software pause on HOLD.
#
# What must be asserted:
#   - OkToSendMore caps at MaxInFlightLines and tolerates a rewound (negative) cursor.
#   - Feed-hold is only injected while connected AND the machine is running.
#   - Settle-on-hold fires only for a requested, not-yet-settled pause/stop (never for the
#     manual R1 raw '!' path).
#   - Hold release is emitted only when the controller really is in HOLD.
#   - The wiring exists in the shipping code: throttle gate in PreProcessStreaming, '!' in
#     PauseStreaming/StopStreaming, '~' on resume/stop release, HOLD branch in
#     ProcessGrblStateChange.
#
# Assertions stay ASCII-only, so this script is safe to run from Windows PowerShell 5.1
# regardless of the console code page.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\StreamThrottle.cs'
$guardFile  = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\PauseMoveGuard.cs'
$boundaryFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\StrokeBoundary.cs'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
if (-not (Test-Path -LiteralPath $guardFile))  { throw "Missing source file: $guardFile" }
if (-not (Test-Path -LiteralPath $boundaryFile)) { throw "Missing source file: $boundaryFile" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stream-throttle-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.StreamThrottle.Tests.dll'

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

& $csc /nologo /target:library /out:$assemblyPath $sourceFile $guardFile $boundaryFile
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('GrblPlotter.AutoWriter.StreamThrottle', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic

$okToSendMore      = $type.GetMethod('OkToSendMore', $flags)
$shouldSendHold    = $type.GetMethod('ShouldSendFeedHold', $flags)
$shouldSettle      = $type.GetMethod('ShouldSettleOnHold', $flags)
$shouldRelease     = $type.GetMethod('ShouldReleaseHold', $flags)
if (-not $okToSendMore)  { throw 'StreamThrottle.OkToSendMore not found.' }
if (-not $shouldSendHold){ throw 'StreamThrottle.ShouldSendFeedHold not found.' }
if (-not $shouldSettle)  { throw 'StreamThrottle.ShouldSettleOnHold not found.' }
if (-not $shouldRelease) { throw 'StreamThrottle.ShouldReleaseHold not found.' }

$maxField = $type.GetField('MaxInFlightLines', $flags)
if (-not $maxField) { throw 'StreamThrottle.MaxInFlightLines not found.' }

$guardType = $assembly.GetType('GrblPlotter.AutoWriter.PauseMoveGuard', $true)
$isMotion  = $guardType.GetMethod('IsMotionCommand', $flags)
$shouldBlk = $guardType.GetMethod('ShouldBlock', $flags)
$refusal   = $guardType.GetField('RefusalMessage', $flags)
if (-not $isMotion)  { throw 'PauseMoveGuard.IsMotionCommand not found.' }
if (-not $shouldBlk) { throw 'PauseMoveGuard.ShouldBlock not found.' }
if (-not $refusal)   { throw 'PauseMoveGuard.RefusalMessage not found.' }

$boundaryType = $assembly.GetType('GrblPlotter.AutoWriter.StrokeBoundary', $true)
$isPenUp      = $boundaryType.GetMethod('IsPenUpLine', $flags)
$linesUntil   = $boundaryType.GetMethod('LinesUntilBoundary', $flags)
$lookAheadFld = $boundaryType.GetField('DefaultLookAhead', $flags)
if (-not $isPenUp)      { throw 'StrokeBoundary.IsPenUpLine not found.' }
if (-not $linesUntil)   { throw 'StrokeBoundary.LinesUntilBoundary not found.' }
if (-not $lookAheadFld) { throw 'StrokeBoundary.DefaultLookAhead not found.' }

$log = New-Object System.Collections.Generic.List[string]
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
    $log.Add("ok - $name")
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
    $log.Add("ok - $name")
}
function Assert-Equal($expected, $actual, [string] $name) {
    if ($expected -ne $actual) { throw "$name : expected '$expected' but got '$actual'." }
    $log.Add("ok - $name")
}

# ---- MaxInFlightLines: the approved compromise is exactly 4. ----
Assert-Equal 4 ([int]$maxField.GetValue($null)) 'MaxInFlightLines == 4'

# ---- OkToSendMore: cap at 4 in-flight, always allow a rewound cursor. ----
Assert-True  ([bool]$okToSendMore.Invoke($null, @(0, 0)))  'OkToSendMore(0,0)'
Assert-True  ([bool]$okToSendMore.Invoke($null, @(3, 0)))  'OkToSendMore(3,0)'
Assert-False ([bool]$okToSendMore.Invoke($null, @(4, 0)))  'OkToSendMore(4,0) capped'
Assert-False ([bool]$okToSendMore.Invoke($null, @(10, 5))) 'OkToSendMore(10,5) capped'
Assert-True  ([bool]$okToSendMore.Invoke($null, @(5, 2)))  'OkToSendMore(5,2)'
Assert-True  ([bool]$okToSendMore.Invoke($null, @(2, 6)))  'OkToSendMore rewound cursor'

# ---- ShouldSendFeedHold: only while connected AND running. ----
Assert-True  ([bool]$shouldSendHold.Invoke($null, @($true, $true)))   'feed hold: connected + running'
Assert-False ([bool]$shouldSendHold.Invoke($null, @($false, $true)))  'feed hold: disconnected'
Assert-False ([bool]$shouldSendHold.Invoke($null, @($true, $false)))  'feed hold: not running'
Assert-False ([bool]$shouldSendHold.Invoke($null, @($false, $false))) 'feed hold: neither'

# ---- ShouldSettleOnHold: requested AND not yet settled; never for the manual R1 path. ----
Assert-True  ([bool]$shouldSettle.Invoke($null, @($true, $false)))   'settle: requested + pending'
Assert-False ([bool]$shouldSettle.Invoke($null, @($false, $false)))  'settle: manual hold (R1 raw path)'
Assert-False ([bool]$shouldSettle.Invoke($null, @($true, $true)))    'settle: already paused'
Assert-False ([bool]$shouldSettle.Invoke($null, @($false, $true)))   'settle: neither'

# ---- ShouldReleaseHold: only from a real HOLD state. ----
Assert-True  ([bool]$shouldRelease.Invoke($null, @($true)))  'release: in HOLD'
Assert-False ([bool]$shouldRelease.Invoke($null, @($false))) 'release: not in HOLD'

# ---- PauseMoveGuard.IsMotionCommand (candidate D). ----
Assert-True  ([bool]$isMotion.Invoke($null, @('G91 G0 X5 Y3')))   'motion: R6 jog line'
Assert-True  ([bool]$isMotion.Invoke($null, @('g1 z5 f10000')))   'motion: lowercase pen down'
Assert-True  ([bool]$isMotion.Invoke($null, @('$J=G91 X5 F1000')))'motion: jog command'
Assert-True  ([bool]$isMotion.Invoke($null, @('G10 L20 P0 X0 Y0 Z0'))) 'motion: re-datum G10'
Assert-True  ([bool]$isMotion.Invoke($null, @('G92 X0')))         'motion: re-datum G92'
Assert-False ([bool]$isMotion.Invoke($null, @('$G')))             'query: parser state'
Assert-False ([bool]$isMotion.Invoke($null, @('$X')))             'unlock must stay reachable'
Assert-False ([bool]$isMotion.Invoke($null, @('$$')))             'query: settings'
Assert-False ([bool]$isMotion.Invoke($null, @('M5')))             'M-code allowed'
Assert-False ([bool]$isMotion.Invoke($null, @('G4 P0.5')))        'dwell allowed'
Assert-False ([bool]$isMotion.Invoke($null, @('G90')))            'mode word allowed'
Assert-False ([bool]$isMotion.Invoke($null, @('')))               'empty allowed'

# ---- PauseMoveGuard.ShouldBlock: while the pause has settled OR the controller holds. ----
Assert-True  ([bool]$shouldBlk.Invoke($null, @($true, $false, 'G0 X5')))  'block: paused + motion'
Assert-True  ([bool]$shouldBlk.Invoke($null, @($false, $true, 'G0 X5')))  'block: HOLD + motion (13:40 wedge case)'
Assert-True  ([bool]$shouldBlk.Invoke($null, @($false, $true, 'G91 G0 X5'))) 'block: HOLD + the exact R6-style jog'
Assert-False ([bool]$shouldBlk.Invoke($null, @($false, $false, 'G0 X5'))) 'allow: idle, not holding'
Assert-False ([bool]$shouldBlk.Invoke($null, @($true, $false, '$G')))     'allow: paused + query'
Assert-False ([bool]$shouldBlk.Invoke($null, @($false, $true, '$X')))     'allow: HOLD + unlock (escape hatch)'
Assert-False ([bool]$shouldBlk.Invoke($null, @($false, $true, 'M5')))     'allow: HOLD + M-code'
Assert-True  (($refusal.GetValue($null)).Length -gt 10)            'refusal message non-empty'

# ---- StrokeBoundary (candidate C). ----
Assert-Equal 4 ([int]$lookAheadFld.GetValue($null)) 'DefaultLookAhead == 4 (matches throttle window)'
Assert-True  ([bool]$isPenUp.Invoke($null, @('G1 Z0 F10000')))       'pen-up: plain lift'
Assert-True  ([bool]$isPenUp.Invoke($null, @('G90 G1 Z0.000 F10000'))) 'pen-up: explicit decimals'
Assert-False ([bool]$isPenUp.Invoke($null, @('G1 Z5 F10000')))       'not pen-up: Z5'
Assert-False ([bool]$isPenUp.Invoke($null, @('G1 X10 Z50')))         'not pen-up: Z50'
Assert-False ([bool]$isPenUp.Invoke($null, @('G1 X15.5 Y6 F200')))   'not pen-up: XY only'
Assert-False ([bool]$isPenUp.Invoke($null, @('')))                   'not pen-up: empty'

$prog1 = [string[]]@('G1 X15.5 Y6 F200', 'G1 X20 Y6 F200', 'G1 Z0 F10000', 'G1 X22 Y1 F1800')
Assert-Equal 3 ([int]$linesUntil.Invoke($null, @($prog1, 4)))  'boundary on 3rd line -> feed 3'
$prog2 = [string[]]@('G1 Z0 F10000', 'G1 X22 Y1 F1800')
Assert-Equal 1 ([int]$linesUntil.Invoke($null, @($prog2, 4)))  'next line is lift -> feed 1'
$prog3 = [string[]]@('G1 X1 Y1 F200', 'G1 X41 Y1 F200', 'G1 X1 Y6 F200', 'G1 X41 Y6 F200', 'G1 Z0 F10000')
Assert-Equal -1 ([int]$linesUntil.Invoke($null, @($prog3, 4))) 'long stroke: no boundary within window'
Assert-Equal 5 ([int]$linesUntil.Invoke($null, @($prog3, 5)))  'same program, window 5 -> feed 5'
$prog4 = [string[]]@('G1 X1 Y1 F200')
Assert-Equal -1 ([int]$linesUntil.Invoke($null, @($prog4, 4))) 'program tail without boundary'
Assert-Equal -1 ([int]$linesUntil.Invoke($null, @([string[]]@(), 4))) 'empty upcoming'

# ---- Wiring assertions: the shipping code must actually use the logic. ----
$streamingFile = Join-Path $repoRoot 'GRBL-Plotter\MachineControl\ControlSerialFormStreaming.cs'
$interfaceFile = Join-Path $repoRoot 'GRBL-Plotter\MachineControl\ControlSerialFormInterface.cs'
$streamingCode = [IO.File]::ReadAllText($streamingFile)
$interfaceCode = [IO.File]::ReadAllText($interfaceFile)

Assert-True ($streamingCode.Contains('StreamThrottle.OkToSendMore(streamingBuffer.IndexSent, streamingBuffer.IndexConfirmed)')) 'wiring: throttle gate in PreProcessStreaming loop'
Assert-True ($streamingCode.Contains("[Feed hold ! - stop immediately]")) 'wiring: pause logs feed hold'
Assert-True ($streamingCode.Contains("[Release hold ~ - resume motion]")) 'wiring: resume releases hold'
Assert-True ($streamingCode.Contains("[Stop: feed hold ! - halt immediately]")) 'wiring: stop logs feed hold'
Assert-True ($streamingCode.Contains("[Stop: wait for HOLD report, then release ~]")) 'wiring F-fix: stop defers the release'
Assert-True ($streamingCode.Contains("pendingStopHoldRelease = true")) 'wiring F-fix: release flag armed after StopStreamingFinal'
Assert-True ($interfaceCode.Contains("(grblStateNow == GrblState.hold) && pendingStopHoldRelease")) 'wiring F-fix: release fires on the HOLD report'
Assert-True ($streamingCode.Contains("[Start refused: controller in HOLD")) 'wiring: StartStreaming refuses HOLD'
Assert-True ($streamingCode.Contains("grblStateNow == GrblState.hold) && !isStreamingPause")) 'wiring: HOLD refusal keeps the pause-resume path exempt'
Assert-True ($streamingCode.Contains("RealtimeCommand((byte)'!')")) 'wiring: feed hold byte sent'
Assert-True ($streamingCode.Contains("RealtimeCommand((byte)'~')")) 'wiring: cycle start byte sent'
Assert-True ($interfaceCode.Contains('StreamThrottle.ShouldSettleOnHold')) 'wiring: HOLD settle branch in ProcessGrblStateChange'
Assert-True ($interfaceCode.Contains("(grblStateNow == GrblState.hold) && isStreaming")) 'wiring: HOLD branch guarded by streaming'

# candidate A wiring: auto pen-up on pause/stop settle, only when the pen was down
Assert-True ($streamingCode.Contains('[Auto pen-up Z0]')) 'wiring A: auto pen-up logged'
Assert-True ($streamingCode.Contains('RequestSend("G90 G1 Z0 F10000")')) 'wiring A: pen-up line sent'
Assert-True ($streamingCode.Contains('AutoWriter.ResumeRestore.IsPenDown(posPause.Z)')) 'wiring A: gated on pen down at settle'

# candidate D wiring: COM CNC manual command box consults the guard (pause + HOLD)
$serialFormCode = [IO.File]::ReadAllText((Join-Path $repoRoot 'GRBL-Plotter\MachineControl\ControlSerialForm.cs'))
Assert-True ($serialFormCode.Contains('AutoWriter.PauseMoveGuard.ShouldBlock(isStreamingPause, grblStateNow == GrblState.hold, cmd)')) 'wiring D: SendCommand consults guard with HOLD'
Assert-True ($serialFormCode.Contains('AutoWriter.PauseMoveGuard.RefusalMessage')) 'wiring D: refusal logged'

# candidate C wiring: boundary scan before feed-hold, drain allowance in the send loop
Assert-True ($streamingCode.Contains('StrokeBoundary.LinesUntilBoundary(PeekUpcomingStreamLines')) 'wiring C: boundary scan on pause request'
Assert-True ($streamingCode.Contains('pauseDrainRemaining = drainToBoundary')) 'wiring C: drain allowance set'
Assert-True ($streamingCode.Contains('pauseDrainRemaining--')) 'wiring C: drain allowance spent per line'
Assert-True ($streamingCode.Contains('(!isStreamingRequestPause || (pauseDrainRemaining > 0))')) 'wiring C: send loop honors drain allowance'
Assert-True ($streamingCode.Contains('streamingBuffer.PeekLine(start + i)')) 'wiring C: peek helper reads buffer'
Assert-True ($interfaceCode.Contains('public string PeekLine(int index)')) 'wiring C: buffer exposes PeekLine'

# candidate B wiring: ResumeRestore no longer emits any G0 rapid (lift -> G1 travel -> re-press)
$resumeFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\ResumeRestore.cs'
$resumeCode = [IO.File]::ReadAllText($resumeFile)
Assert-False ($resumeCode.Contains('G0 X')) 'wiring B: no G0 rapid in XY restore'
Assert-True  ($resumeCode.Contains('F1800')) 'wiring B: XY travel carries explicit F1800'

# candidate I wiring: flow-panel reset routed through the full soft reset
$userControlsCode = [IO.File]::ReadAllText((Join-Path $repoRoot 'GRBL-Plotter\GUI\MainFormUserControls.cs'))
Assert-True ($userControlsCode.Contains('_serial_form.GrblReset(true); return;')) 'wiring I: flow-panel reset uses GrblReset(savePos)'

$log.Add('')
$log.Add('PASS: stream throttle caps in-flight lines at 4, feed-hold ''!'' is injected on pause/stop')
$log.Add('only while connected and running, the pause settles on HOLD exactly once per request, and')
$log.Add('resume/stop release the hold with ''~''. All wiring points are present in the shipping code.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\stream-throttle-test.txt'), $log)
