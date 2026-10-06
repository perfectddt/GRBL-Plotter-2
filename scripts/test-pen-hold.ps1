param(
    [ValidatePattern('^COM[1-9][0-9]*$')][string]$Port = 'COM9',
    [switch]$Execute,
    [switch]$ContinuousMotion,
    [switch]$PenInstalled,
    [ValidateSet(1,4,5,6,8,10,100)][int]$LengthMm = 1,
    [ValidateSet('X','Y')][string]$Axis = 'X',
    [switch]$FastMotion,
    [switch]$Square,
    [switch]$ThreeStrokes,
    [switch]$CharacterGong,
    [switch]$CharacterWang,
    [switch]$CharacterRow,
    [switch]$SizeCompare,
    [ValidateRange(-100,100)][double]$DrawOffsetXmm = 0,
    [ValidateRange(-100,100)][double]$DrawOffsetYmm = 0
)
$ErrorActionPreference = 'Stop'
if ($Square -and (-not $ContinuousMotion -or -not $FastMotion -or $LengthMm -ne 10 -or $Axis -ne 'X')) { throw 'Square requires continuous fast motion, length 10, axis X.' }
if ($ThreeStrokes -and -not $Square) { throw 'Three strokes requires bounded square test options.' }
if ($CharacterGong -and (-not $Square -or $ThreeStrokes)) { throw 'Character requires bounded square options, without three strokes.' }
if ($CharacterWang -and ($Square -or $ThreeStrokes -or $CharacterGong)) { throw 'Character Wang is a standalone small-character test; do not combine it with square, three-strokes or gong.' }
if ($CharacterWang -and (-not $ContinuousMotion -or -not $FastMotion -or -not $PenInstalled -or $Axis -ne 'X' -or $LengthMm -notin @(4,5,6,8))) { throw 'Character Wang requires continuous fast motion, an installed pen, axis X and a small size (4/5/6/8 mm).' }
if (($CharacterRow -or $SizeCompare) -and ($Square -or $ThreeStrokes -or $CharacterGong -or $CharacterWang)) { throw 'Row and size-compare modes are standalone; do not combine them with square, three-strokes, gong or wang.' }
if (($CharacterRow -or $SizeCompare) -and $CharacterRow -and $SizeCompare) { throw 'Choose one of -CharacterRow or -SizeCompare.' }
if (($CharacterRow -or $SizeCompare) -and (-not $ContinuousMotion -or -not $FastMotion -or -not $PenInstalled -or $Axis -ne 'X')) { throw 'Row and size-compare modes require continuous fast motion, an installed pen and axis X.' }
if ($CharacterRow -and $LengthMm -notin @(4,5,6,8)) { throw 'Character row requires a small character size (4/5/6/8 mm).' }
if ($PenInstalled -and -not $ContinuousMotion) { throw 'Installed-pen test requires continuous motion; standalone hold tests are pen-removed only.' }
if ($LengthMm -ne 1 -and -not $ContinuousMotion) { throw 'Line length applies only to continuous motion.' }
if ($Axis -ne 'X' -and -not $ContinuousMotion) { throw 'Selected drawing axis applies only to continuous motion.' }
if ($FastMotion -and -not $ContinuousMotion) { throw 'Fast motion applies only to continuous motion.' }
# Explicit, bounded diagnostic. Default invocation never opens a serial port.
$allowed = @('$$', '$G', '$#', '$1=255', '$1=25', 'G1G90 Z0.0F10000', 'G1G90 Z5.0F10000')
if ($ContinuousMotion) { $allowed = @('$$', '$G', '$#', 'G1G90 Z0.0F10000', 'G1G90 Z5.0F10000') }
# Small character WANG: three horizontals plus one vertical, scaled to LengthMm.
# Straight-line centre-line geometry only; it is deliberately not a font outline.
$wangStrokes = @()
if ($CharacterWang) {
    $wangSize = [double]$LengthMm
    $wangHalf = $wangSize / 2.0
    $wangStrokes = @(
        @(0.0, 0.0, $wangSize, 0.0),
        @(0.0, $wangHalf, $wangSize, $wangHalf),
        @(0.0, $wangSize, $wangSize, $wangSize),
        @($wangHalf, 0.0, $wangHalf, $wangSize)
    )
}
function New-WangGroups([double]$startX, [double]$startY) {
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $result = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $wangStrokes.Count; $i++) {
        $s = $wangStrokes[$i]
        $travel = 'G1G90 X' + ($startX + $s[0]).ToString('0.000', $culture) + 'Y' + ($startY + $s[1]).ToString('0.000', $culture) + 'F1800'
        $draw = 'G1G90 X' + ($startX + $s[2]).ToString('0.000', $culture) + 'Y' + ($startY + $s[3]).ToString('0.000', $culture) + 'F1000'
        $group = @($travel, 'G1G90 Z5.0F10000', $draw, 'G1G90 Z0.0F10000')
        if ($i -eq $wangStrokes.Count - 1) { $group += 'G1G90 X' + $startX.ToString('0.000', $culture) + 'Y' + $startY.ToString('0.000', $culture) + 'F1800' }
        $result.Add($group)
    }
    return $result
}
function Assert-WangPlan($groups, [double]$size) {
    if ($groups.Count -ne 4) { throw ('Character Wang must have four strokes, got ' + $groups.Count) }
    $pattern = '^G1G90 (Z[05]\.0F10000|X-?[0-9]+\.[0-9]{3}Y-?[0-9]+\.[0-9]{3}F(1000|1800))$'
    foreach ($group in $groups) {
        $bytes = [Text.Encoding]::ASCII.GetByteCount(($group -join "`n") + "`n")
        if ($bytes -gt 127) { throw ('Character Wang group exceeds the bounded RX capacity: ' + $bytes + ' bytes.') }
        if ($group[0] -notmatch 'F1800$') { throw 'Each stroke must begin with an F1800 travel move.' }
        if ($group[1] -cne 'G1G90 Z5.0F10000') { throw 'Each stroke must pen down with Z5.' }
        if ($group[2] -notmatch 'F1000$') { throw 'Each stroke must draw with F1000.' }
        foreach ($command in $group) {
            if ($command -notmatch $pattern) { throw ('Unexpected character command: ' + $command) }
            if ($command -match 'X(-?[0-9]+\.[0-9]{3})Y(-?[0-9]+\.[0-9]{3})') {
                $x = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
                $y = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
                if ($x -lt 0 -or $x -gt $size -or $y -lt 0 -or $y -gt $size) { throw ('Character Wang leaves the ' + $size + ' mm envelope: ' + $command) }
            }
        }
    }
}
function Get-GongStrokes([double]$size) {
    $half = $size / 2.0
    return @(@(0.0, 0.0, $size, 0.0), @($half, 0.0, $half, $size), @(0.0, $size, $size, $size))
}
function Get-ShiStrokes([double]$size) {
    $half = $size / 2.0
    return @(@(0.0, $half, $size, $half), @($half, 0.0, $half, $size))
}
function Get-WangStrokes([double]$size) {
    $half = $size / 2.0
    return @(@(0.0, 0.0, $size, 0.0), @(0.0, $half, $size, $half), @(0.0, $size, $size, $size), @($half, 0.0, $half, $size))
}
# Character row: GONG, SHI, WANG in a line, each in a LengthMm cell with a 2mm gap.
# Size compare: WANG drawn at 5/6/8mm in a line for a small-size legibility comparison.
# Both are straight-line centre-line geometry, deliberately not font outlines.
function New-RowPlan {
    $gap = 2.0
    $plan = [Collections.Generic.List[object]]::new()
    $cursor = 0.0
    if ($CharacterRow) {
        $size = [double]$LengthMm
        foreach ($name in @('Gong', 'Shi', 'Wang')) {
            $strokes = switch ($name) { 'Gong' { Get-GongStrokes $size } 'Shi' { Get-ShiStrokes $size } 'Wang' { Get-WangStrokes $size } }
            $plan.Add([pscustomobject]@{ Strokes = $strokes; Ox = $cursor; Oy = 0.0; W = $size; H = $size })
            $cursor += $size + $gap
        }
    } else {
        foreach ($size in @(5.0, 6.0, 8.0)) {
            $plan.Add([pscustomobject]@{ Strokes = (Get-WangStrokes $size); Ox = $cursor; Oy = 0.0; W = $size; H = $size })
            $cursor += $size + $gap
        }
    }
    return $plan
}
function New-RowGroups($plan, [double]$baseX, [double]$baseY, [double]$homeX, [double]$homeY) {
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $list = [Collections.Generic.List[object]]::new()
    foreach ($cell in $plan) {
        $ox = $baseX + $cell.Ox
        $oy = $baseY + $cell.Oy
        foreach ($s in $cell.Strokes) {
            $travel = 'G1G90 X' + ($ox + $s[0]).ToString('0.000', $culture) + 'Y' + ($oy + $s[1]).ToString('0.000', $culture) + 'F1800'
            $draw = 'G1G90 X' + ($ox + $s[2]).ToString('0.000', $culture) + 'Y' + ($oy + $s[3]).ToString('0.000', $culture) + 'F1000'
            $list.Add(@($travel, 'G1G90 Z5.0F10000', $draw, 'G1G90 Z0.0F10000'))
        }
    }
    if ($list.Count -gt 0) {
        $last = $list.Count - 1
        $list[$last] = $list[$last] + ('G1G90 X' + $homeX.ToString('0.000', $culture) + 'Y' + $homeY.ToString('0.000', $culture) + 'F1800')
    }
    return $list
}
function Assert-RowPlan($plan, $groups, [double]$baseX, [double]$baseY) {
    $width = 0.0
    $height = 0.0
    $expected = 0
    foreach ($cell in $plan) {
        $width = [Math]::Max($width, $cell.Ox + $cell.W)
        $height = [Math]::Max($height, $cell.Oy + $cell.H)
        $expected += $cell.Strokes.Count
    }
    if ($groups.Count -ne $expected) { throw ('Row must have ' + $expected + ' stroke groups, got ' + $groups.Count) }
    $pattern = '^G1G90 (Z[05]\.0F10000|X-?[0-9]+\.[0-9]{3}Y-?[0-9]+\.[0-9]{3}F(1000|1800))$'
    foreach ($group in $groups) {
        $bytes = [Text.Encoding]::ASCII.GetByteCount(($group -join "`n") + "`n")
        if ($bytes -gt 127) { throw ('Row group exceeds the bounded RX capacity: ' + $bytes + ' bytes.') }
        if ($group[0] -notmatch 'F1800$') { throw 'Each stroke must begin with an F1800 travel move.' }
        if ($group[1] -cne 'G1G90 Z5.0F10000') { throw 'Each stroke must pen down with Z5.' }
        if ($group[2] -notmatch 'F1000$') { throw 'Each stroke must draw with F1000.' }
        for ($ci = 0; $ci -lt $group.Count; $ci++) {
            $command = $group[$ci]
            if ($command -notmatch $pattern) { throw ('Unexpected row command: ' + $command) }
            # Only the travel (index 0) and draw (index 2) commands must stay inside the
            # drawing envelope; the trailing command is the return-to-start move.
            if (($ci -eq 0 -or $ci -eq 2) -and $command -match 'X(-?[0-9]+\.[0-9]{3})Y(-?[0-9]+\.[0-9]{3})') {
                $x = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
                $y = [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
                if ($x -lt $baseX - 0.001 -or $x -gt $baseX + $width + 0.001 -or $y -lt $baseY - 0.001 -or $y -gt $baseY + $height + 0.001) { throw ('Row leaves the envelope: ' + $command) }
            }
        }
    }
    return [pscustomobject]@{ Width = $width; Height = $height; Groups = $expected }
}
if (-not $Execute) {
    $allowed
    if ($CharacterWang) {
        $preview = New-WangGroups 0.0 0.0
        Assert-WangPlan $preview ([double]$LengthMm)
        Write-Output ('CHARACTER WANG PLAN OK: strokes=' + $preview.Count + ' size=' + $LengthMm + 'mm envelope=0..' + $LengthMm + 'mm start=0,0')
        foreach ($group in $preview) { Write-Output ('  ' + ($group -join ' | ')) }
    }
    if ($CharacterRow -or $SizeCompare) {
        $plan = New-RowPlan
        $groups = New-RowGroups $plan ([double]$DrawOffsetXmm) ([double]$DrawOffsetYmm) 0.0 0.0
        $env = Assert-RowPlan $plan $groups ([double]$DrawOffsetXmm) ([double]$DrawOffsetYmm)
        $label = if ($CharacterRow) { 'CHARACTER ROW' } else { 'SIZE COMPARE' }
        Write-Output ($label + ' PLAN OK: groups=' + $env.Groups + ' width=' + $env.Width + 'mm height=' + $env.Height + 'mm offsetX=' + $DrawOffsetXmm + 'mm offsetY=' + $DrawOffsetYmm + 'mm')
        foreach ($group in $groups) { Write-Output ('  ' + ($group -join ' | ')) }
    }
    return
}
$outputRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/stage1'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$logPath = Join-Path $outputRoot ('pen-hold-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.txt')
$writer = [IO.StreamWriter]::new([IO.FileStream]::new($logPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read))
$writer.AutoFlush = $true
$serial = [IO.Ports.SerialPort]::new($Port, 115200, [IO.Ports.Parity]::None, 8, [IO.Ports.StopBits]::One)
$serial.DtrEnable = $false
$serial.RtsEnable = $false
$serial.Handshake = [IO.Ports.Handshake]::None
$serial.ReadTimeout = 150
$serial.WriteTimeout = 1000
$serial.NewLine = "`n"
$script:restartSeen = $false
$script:restoreNeeded = $false
$restored = $false
$testError = $null
function Log-Line([string]$text) { $writer.WriteLine([DateTime]::UtcNow.ToString('O') + ' ' + $text) }
function Read-Line {
    try {
        $line = $serial.ReadLine().TrimEnd("`r")
        Log-Line ('RX ' + $line)
        if ($line -match '^(rst:|ets Jul|\[MSG:Grbl_ESP32 Ver|Grbl \d)') { $script:restartSeen = $true }
        return $line
    } catch [TimeoutException] { return $null }
}
function Query([string]$command) {
    if ($command -cnotin $allowed) { throw 'Command outside diagnostic allowlist.' }
    Log-Line ('TX ' + $command)
    $serial.WriteLine($command)
    $response = [Collections.Generic.List[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds(4)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = Read-Line
        if ($null -eq $line) { continue }
        $response.Add($line)
        if ($line -match '^(error:|ALARM:)') { throw "Controller rejected $command : $line" }
        if ($line -eq 'ok') { return ($response -join "`n") }
    }
    throw "No acknowledgement for $command"
}
function Wait-Idle([int]$TimeoutSeconds = 4) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        Log-Line 'TX ?'
        $serial.Write([byte[]]@(63), 0, 1)
        $line = Read-Line
        if ($line -match '^(error:|ALARM:)') { throw $line }
        if ($script:restartSeen) { throw 'Controller restarted; motion stopped.' }
        if ($line -match '^<Idle\|MPos:([^|>]+)') {
            $axes = $Matches[1].Split(',')
            if ($axes.Length -lt 3) { throw 'Incomplete position.' }
            $z = [double]::Parse($axes[2], [Globalization.CultureInfo]::InvariantCulture)
            if ([double]::IsNaN($z) -or [double]::IsInfinity($z) -or $z -lt 0 -or $z -gt 5) { throw 'Z outside reviewed 0..5 range.' }
            $script:lastCoordinates = $axes
            return $z
        }
    }
    throw 'Fresh Idle status not received.'
}
try {
    if (($CharacterRow -or $SizeCompare) -and $PenInstalled) {
        $mode = if ($CharacterRow) { 'Character row GONG/SHI/WANG ' + $LengthMm + 'mm' } else { 'Size compare WANG 5/6/8mm' }
        Log-Line ('User confirmed installed pen clearance, fixed paper, sufficient X/Y clearance and original app disconnected. ' + $mode + '; draw offsetX=' + $DrawOffsetXmm + 'mm offsetY=' + $DrawOffsetYmm + 'mm; no settings writes.')
    }
    elseif ($CharacterWang -and $PenInstalled) { Log-Line ('User confirmed installed pen clearance, fixed paper, sufficient X/Y clearance and original app disconnected. Small character WANG ' + $LengthMm + 'mm; no settings writes.') }
    elseif ($ContinuousMotion -and $PenInstalled) { Log-Line ('User confirmed installed pen clearance, fixed paper, sufficient ' + $Axis + ' clearance and original app disconnected. Paper line length=' + $LengthMm + 'mm; no settings writes.') }
    elseif ($ContinuousMotion) { Log-Line 'User confirmed pen removed, +/-2mm X clearance and original app disconnected. Continuous X+1mm test; no settings writes.' }
    else { Log-Line 'User authorized temporary $1=255 test with pen removed; restore original $1=25. Incomplete until final restoration verified.' }
    $serial.Open()
    $settleUntil = [DateTime]::UtcNow.AddSeconds(3)
    while ([DateTime]::UtcNow -lt $settleUntil) { $null = Read-Line }
    if ($script:restartSeen) { throw 'Startup/reset detected; no parameter writes or motion allowed.' }
    $settings = Query '$$'
    $modal = Query '$G'
    $offsets = Query '$#'
    if ($settings -notmatch '(?m)^\$1=25$') { throw 'Expected original $1=25; no writes allowed.' }
    if ($modal -notmatch '\[GC:G[01] G54 G17 G21 G90 G94 ') { throw 'Unreviewed coordinate mode.' }
    if ($offsets -notmatch '\[G54:0.000,0.000,0.000\]' -or $offsets -notmatch '\[G92:0.000,0.000,0.000\]' -or $offsets -notmatch '\[TLO:0.000\]') { throw 'Unreviewed offsets.' }
    $null = Wait-Idle
    if ($ContinuousMotion) {
        $culture = [Globalization.CultureInfo]::InvariantCulture
        $coordinateIndex = if ($Axis -eq 'Y') { 1 } else { 0 }
        $startCoordinate = [double]::Parse($script:lastCoordinates[$coordinateIndex], $culture)
        if ([double]::IsNaN($startCoordinate) -or [double]::IsInfinity($startCoordinate)) { throw 'Invalid selected axis position.' }
        $lineFeed = if ($FastMotion) { 1000 } elseif ($LengthMm -eq 100) { 300 } elseif ($LengthMm -eq 10) { 60 } else { 20 }
        $returnFeed = 1800
        $motionTimeout = [int][Math]::Ceiling($LengthMm * 60.0 / $lineFeed) + 4
        $returnTimeout = [int][Math]::Ceiling($LengthMm * 60.0 / $returnFeed) + 4
        $outward = 'G1G90 ' + $Axis + ($startCoordinate + $LengthMm).ToString('0.000', $culture) + 'F' + $lineFeed
        $returning = 'G1G90 ' + $Axis + $startCoordinate.ToString('0.000', $culture) + 'F' + $returnFeed
        $allowed += @($outward, $returning)
        $null = Query 'G1G90 Z0.0F10000'
        $null = Wait-Idle
        $batch = @('G1G90 Z5.0F10000', $outward, 'G1G90 Z0.0F10000')
        if ($Square -or $CharacterWang -or $CharacterRow -or $SizeCompare) {
            $startY = [double]::Parse($script:lastCoordinates[1], $culture)
            if ([double]::IsNaN($startY) -or [double]::IsInfinity($startY)) { throw 'Invalid Y position.' }
        }
        if ($Square) {
            $squareMoves = @(
                $outward,
                ('G1G90 Y' + ($startY + 10).ToString('0.000', $culture) + 'F1000'),
                ('G1G90 X' + $startCoordinate.ToString('0.000', $culture) + 'F1000'),
                ('G1G90 Y' + $startY.ToString('0.000', $culture) + 'F1000')
            )
            $allowed += $squareMoves
            $batch = @('G1G90 Z5.0F10000') + $squareMoves + @('G1G90 Z0.0F10000')
            $motionTimeout = 8
        }
        $groups = [Collections.Generic.List[object]]::new()
        if ($CharacterGong) {
            $strokes = @(@(0,0,10,0), @(5,0,5,10), @(0,10,10,10))
            for ($strokeIndex = 0; $strokeIndex -lt $strokes.Count; $strokeIndex++) {
                $stroke = $strokes[$strokeIndex]
                $travel = 'G1G90 X' + ($startCoordinate + $stroke[0]).ToString('0.000', $culture) + 'Y' + ($startY + $stroke[1]).ToString('0.000', $culture) + 'F1800'
                $draw = 'G1G90 X' + ($startCoordinate + $stroke[2]).ToString('0.000', $culture) + 'Y' + ($startY + $stroke[3]).ToString('0.000', $culture) + 'F1000'
                $group = @($travel, 'G1G90 Z5.0F10000', $draw, 'G1G90 Z0.0F10000')
                if ($strokeIndex -eq 2) { $group += 'G1G90 X' + $startCoordinate.ToString('0.000', $culture) + 'Y' + $startY.ToString('0.000', $culture) + 'F1800' }
                $allowed += $group
                $groups.Add($group)
            }
        } elseif ($CharacterWang) {
            foreach ($group in (New-WangGroups $startCoordinate $startY)) {
                $allowed += $group
                $groups.Add($group)
            }
        } elseif ($CharacterRow -or $SizeCompare) {
            $rowPlan = New-RowPlan
            $rowBaseX = $startCoordinate + [double]$DrawOffsetXmm
            $rowBaseY = $startY + [double]$DrawOffsetYmm
            $null = Assert-RowPlan $rowPlan (New-RowGroups $rowPlan $rowBaseX $rowBaseY $startCoordinate $startY) $rowBaseX $rowBaseY
            foreach ($group in (New-RowGroups $rowPlan $rowBaseX $rowBaseY $startCoordinate $startY)) {
                $allowed += $group
                $groups.Add($group)
            }
            $motionTimeout = 20
        } elseif ($ThreeStrokes) {
            foreach ($rowOffset in @(0,5,10)) {
                $travelY = 'G1G90 Y' + ($startY + $rowOffset).ToString('0.000', $culture) + 'F1800'
                $homeX = 'G1G90 X' + $startCoordinate.ToString('0.000', $culture) + 'F1800'
                $group = @($travelY, 'G1G90 Z5.0F10000', $outward, 'G1G90 Z0.0F10000', $homeX)
                if ($rowOffset -eq 10) { $group += 'G1G90 Y' + $startY.ToString('0.000', $culture) + 'F1800' }
                $allowed += @($travelY, $homeX) + $group
                $groups.Add($group)
            }
        } else { $groups.Add($batch) }
        foreach ($group in $groups) {
            if ([Text.Encoding]::ASCII.GetByteCount(($group -join "`n") + "`n") -gt 127) { throw 'Batch exceeds bounded RX capacity; no drawing allowed.' }
            foreach ($command in $group) { if ($command -cnotin $allowed) { throw 'Unselected batch command.' } }
        }
        foreach ($batch in $groups) {
        foreach ($command in $batch) {
            if ($command -cnotin $allowed) { throw 'Unselected batch command.' }
            Log-Line ('TX QUEUED ' + $command)
        }
        # The checked batch fits within the serial RX buffer; queue together
        # to avoid deliberately leaving an idle gap between pen-down and XY motion.
        $serial.Write(($batch -join "`n") + "`n")
        $acknowledgements = 0
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ($acknowledgements -lt $batch.Count -and [DateTime]::UtcNow -lt $deadline) {
            $line = Read-Line
            if ($script:restartSeen -or $line -match '^(error:|ALARM:)') { throw 'Continuous batch interrupted; do not auto-return.' }
            if ($line -eq 'ok') { $acknowledgements++ }
        }
        if ($acknowledgements -ne $batch.Count) { throw 'Continuous batch acknowledgements incomplete.' }
        $actual = Wait-Idle -TimeoutSeconds $motionTimeout
        if ($actual -ne 0) { throw 'Pen-up after batch not confirmed.' }
        }
        $endCoordinate = [double]::Parse($script:lastCoordinates[$coordinateIndex], $culture)
        if ($Square -or $CharacterWang -or $CharacterRow -or $SizeCompare) {
            if ($actual -ne 0 -or [Math]::Abs($endCoordinate - $startCoordinate) -gt 0.001 -or [Math]::Abs([double]::Parse($script:lastCoordinates[1], $culture) - $startY) -gt 0.001) { throw 'Shape end position unexpected; XY not returned to start.' }
            if ((Query '$$') -notmatch '(?m)^\$1=25$') { throw 'Original idle setting differs.' }
            $shape = if ($CharacterGong) { 'CHARACTER GONG' } elseif ($CharacterWang) { 'CHARACTER WANG' } elseif ($CharacterRow) { 'CHARACTER ROW' } elseif ($SizeCompare) { 'SIZE COMPARE' } elseif ($ThreeStrokes) { 'THREE STROKES' } else { 'SQUARE' }
            Log-Line ($shape + ' TEST COMPLETE; XY returned, Z0, settings not written.')
        } else {
        if ($actual -ne 0 -or [Math]::Abs($endCoordinate - $startCoordinate - $LengthMm) -gt 0.001) { throw 'Continuous end position unexpected.' }
        $null = Query $returning
        $null = Wait-Idle -TimeoutSeconds $returnTimeout
        if ([Math]::Abs([double]::Parse($script:lastCoordinates[$coordinateIndex], $culture) - $startCoordinate) -gt 0.001) { throw 'Return position not confirmed.' }
        if ((Query '$$') -notmatch '(?m)^\$1=25$') { throw 'Original idle setting differs.' }
        Log-Line ('CONTINUOUS TEST COMPLETE; ' + $Axis + ' returned, Z0, settings not written.')
        }
    } else {
    $script:restoreNeeded = $true # Set before the write; an acknowledgement failure still requires restoration.
    $null = Query '$1=255'
    if ((Query '$$') -notmatch '(?m)^\$1=255$') { throw 'Hold setting not confirmed.' }
    foreach ($target in @(0,5,0)) {
        if ($script:restartSeen) { throw 'Reset detected before movement.' }
        $null = Query ('G1G90 Z' + $target + '.0F10000')
        $actual = Wait-Idle
        if ([Math]::Abs($actual - $target) -gt 0.001) { throw 'Reported pen position differs from target.' }
        Log-Line ('OBSERVE Z=' + $target + ' for 3 seconds')
        Write-Output ('观察笔夹：Z=' + $target)
        Start-Sleep -Seconds 3
    }
    }
} catch {
    $testError = $_.Exception.Message
    Log-Line ('TEST ERROR ' + $testError)
} finally {
    if ($script:restoreNeeded -and $serial.IsOpen) {
        try {
            $null = Query '$1=25'
            $restored = (Query '$$') -match '(?m)^\$1=25$'
            Log-Line ('RESTORATION VERIFIED ' + $restored)
        } catch { Log-Line ('RESTORATION ERROR ' + $_.Exception.Message) }
    }
    if ($serial.IsOpen) { $serial.Close() }
    $serial.Dispose()
    $writer.Dispose()
}
[pscustomobject]@{Log=$logPath; TestError=$testError; SettingMayHaveChanged=$script:restoreNeeded; OriginalSettingRestored=$restored} | ConvertTo-Json -Compress
if ($script:restoreNeeded -and -not $restored) { throw 'Restoration not verified. Do not continue robot operation.' }
if ($testError) { throw $testError }
