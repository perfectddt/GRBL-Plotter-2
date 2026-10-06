$ErrorActionPreference = 'Stop'

# Candidate J (R6, 2026-10-06): pause/resume lost in-flight lines.
#
# R6 field evidence: pause settled with the machine parked at the START of a stroke
# (line 61 done, lines 62-63 written to the OS but never executed by grbl), yet resume
# continued from line 64 - the vertical stroke was skipped on paper. grbl reporting IDLE
# proves every line it received was executed and ok'd, so anything still unconfirmed at
# settle time never arrived. The fix rewinds at pause settle:
#   - streamingBuffer.IndexSent = IndexConfirmed  (resume re-enqueues from first unconfirmed)
#   - sendBuffer.RemoveUnconfirmedTail()          (drop the sent-but-unconfirmed copies)
#
# This script loads the real build and replays the R6 buffer states against the private
# CodeBuffer class, asserting the cursor arithmetic that the resume depends on.

$repoRoot = Split-Path -Parent $PSScriptRoot
# Prefer Debug, but fall back to Release when Debug is locked by a running instance or
# otherwise older - the CodeBuffer semantics under test are identical in both configs.
$candidates = @(
    (Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'),
    (Join-Path $repoRoot 'GRBL-Plotter\bin\Release\GRBL-Plotter.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending
if ($candidates.Count -lt 1) { throw 'No build output found (Debug or Release).' }
$exePath = $candidates[0]
$exeStamp = (Get-Item $exePath).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')

$outPath = Join-Path $repoRoot 'artifacts\stage0\resume-rewind-test.txt'
$log = New-Object System.Collections.Generic.List[string]
$passCount = 0

function Assert-Equal($expected, $actual, $what) {
    if ("$expected" -ne "$actual") { throw "FAIL ${what}: expected '$expected' got '$actual'" }
    $script:log.Add(("PASS {0,-46} = {1}" -f $what, $actual))
    $script:passCount++
}
function Assert-True($cond, $what) {
    if (-not $cond) { throw "FAIL ${what}" }
    $script:log.Add(("PASS {0}" -f $what))
    $script:passCount++
}

try {
    $log.Add('Exe under test: ' + $exePath + '  (' + $exeStamp + ')')
    $assembly = [System.Reflection.Assembly]::LoadFrom($exePath)
    # NB: there is no type named ControlSerialFormInterface - the three MachineControl files are
    # all "partial class ControlSerialForm", and CodeBuffer is nested inside it.
    $ifaceType = $assembly.GetType('GrblPlotter.ControlSerialForm', $true)
    $flags = [System.Reflection.BindingFlags]'NonPublic'
    $cbType = $ifaceType.GetNestedType('CodeBuffer', $flags)
    if ($null -eq $cbType) { throw 'CodeBuffer nested type not found.' }

    $addMI = $cbType.GetMethod('Add', [Type[]]@([string], [int]))
    if ($null -eq $addMI) { throw 'CodeBuffer.Add(string,int) not found.' }
    if ($null -eq $cbType.GetMethod('RemoveUnconfirmedTail')) {
        throw 'CodeBuffer.RemoveUnconfirmedTail not found - candidate J is not built into the exe.'
    }

    function New-CB { [Activator]::CreateInstance($script:cbType, $true) }
    function CB-Add($cb, $txt, $nr) { $script:addMI.Invoke($cb, @($txt, $nr)) }
    function CB-Call($cb, $name) { $script:cbType.GetMethod($name).Invoke($cb, $null) }
    function CB-Get($cb, $prop) { $script:cbType.GetProperty($prop).GetValue($cb) }
    function CB-Set($cb, $prop, $v) { $script:cbType.GetProperty($prop).SetValue($cb, $v) }

    # ------------------------------------------------------------------
    # Scenario A - baseline cursor semantics (the meaning of 'confirmed').
    # R6 log line: "[Start streaming line:64]" came from GetSentLineNr() reading the
    # NEXT entry to enqueue, and the resume point must come from the same convention.
    # ------------------------------------------------------------------
    $a = New-CB
    CB-Add $a 'G1Z5' 61          # pen down at stroke start (line 61 - executed in R6)
    CB-Add $a 'G1X31.5Y6F200' 62 # vertical stroke            (line 62 - LOST in R6)
    CB-Add $a 'G1Z0' 63          # pen up                     (line 63 - LOST in R6)
    CB-Add $a 'G1X36Y1F1800' 64  # next stroke travel         (line 64 - old resume point)
    Assert-Equal 4 (CB-Get $a 'Count') 'A: Count after 4 adds'

    (CB-Call $a 'LineWasSent') | Out-Null; (CB-Call $a 'LineWasSent') | Out-Null
    Assert-Equal 2 (CB-Get $a 'IndexSent') 'A: IndexSent after 2 sends'
    # sent is the NEXT-to-send cursor: after sending lines 61+62 it points at line 63.
    Assert-Equal 'G1Z0' (CB-Call $a 'GetSentLine') 'A: GetSentLine is next-to-send (line 63)'

    (CB-Call $a 'LineWasReceived') | Out-Null
    Assert-Equal 1 (CB-Get $a 'IndexConfirmed') 'A: IndexConfirmed after 1 ok'
    # GetConfirmedLineNr returns the FIRST UNCONFIRMED line - the resume-from value.
    Assert-Equal 62 (CB-Call $a 'GetConfirmedLineNr') 'A: GetConfirmedLineNr = first unconfirmed'

    # ------------------------------------------------------------------
    # Scenario B - R6 replay on the sendBuffer: rewind drops exactly the lost tail
    # and the re-enqueued lost line becomes the next one to send.
    # ------------------------------------------------------------------
    $b = New-CB
    # lines 58..63 written to the OS (6 sends), oks received for 58..61 (4 confirms)
    foreach ($ln in 58..63) { CB-Add $b ("L$ln") $ln }
    for ($i = 0; $i -lt 6; $i++) { (CB-Call $b 'LineWasSent') | Out-Null }
    for ($i = 0; $i -lt 4; $i++) { (CB-Call $b 'LineWasReceived') | Out-Null }
    Assert-Equal 62 (CB-Call $b 'GetConfirmedLineNr') 'B: resume-from is line 62 (R6)'

    $removed = CB-Call $b 'RemoveUnconfirmedTail'
    Assert-Equal 2 $removed 'B: RemoveUnconfirmedTail drops 2 lost lines'
    Assert-Equal 4 (CB-Get $b 'Count') 'B: Count after rewind'
    Assert-Equal 4 (CB-Get $b 'IndexSent') 'B: IndexSent reset to confirmed'
    Assert-Equal 4 (CB-Get $b 'IndexConfirmed') 'B: IndexConfirmed unchanged'

    # After the pause settles, other commands ($G, M5, M9) ride through the queue.
    CB-Add $b '$G' 0
    (CB-Call $b 'LineWasSent') | Out-Null; (CB-Call $b 'LineWasReceived') | Out-Null
    Assert-Equal 5 (CB-Get $b 'IndexConfirmed') 'B: ok for $G advances confirmed'
    # With the sent cursor past the end, GetSentLine returns the sentinel "OV".
    Assert-Equal 'OV' (CB-Call $b 'GetSentLine') 'B: nothing pending after $G ok'

    # At resume the lost lines are re-enqueued from the rewound streamingBuffer and
    # become the next ones to go out - the vertical stroke is re-sent, not skipped.
    CB-Add $b 'G1X31.5Y6F200' 62
    CB-Add $b 'G1Z0' 63
    Assert-Equal 'G1X31.5Y6F200' (CB-Call $b 'GetSentLine') 'B: re-sent line 62 is next to send'
    (CB-Call $b 'LineWasSent') | Out-Null
    Assert-Equal 'G1Z0' (CB-Call $b 'GetSentLine') 'B: re-sent line 63 follows'
    (CB-Call $b 'LineWasSent') | Out-Null
    (CB-Call $b 'LineWasReceived') | Out-Null; (CB-Call $b 'LineWasReceived') | Out-Null
    Assert-Equal 0 (CB-Call $b 'RemoveUnconfirmedTail') 'B: tail clean once resent lines are ok-d'

    # ------------------------------------------------------------------
    # Scenario C - clean pause boundary: nothing in flight, nothing removed.
    # This must stay byte-identical to the old behaviour.
    # ------------------------------------------------------------------
    $c = New-CB
    CB-Add $c 'G1X0Y0' 1
    CB-Add $c 'G1Z5' 2
    (CB-Call $c 'LineWasSent') | Out-Null; (CB-Call $c 'LineWasSent') | Out-Null
    (CB-Call $c 'LineWasReceived') | Out-Null; (CB-Call $c 'LineWasReceived') | Out-Null
    Assert-Equal 0 (CB-Call $c 'RemoveUnconfirmedTail') 'C: clean tail removes nothing'
    Assert-Equal 2 (CB-Get $c 'Count') 'C: Count unchanged'
    CB-Add $c 'G1X10Y10F1000' 3
    Assert-Equal 'G1X10Y10F1000' (CB-Call $c 'GetSentLine') 'C: streaming continues normally'

    # ------------------------------------------------------------------
    # Scenario D - streamingBuffer rewind: cursor only, entries preserved.
    # The whole program stays in the buffer; only the enqueue cursor moves back.
    # ------------------------------------------------------------------
    $d = New-CB
    foreach ($ln in 1..10) { CB-Add $d ("L$ln") $ln }
    for ($i = 0; $i -lt 7; $i++) { (CB-Call $d 'LineWasSent') | Out-Null }   # enqueued 1..7
    for ($i = 0; $i -lt 5; $i++) { (CB-Call $d 'LineWasReceived') | Out-Null } # ok for 1..5
    CB-Set $d 'IndexSent' (CB-Get $d 'IndexConfirmed')
    Assert-Equal 10 (CB-Get $d 'Count') 'D: entries preserved after cursor rewind'
    Assert-Equal 6 (CB-Call $d 'GetSentLineNr') 'D: resume re-enqueues from line 6'
    Assert-Equal 'L6' (CB-Call $d 'GetSentLine') 'D: line 6 content is next'
    # Re-enqueue path: PreProcessStreaming adds line 6 again with its own lineNr.
    CB-Add $d 'L6' 6
    Assert-Equal 6 (CB-Call $d 'GetSentLineNr') 'D: re-added line keeps lineNr 6'

    $log.Add('')
    $log.Add("PASS: candidate J rewind semantics hold - $passCount assertions. " +
        'Pause settle drops sent-but-unconfirmed lines, resume re-sends them, and a clean ' +
        'pause boundary is untouched.')
}
catch {
    $log.Add('FAIL: ' + $_.Exception.Message)
    # Method-call arguments cannot contain a pipeline (parse error), so materialize first.
    $traceLines = @($_.Exception.StackTrace -split "`r?`n" | Select-Object -First 6)
    $log.AddRange([string[]]$traceLines)
    [IO.File]::WriteAllLines($outPath, $log)
    exit 1
}
[IO.File]::WriteAllLines($outPath, $log)
exit 0
