$ErrorActionPreference = 'Stop'

# Layout check for the 「解锁」 and 「置零」 buttons added to the COM CNC window on 2026-10-06.
#
# They were inserted by widening tableLayoutPanel1 from 9 to 10 and then to 11 columns, moving
# btnGRBLHardReset from column 8 to 9 to 10. That kind of edit is easy to get subtly wrong, and a
# Designer mistake would only show up as overlapping buttons at runtime. So this script loads the
# real ControlSerialForm (with its real .resx), forces the TableLayoutPanel to lay out, and asserts
# the geometry of every button in the command row.
#
# The form is only constructed, never shown, so no serial port is opened and no GRBL is contacted.

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw "Missing build output: $exePath" }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = New-Object System.Collections.Generic.List[string]
try {
    $assembly = [System.Reflection.Assembly]::LoadFrom($exePath)
    $formType = $assembly.GetType('GrblPlotter.ControlSerialForm', $true)

    $form = [Activator]::CreateInstance($formType, @('COM-TEST', 1, $null, $null))

    # Force the layout to settle without showing the window on screen.
    $form.CreateControl()
    [System.Windows.Forms.Application]::DoEvents()
    $panel = $form.Controls.Find('tableLayoutPanel1', $true)[0]
    $panel.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()

    $log.Add("ColumnCount = " + $panel.ColumnCount)
    $log.Add("ClientSize  = " + $form.ClientSize)
    $log.Add("ColumnStyles= " + $panel.ColumnStyles.Count + "  GrowStyle=" + $panel.GrowStyle)
    for ($i = 0; $i -lt $panel.ColumnStyles.Count; $i++) {
        $cs = $panel.ColumnStyles[$i]
        $log.Add(("   col{0}: {1,-10} {2}" -f $i, $cs.SizeType, $cs.Width))
    }

    if ($panel.ColumnCount -ne 11) {
        throw "Expected ColumnCount 11 after adding the datum button, got $($panel.ColumnCount)."
    }
    # The column styles must track ColumnCount. A missing style makes TableLayoutPanel fall back
    # to AutoSize for that column and squeeze its neighbours, which silently clips captions.
    if ($panel.ColumnStyles.Count -ne $panel.ColumnCount) {
        throw "ColumnStyles has $($panel.ColumnStyles.Count) entries but ColumnCount is $($panel.ColumnCount); the unstyled column would be auto-sized and squeeze its neighbours."
    }
    $percentSum = 0
    foreach ($cs in $panel.ColumnStyles) { $percentSum += [int]$cs.Width }
    if ($percentSum -ne 100) { throw "ColumnStyles must sum to 100 percent, got $percentSum." }

    # Every button that must sit in the command row, in the expected left-to-right order.
    $expected = @(
        @('btnGRBLCommand0', '$'),
        @('btnGRBLCommand1', '$$'),
        @('btnGRBLCommand2', '$#'),
        @('btnGRBLCmndParser', '$G'),
        @('btnGRBLCmndBuild', '$I'),
        @('btnGRBLCommand3', '$N'),
        @('btnGRBLCommand4', '$X'),
        @('btnGRBLReset', 'RESET'),
        @('btnGRBLUnlock', 'UNLOCK'),
        @('btnGRBLDatum', 'DATUM'),
        @('btnGRBLHardReset', 'RST')
    )

    $boxes = @()
    foreach ($pair in $expected) {
        $name = $pair[0]
        $ctrl = $form.Controls.Find($name, $true)[0]
        if (-not $ctrl) { throw "Control not found: $name" }
        $boxes += [pscustomobject]@{ Name = $name; Ctrl = $ctrl }
    }

    $log.Add('')
    $log.Add(('-' * 78))
    $log.Add((' {0,-18} {1,-14} {2,-22} {3}' -f 'NAME', 'TEXT', 'BOUNDS(X,Y,W,H)', 'COL'))
    $log.Add(('-' * 78))

    $prevRight = -1
    $prevName = ''
    $clientWidth = $form.ClientSize.Width
    foreach ($b in $boxes) {
        $ctrl = $b.Ctrl
        $r = $ctrl.Bounds
        $col = $panel.GetColumn($ctrl)
        $log.Add((' {0,-18} {1,-14} {2,-22} {3}' -f $b.Name, $ctrl.Text,
            ("$($r.X),$($r.Y),$($r.Width),$($r.Height)"), $col))

        if ($ctrl.Parent -ne $panel) { throw "$($b.Name) is not a child of tableLayoutPanel1." }
        # NB: a control on a form that was never shown reports Visible=false even though it is laid
        # out, so presence is asserted through the bounds instead.
        if ($r.Width -le 0 -or $r.Height -le 0) {
            throw "$($b.Name) has a non-positive size ($($r.Width)x$($r.Height)); the 11-column layout collapsed."
        }
        # Regression guard: the 9-column row was an exact fit for the old 326px design width, so
        # simply adding a column pushed RST past the client area and clipped it. Any button whose
        # right edge exceeds the client width is unreachable for the operator.
        if ($r.Right -gt $clientWidth) {
            throw "$($b.Name) right edge $($r.Right) exceeds client width $clientWidth; it would be clipped."
        }
        # Overlap check against the previous button in the row.
        if ($prevRight -ge 0 -and $r.X -lt $prevRight) {
            throw "$($b.Name) overlaps $($prevName): left $($r.X) < previous right $prevRight."
        }
        $prevRight = $r.Right
        $prevName = $b.Name
    }
    $log.Add(('-' * 78))

    # The unlock button must actually be labelled, not blank, and must be next to RESET/RST.
    $unlock = $form.Controls.Find('btnGRBLUnlock', $true)[0]
    if ([string]::IsNullOrEmpty($unlock.Text)) { throw 'btnGRBLUnlock has no caption.' }
    $reset = $form.Controls.Find('btnGRBLReset', $true)[0]

    # It carries two CJK glyphs, so it needs an absolute minimum width. It is intentionally narrower
    # than 重置 (which gets the largest share), so the guard is a pixel floor rather than a comparison -
    # a regression here squeezes flexible columns and silently clips the caption.
    $minUnlockWidth = 44
    if ($unlock.Width -lt $minUnlockWidth) {
        throw "btnGRBLUnlock is only $($unlock.Width)px wide (minimum $minUnlockWidth); its CJK caption would be clipped."
    }

    $log.Add('')
    $log.Add('Unlock caption  : ' + $unlock.Text)
    $log.Add('Unlock width    : ' + $unlock.Width + 'px (minimum ' + $minUnlockWidth + 'px, 重置 is ' + $reset.Width + 'px)')
    $log.Add('Unlock tooltip  : ' + $form.Controls.Find('btnGRBLUnlock', $true)[0].ToolTipText.Length.ToString() + ' chars')

    # The datum button carries two CJK glyphs too, so it needs the same pixel floor. Squeezing
    # flexible columns is the failure mode that clips a caption without any error.
    $datum = $form.Controls.Find('btnGRBLDatum', $true)[0]
    if ([string]::IsNullOrEmpty($datum.Text)) { throw 'btnGRBLDatum has no caption.' }
    $minDatumWidth = 44
    if ($datum.Width -lt $minDatumWidth) {
        throw "btnGRBLDatum is only $($datum.Width)px wide (minimum $minDatumWidth); its CJK caption would be clipped."
    }
    $log.Add('')
    $log.Add('Datum caption   : ' + $datum.Text)
    $log.Add('Datum width     : ' + $datum.Width + 'px (minimum ' + $minDatumWidth + 'px)')
    $log.Add('Datum column    : ' + $panel.GetColumn($datum) + ' (must sit between unlock and RST)')
    if ($panel.GetColumn($datum) -le $panel.GetColumn($unlock)) {
        throw 'btnGRBLDatum must be to the right of btnGRBLUnlock.'
    }
    if ($panel.GetColumn($datum) -ge $panel.GetColumn($form.Controls.Find('btnGRBLHardReset', $true)[0])) {
        throw 'btnGRBLDatum must be to the left of btnGRBLHardReset.'
    }

    # The command input and the send button must still fill row 4 without overlapping.
    $cmd = $form.Controls.Find('cBCommand', $true)[0]
    $send = $form.Controls.Find('btnSend', $true)[0]
    $log.Add('cBCommand bounds: ' + $cmd.Bounds)
    $log.Add('btnSend bounds  : ' + $send.Bounds)
    if ($send.Left -lt $cmd.Right) {
        throw "btnSend ($($send.Left)) overlaps cBCommand right edge ($($cmd.Right))."
    }

    $form.Dispose()
    $assembly = $null

    $log.Add('')
    $log.Add('PASS: 11-column layout keeps all 11 command-row buttons visible, non-overlapping and correctly ordered.')
} catch {
    $log.Add('FAILED: ' + $_.Exception.Message)
    throw
} finally {
    [System.IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\unlock-layout-test.txt'), $log)
}