$ErrorActionPreference = 'Stop'

# Stage 2 end-to-end sample (§11.2-3): produce the real writing program through the PRODUCTION
# code path (BuiltinGlyphLibrary -> TextLayoutService -> WritingProgramService), offline-verify
# it, and save it under artifacts/stage2 for the on-machine run.
#
# Text: two lines demonstrating multi-character + space + newline + font size:
#   line 1: gong gong (space) wang     (size 6 mm body, letter spacing 2, line spacing 4)
#   line 2: yi (space) gong
# Ink extents stay inside the R6-proven safe box (X 0..41, Y about -20..+20).
#
# Chinese literals are built from code points: PS 5.1 reads this file as ANSI.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\BuiltinGlyphLibrary.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\TextLayoutService.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage2-sample-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage2.Sample.dll'
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

# ---- Production-like profile for this machine (values verified on the bench; the profile ----
# ---- itself stays Unverified, so the program honestly carries the PREVIEW ONLY comment).  ----
$p = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$p.ProfileId = 'stage2-sample'
$p.DisplayName = 'stage2 sample'
$p.PenControlMode = 'Z'
$p.PenUpValue = 0.0
$p.PenDownValue = 5.0
$p.AxisMapping = New-Object GrblPlotter.AutoWriter.Models.CoordinateMapping
$p.AxisMapping.WorkOrigin = 'G54'
$p.AxisMapping.XDirection = 1
$p.AxisMapping.YDirection = 1
$p.UsableBoundsWorkMm = New-Object GrblPlotter.AutoWriter.Models.CoordinateBounds
$p.UsableBoundsWorkMm.MinX = -5.0
$p.UsableBoundsWorkMm.MaxX = 45.0
$p.UsableBoundsWorkMm.MinY = -25.0
$p.UsableBoundsWorkMm.MaxY = 25.0
$p.UsableBoundsWorkMm.MinZ = -1.0
$p.UsableBoundsWorkMm.MaxZ = 6.0
$p.Speeds = New-Object GrblPlotter.AutoWriter.Models.MachineSpeeds
$p.Speeds.DrawingMmPerMin = 1000.0
$p.Speeds.TravelMmPerMin = 1800.0
$p.Speeds.ZMmPerMin = 10000.0
$p.Delays = New-Object GrblPlotter.AutoWriter.Models.MachineDelays
$p.Delays.PenUpMs = 0
$p.Delays.PenDownMs = 0
$p.Capabilities = New-Object GrblPlotter.AutoWriter.Models.MachineCapabilities
$p.Capabilities.RequiresContinuousPenMotion = $true

$gong = [string][char]0x5DE5
$wang = [string][char]0x738B
$yi   = [string][char]0x4E00
$text = $gong + $gong + ' ' + $wang + "`n" + $yi + ' ' + $gong
$sizeMm = 6.0

$resolve = [Func[char, GrblPlotter.AutoWriter.Services.BuiltinGlyph]]{ param($c) [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::Resolve($c) }
$strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::Layout($text, $resolve, 2.0, 4.0,
    [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::LineHeightMm, $sizeMm / [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::BodySizeMm)

$options = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
$options.RequireVerifiedRun = $false
$options.AlignStartToOrigin = $true
$options.ParkPosition = [GrblPlotter.AutoWriter.Models.WritingPoint]::new(0.0, 0.0)

$program = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($p, $strokes, $options)

# ---- Offline verification of the artifact itself ----
$log = New-Object System.Collections.Generic.List[string]
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
    $log.Add("ok - $name")
}

$violations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($program, 5.0)
Assert-True ($violations.Count -eq 0) 'continuous queue invariant holds for the whole text'

Assert-True ($program.Contains('G1 Z5 F10000') -and $program.Contains('G1 Z0 F10000')) 'pen values are the proven Z0/Z5 with F10000'
Assert-True ($program.Contains('; Start point:')) 'start-to-origin documented'
Assert-True ($program.Contains('; End park:')) 'end park documented'

$code = @($program -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
Assert-True ($code[-1] -eq 'G1 X0 Y0 F1800') 'final code line parks on the origin'

# Ink extents must stay inside the R6-proven safe box.
$xs = @(); $ys = @()
foreach ($line in $code) {
    if ($line -match '^G1 X(-?[\d.]+) Y(-?[\d.]+) ') { $xs += [double]$Matches[1]; $ys += [double]$Matches[2] }
}
$minX = ($xs | Measure-Object -Minimum).Minimum; $maxX = ($xs | Measure-Object -Maximum).Maximum
$minY = ($ys | Measure-Object -Minimum).Minimum; $maxY = ($ys | Measure-Object -Maximum).Maximum
Assert-True (($minX -ge 0.0) -and ($maxX -le 41.0)) "ink X extent $minX..$maxX inside the proven box"
Assert-True (($minY -ge -20.0) -and ($maxY -le 20.0)) "ink Y extent $minY..$maxY inside the proven box"
$log.Add("info - ink extents X $minX..$maxX  Y $minY..$maxY  lines=$($code.Count)")

$outDir = Join-Path $repoRoot 'artifacts\stage2'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$outFile = Join-Path $outDir 'stage2-writing-sample-6mm.nc'
[IO.File]::WriteAllText($outFile, $program, [Text.UTF8Encoding]::new($false))
$log.Add("info - artifact written: $outFile")
$log.Add('')
$log.Add('PASS: stage2 sample program generated through the production path and offline-verified.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\stage2-sample.txt'), $log)
