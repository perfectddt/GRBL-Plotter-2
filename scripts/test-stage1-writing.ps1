$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MachineProfileApplier.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage1-writing-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.Writing.Tests.dll'
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

& $csc /nologo /target:library /out:$assemblyPath /r:System.Runtime.Serialization.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Add-Type -Path $assemblyPath

function Assert-Contains([string] $text, [string] $needle, [string] $name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "$name did not contain '$needle'." }
}
function Assert-NotContains([string] $text, [string] $needle, [string] $name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { throw "$name unexpectedly contained '$needle'." }
}
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
}
function Assert-Throws([scriptblock] $action, [string] $name) {
    $thrown = $false
    try { & $action } catch { $thrown = $true }
    if (-not $thrown) { throw "$name was expected to reject its input." }
}
function Assert-HasBlocker($report, [string] $code, [string] $name) {
    if (-not $report.HasBlocker($code)) { throw "$name did not report blocker '$code'." }
}

function New-Profile([string] $id) {
    $p = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
    $p.ProfileId = $id
    $p.DisplayName = $id
    $p.PenControlMode = 'Z'
    $p.PenUpValue = 0.0
    $p.PenDownValue = 5.0
    $p.AxisMapping = New-Object GrblPlotter.AutoWriter.Models.CoordinateMapping
    $p.AxisMapping.WorkOrigin = 'G54'
    $p.UsableBoundsWorkMm = New-Object GrblPlotter.AutoWriter.Models.CoordinateBounds
    $p.UsableBoundsWorkMm.MinX = 0.0
    $p.UsableBoundsWorkMm.MaxX = 30.0
    $p.UsableBoundsWorkMm.MinY = 0.0
    $p.UsableBoundsWorkMm.MaxY = 30.0
    $p.UsableBoundsWorkMm.MinZ = -1.0
    $p.UsableBoundsWorkMm.MaxZ = 6.0
    $p.Speeds = New-Object GrblPlotter.AutoWriter.Models.MachineSpeeds
    $p.Speeds.DrawingMmPerMin = 1000.0
    $p.Speeds.TravelMmPerMin = 2000.0
    $p.Speeds.ZMmPerMin = 300.0
    $p.Delays = New-Object GrblPlotter.AutoWriter.Models.MachineDelays
    $p.Delays.PenUpMs = 0
    $p.Delays.PenDownMs = 0
    $p.Capabilities = New-Object GrblPlotter.AutoWriter.Models.MachineCapabilities
    return $p
}

function New-Pt([double] $x, [double] $y) {
    return [GrblPlotter.AutoWriter.Models.WritingPoint]::new($x, $y)
}

function New-Stroke([double[]] $coords) {
    $s = New-Object GrblPlotter.AutoWriter.Models.WritingStroke
    for ($i = 0; $i -lt $coords.Length; $i += 2) {
        $s.Points.Add((New-Pt $coords[$i] $coords[$i + 1]))
    }
    return $s
}

# Three strokes forming the character gong (top bar, vertical, bottom bar) inside a 10x10 area.
function New-GongStrokes {
    $strokes = New-Object 'System.Collections.Generic.List[GrblPlotter.AutoWriter.Models.WritingStroke]'
    $strokes.Add((New-Stroke @(1, 11, 11, 11)))
    $strokes.Add((New-Stroke @(6, 1, 6, 11)))
    $strokes.Add((New-Stroke @(1, 1, 11, 1)))
    return ,$strokes
}

function New-Options([bool] $requireVerified) {
    $o = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
    $o.RequireVerifiedRun = $requireVerified
    return $o
}

# 1) Continuous, verified profile: no dwell, pen-down immediately followed by XY, stroke order kept.
$continuous = New-Profile 'continuous-verified'
$continuous.VerificationStatus = 'Verified'
$continuous.VerifiedAtUtc = [DateTime]::UtcNow
$continuous.VerificationEvidence = New-Object 'System.Collections.Generic.List[string]'
$continuous.VerificationEvidence.Add('stage1 writing bench record')
$continuous.Capabilities.RequiresContinuousPenMotion = $true
$program = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, (New-GongStrokes), (New-Options $true))
Assert-Contains $program 'G21' 'units'
Assert-Contains $program 'G90' 'absolute'
Assert-Contains $program 'G94' 'feed mode'
Assert-Contains $program 'G54' 'work frame'
Assert-Contains $program 'G1 Z0 F300' 'pen-up value'
Assert-Contains $program 'G1 Z5 F300' 'pen-down value'
Assert-Contains $program 'G1 X11 Y11 F1000' 'drawing feed'
Assert-Contains $program 'G1 X1 Y11 F2000' 'travel feed'
Assert-Contains $program 'do not wait for Idle between blocks' 'continuous requirement comment'
Assert-NotContains $program 'G4' 'dwell'
Assert-NotContains $program 'G0' 'rapid'
Assert-NotContains $program 'M0' 'program stop'
Assert-NotContains $program 'PREVIEW ONLY' 'verified run must not be marked preview'
$violations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($program, 5.0)
Assert-True ($violations.Count -eq 0) 'continuous queue violations'
Assert-Contains $program 'Axis direction is not fully declared' 'undeclared direction is surfaced'

# Stroke order: top bar, then vertical, then bottom bar (each travel precedes its pen-down and draw).
$iTop = $program.IndexOf('G1 X11 Y11 F1000', [StringComparison]::Ordinal)
$iVertStart = $program.IndexOf('G1 X6 Y1 F2000', [StringComparison]::Ordinal)
$iVertEnd = $program.IndexOf('G1 X6 Y11 F1000', [StringComparison]::Ordinal)
$iBottomStart = $program.IndexOf('G1 X1 Y1 F2000', [StringComparison]::Ordinal)
$iBottomEnd = $program.IndexOf('G1 X11 Y1 F1000', [StringComparison]::Ordinal)
Assert-True ($iTop -ge 0 -and $iVertStart -gt $iTop -and $iVertEnd -gt $iVertStart -and $iBottomStart -gt $iVertEnd -and $iBottomEnd -gt $iBottomStart) 'stroke order preserved'

# Detector flags a broken queue (dwell inserted after pen-down).
$broken = $program.Replace('G1 Z5 F300', "G1 Z5 F300`r`nG4 P0.1")
$brokenViolations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($broken, 5.0)
Assert-True ($brokenViolations.Count -ge 2) 'detector catches dwell and missing following XY'

# 2) Non-continuous profile with delays keeps its synchronising dwell (regression for other machines).
$withDwell = New-Profile 'dwell-machine'
$withDwell.Delays.PenUpMs = 100
$withDwell.Delays.PenDownMs = 200
$dwellProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($withDwell, (New-GongStrokes), (New-Options $false))
Assert-Contains $dwellProgram 'G4 P0.1' 'pen-up delay'
Assert-Contains $dwellProgram 'G4 P0.2' 'pen-down delay'
Assert-Contains $dwellProgram 'PREVIEW ONLY' 'preview marker'

# 3) Continuous profile with a positive dwell is rejected.
$badContinuous = New-Profile 'continuous-bad-dwell'
$badContinuous.Capabilities.RequiresContinuousPenMotion = $true
$badContinuous.Delays.PenDownMs = 50
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($badContinuous, (New-GongStrokes), (New-Options $false)) } 'continuous with positive dwell'

# 4) Unknown pen values are rejected (never silently filled).
$unknownPen = New-Profile 'unknown-pen'
$unknownPen.PenUpValue = $null
$unknownPen.PenDownValue = $null
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($unknownPen, (New-GongStrokes), (New-Options $false)) } 'unknown pen values'

# 5) Out-of-bounds stroke is rejected.
$outside = New-Object 'System.Collections.Generic.List[GrblPlotter.AutoWriter.Models.WritingStroke]'
$outside.Add((New-Stroke @(1, 1, 40, 1)))
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, $outside, (New-Options $true)) } 'out-of-bounds stroke'

# 6) Single-point stroke is rejected instead of being dropped silently.
$single = New-Object 'System.Collections.Generic.List[GrblPlotter.AutoWriter.Models.WritingStroke]'
$single.Add((New-Stroke @(1, 1)))
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, $single, (New-Options $true)) } 'single-point stroke'

# 7) RequireVerifiedRun with an unverified profile is rejected, but preview is allowed.
$unverified = New-Profile 'unverified'
$previewProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($unverified, (New-GongStrokes), (New-Options $false))
Assert-Contains $previewProgram 'PREVIEW ONLY' 'preview for unverified profile'
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($unverified, (New-GongStrokes), (New-Options $true)) } 'unverified run'

# 8) Apply report: unknown stays unknown; only a fully verified profile can start writing.
$reportUnverified = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($unverified)
Assert-False $reportUnverified.CanStartWriting 'unverified report cannot start'
Assert-True $reportUnverified.CanGeneratePreview 'unverified report can preview'
Assert-False $reportUnverified.IsVerified 'unverified status'
Assert-HasBlocker $reportUnverified 'unverified' 'unverified report'
$unknownItems = @($reportUnverified.Items | Where-Object { -not $_.Known })
Assert-True ($unknownItems.Count -gt 0) 'unknown items surfaced'
Assert-True ($reportUnverified.ToText().Length -gt 0) 'report text rendered'

$reportContinuous = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($continuous)
Assert-True $reportContinuous.CanStartWriting 'verified continuous report can start'
Assert-True $reportContinuous.PenValuesKnown 'pen values known'
Assert-True $reportContinuous.BoundsKnown 'bounds known'
Assert-True ($reportContinuous.Blockers.Count -eq 0) 'verified report has no blockers'

$missingSpeeds = New-Profile 'missing-speeds'
$missingSpeeds.Speeds = $null
$reportMissing = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($missingSpeeds)
Assert-False $reportMissing.SpeedsKnown 'missing speeds unknown'
Assert-False $reportMissing.CanGeneratePreview 'missing speeds cannot preview'
Assert-HasBlocker $reportMissing 'speeds-unknown' 'missing speeds report'

# 9) Apply report honours explicit operator overrides: the same profile that has no speeds in the
#    file can preview once the operator supplies them, and each supplied value is flagged as overridden.
$reportOverrideProfile = New-Profile 'override-report'
$reportOverrideProfile.Speeds = $null
$overrides = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
$overrides.DrawingMmPerMin = 1000.0
$overrides.TravelMmPerMin = 1800.0
$overrides.ZMmPerMin = 10000.0
$reportOverride = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($reportOverrideProfile, $overrides)
Assert-True $reportOverride.SpeedsKnown 'override speeds known'
Assert-True $reportOverride.CanGeneratePreview 'override enables preview'
Assert-False ($reportOverride.HasBlocker('speeds-unknown')) 'override clears the speeds blocker'
$overriddenItems = @($reportOverride.Items | Where-Object { $_.Overridden })
Assert-True ($overriddenItems.Count -eq 3) 'three overridden speed items surfaced'
Assert-True (@($overriddenItems | Where-Object { $_.Value -eq '1000' }).Count -ge 1) 'override value rendered'

# The same profile with no override still reports unknown speeds (the file is never rewritten).
$reportNoOverride = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($reportOverrideProfile)
Assert-False $reportNoOverride.SpeedsKnown 'no override keeps speeds unknown'
Assert-HasBlocker $reportNoOverride 'speeds-unknown' 'no override keeps speeds blocker'

# Overrides for pen values alone do not change the profile, only which value the report would use.
$reportPenOverride = New-Profile 'pen-override'
$reportPenOverride.PenUpValue = $null
$reportPenOverride.PenDownValue = $null
$penOverrides = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
$penOverrides.PenUpValue = 0.0
$penOverrides.PenDownValue = 5.0
$reportPen = [GrblPlotter.AutoWriter.Services.MachineProfileApplier]::BuildReport($reportPenOverride, $penOverrides)
Assert-True $reportPen.PenValuesKnown 'pen overrides make pen values known'
Assert-True (@($reportPen.Items | Where-Object { $_.Overridden }).Count -eq 2) 'two pen overrides surfaced'

# 10) The profile's declared axis direction is applied when emitting machine work coordinates.
#     Shared-paper convention for this machine: board $3 = 5 (Y invert bit cleared), so +Y moves
#     away from the operator (towards paper "up") and the shipped profile declares yDirection = +1.
#     A machine whose +Y points towards the operator declares yDirection = -1 and gets mirrored Y.
$flipY = New-Profile 'direction-flip-y'
$flipY.AxisMapping.XDirection = 1
$flipY.AxisMapping.YDirection = -1
$flipY.UsableBoundsWorkMm.MinX = -30.0
$flipY.UsableBoundsWorkMm.MaxX = 30.0
$flipY.UsableBoundsWorkMm.MinY = -30.0
$flipY.UsableBoundsWorkMm.MaxY = 30.0
$flipYProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($flipY, (New-GongStrokes), (New-Options $false))
Assert-Contains $flipYProgram 'G1 X1 Y-11 F2000' 'flipped Y travel'
Assert-Contains $flipYProgram 'G1 X6 Y-11 F1000' 'flipped Y vertical draw'
Assert-Contains $flipYProgram 'G1 X11 Y-1 F1000' 'flipped Y bottom bar draw'
Assert-NotContains $flipYProgram 'Y11' 'no untransformed Y remains on a flipped profile'
Assert-NotContains $flipYProgram 'Axis direction is not fully declared' 'a declared direction suppresses the warning'
$flipViolations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($flipYProgram, 5.0)
Assert-True ($flipViolations.Count -eq 0) 'flipped program keeps the continuous queue invariant'

# Production configuration of this machine (board $3 = 5): yDirection = +1 passes the paper
# coordinates straight through, so the emitted runner and the 2D preview agree with the paper.
$passY = New-Profile 'direction-pass-y'
$passY.AxisMapping.XDirection = 1
$passY.AxisMapping.YDirection = 1
$passYProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($passY, (New-GongStrokes), (New-Options $false))
Assert-Contains $passYProgram 'G1 X1 Y11 F2000' 'yDirection +1 leaves travel unchanged'
Assert-Contains $passYProgram 'G1 X6 Y11 F1000' 'yDirection +1 leaves the vertical draw unchanged'
Assert-Contains $passYProgram 'G1 X11 Y1 F1000' 'yDirection +1 leaves the bottom bar unchanged'
Assert-NotContains $passYProgram 'Y-11' 'yDirection +1 does not negate Y'
Assert-NotContains $passYProgram 'Axis direction is not fully declared' 'a declared +1 direction suppresses the warning'

# X direction is consumed the same way (this machine declares xDirection = 1, so it is unchanged),
# and a mirrored X still validates against the signed work bounds.
$flipX = New-Profile 'direction-flip-x'
$flipX.AxisMapping.XDirection = -1
$flipX.AxisMapping.YDirection = 1
$flipX.UsableBoundsWorkMm.MinX = -30.0
$flipX.UsableBoundsWorkMm.MaxX = 30.0
$flipX.UsableBoundsWorkMm.MinY = -30.0
$flipX.UsableBoundsWorkMm.MaxY = 30.0
$flipXProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($flipX, (New-GongStrokes), (New-Options $false))
Assert-Contains $flipXProgram 'G1 X-1 Y11 F2000' 'flipped X travel'
Assert-Contains $flipXProgram 'G1 X-11 Y1 F1000' 'flipped X draw leaves Y unchanged'

# A stroke that only leaves the bounds AFTER the direction transform is still rejected.
$flipOutside = New-Profile 'direction-flip-outside'
$flipOutside.AxisMapping.YDirection = -1
$flipOutside.UsableBoundsWorkMm.MinX = 0.0
$flipOutside.UsableBoundsWorkMm.MaxX = 30.0
$flipOutside.UsableBoundsWorkMm.MinY = 0.0
$flipOutside.UsableBoundsWorkMm.MaxY = 30.0
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($flipOutside, (New-GongStrokes), (New-Options $false)) } 'transform that leaves the signed bounds'

# 11) Optional end park: raises the pen and returns to the given work point at the travel feed,
#     is documented in the program, is transformed by the axis directions, and is bounds-checked.
$parkOptions = New-Options $false
$parkOptions.ParkPosition = (New-Pt 0 0)
$parkProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, (New-GongStrokes), $parkOptions)
Assert-Contains $parkProgram 'End park' 'end park is documented in the program'
Assert-Contains $parkProgram 'G1 X0 Y0 F2000' 'end park returns to the origin at the travel feed'
$parkCode = @($parkProgram -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
Assert-True ($parkCode[-1] -eq 'G1 X0 Y0 F2000') 'end park is the final code line'
Assert-True ($parkCode[-2] -eq 'G1 Z0 F300') 'pen is raised before the end park'
$parkViolations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($parkProgram, 5.0)
Assert-True ($parkViolations.Count -eq 0) 'end park keeps the continuous queue invariant'

# Without a park position the program still stops at the end of the last stroke (no return).
Assert-NotContains $program 'G1 X0 Y0' 'a program without a park position never returns to the origin'

# The end park goes through the same direction transform as the strokes.
$flipParkOptions = New-Options $false
$flipParkOptions.ParkPosition = (New-Pt 2 3)
$flipParkProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($flipY, (New-GongStrokes), $flipParkOptions)
Assert-Contains $flipParkProgram 'G1 X2 Y-3 F2000' 'end park honours the declared axis direction'

# An end park that leaves the verified work bounds is rejected like a stroke point.
$badParkOptions = New-Options $false
$badParkOptions.ParkPosition = (New-Pt -5 0)
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, (New-GongStrokes), $badParkOptions) } 'out-of-bounds end park'

# 12) Start-point rule: with AlignStartToOrigin the whole set moves so the program's first
#     pen-down point is the work origin (X0 Y0), while the shape itself is untouched. The built-in
#     geometry starts at (1,11), so this is visible in the emitted coordinates.
#
#     The alignment is a translation of the whole set, so a first stroke that starts at the top of
#     the character makes the shape extend below the origin. The profile therefore has to declare a
#     usable area that contains the origin and that extension; an alignment that would leave the
#     declared area is rejected like any other out-of-bounds point instead of being silently
#     clipped. The profile below is the machine with a symmetric usable area.
$alignProfile = New-Profile 'start-aligned'
$alignProfile.AxisMapping.XDirection = 1
$alignProfile.AxisMapping.YDirection = 1
$alignProfile.UsableBoundsWorkMm.MinX = -30.0
$alignProfile.UsableBoundsWorkMm.MaxX = 30.0
$alignProfile.UsableBoundsWorkMm.MinY = -30.0
$alignProfile.UsableBoundsWorkMm.MaxY = 30.0
$alignProfile.Capabilities.RequiresContinuousPenMotion = $true
$alignProfile.VerificationStatus = 'Verified'
$alignProfile.VerifiedAtUtc = [DateTime]::UtcNow
$alignProfile.VerificationEvidence = New-Object 'System.Collections.Generic.List[string]'
$alignProfile.VerificationEvidence.Add('stage1 start-point bench record')

$alignedOptions = New-Options $true
$alignedOptions.AlignStartToOrigin = $true
$alignedProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($alignProfile, (New-GongStrokes), $alignedOptions)
Assert-Contains $alignedProgram 'Start point: the first pen-down point' 'aligned program documents its start point'
Assert-NotContains $alignedProgram 'G1 X1 Y11' 'aligned program no longer starts at the design margin'

$alignedCode = @($alignedProgram -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
$alignedFirstXy = $alignedCode | Where-Object { $_.StartsWith('G1 X', [StringComparison]::Ordinal) } | Select-Object -First 1
Assert-True ($alignedFirstXy -eq 'G1 X0 Y0 F2000') 'the first pen-down point of an aligned program is the work origin'

# The first touch is followed immediately by the pen-down, as the continuous queue requires.
$alignedFirstIndex = [Array]::IndexOf($alignedCode, $alignedFirstXy)
Assert-True ($alignedCode[$alignedFirstIndex + 1] -eq 'G1 Z5 F300') 'the aligned program lowers the pen right after it reaches the origin'
Assert-True (@([GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($alignedProgram, 5.0)).Count -eq 0) 'aligned program keeps the continuous queue invariant'

# Only the position moved: the 10 x 10 mm shape, its stroke order and its spacing are unchanged.
$alignedX = @(); $alignedY = @()
foreach ($line in $alignedCode) {
    if ($line -match '^G1 X(-?[\d.]+) Y(-?[\d.]+) ') {
        $alignedX += [double]$Matches[1]
        $alignedY += [double]$Matches[2]
    }
}
Assert-True ((($alignedX | Measure-Object -Minimum).Minimum -eq 0.0) -and (($alignedX | Measure-Object -Maximum).Maximum -eq 10.0)) 'the aligned shape keeps its 10 mm X span'
Assert-True ((($alignedY | Measure-Object -Minimum).Minimum -eq -10.0) -and (($alignedY | Measure-Object -Maximum).Maximum -eq 0.0)) 'the aligned shape keeps its 10 mm Y span, now hanging below the origin'
Assert-Contains $alignedProgram 'G1 X10 Y0 F1000' 'the aligned top bar is still 10 mm long'
Assert-Contains $alignedProgram 'G1 X5 Y-10 F2000' 'the aligned vertical still starts at the same offset'

# An alignment that would leave the declared work area is reported, never clipped.
Assert-Throws { [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, (New-GongStrokes), $alignedOptions) } 'an alignment that leaves the declared work area'

# The end park shares the origin, so the program both starts and finishes on the operator's zero.
$alignedParkOptions = New-Options $true
$alignedParkOptions.AlignStartToOrigin = $true
$alignedParkOptions.ParkPosition = (New-Pt 0 0)
$alignedParkProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($alignProfile, (New-GongStrokes), $alignedParkOptions)
$alignedParkCode = @($alignedParkProgram -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
Assert-True ($alignedParkCode[-1] -eq 'G1 X0 Y0 F2000') 'the aligned program parks on the origin as its final code line'
Assert-True (@($alignedParkCode | Where-Object { $_ -eq 'G1 X0 Y0 F2000' }).Count -eq 2) 'the aligned program starts and ends on the work origin'

# The translation is applied in paper coordinates, before the axis transform, so a mirrored Y
# still lands its first touch exactly on the origin.
$flipAlignOptions = New-Options $false
$flipAlignOptions.AlignStartToOrigin = $true
$flipAlignProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($flipY, (New-GongStrokes), $flipAlignOptions)
$flipAlignCode = @($flipAlignProgram -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
$flipAlignFirstXy = $flipAlignCode | Where-Object { $_.StartsWith('G1 X', [StringComparison]::Ordinal) } | Select-Object -First 1
Assert-True ($flipAlignFirstXy -eq 'G1 X0 Y0 F2000') 'the origin start survives a mirrored Y axis'
Assert-Contains $flipAlignProgram 'G1 X5 Y0 F1000' 'the mirrored aligned shape keeps its geometry'

# Without the option the raw design coordinates are emitted, which is the exact baseline the
# earlier groups in this file assert against.
$rawOptions = New-Options $true
$rawProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($continuous, (New-GongStrokes), $rawOptions)
Assert-True ($rawProgram -eq $program) 'leaving the option off reproduces the raw design coordinates exactly'

Write-Host ('PASS: stage1 writing program and apply report validated. Temporary artifacts: ' + $tempRoot)
