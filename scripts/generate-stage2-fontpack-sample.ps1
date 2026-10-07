$ErrorActionPreference = 'Stop'

# Stage 2 font-pack paper sample (font route A, doc section 2): produce the real
# writing program through the PRODUCTION code path (GlyphFontPackService ->
# TextLayoutService -> WritingProgramService), offline-verify it, and save it
# under artifacts/stage2 for the on-machine run.
#
# Text, two lines, body size 6 mm, letter spacing 2 mm, line spacing 4 mm:
#   line 1: yong he guo gong   (U+6C38 U+548C U+56FD U+5DE5)
#   line 2: wang yi er san     (U+738B U+4E00 U+4E8C U+4E09)
# All eight characters come from the HanziWriter pilot pack (true stroke-order
# centerlines). Note: align-start-to-origin pins the FIRST pen-down point to the
# origin; yong's leftmost ink reaches about 1.5 mm LEFT of the origin - keep
# that in mind when positioning the paper.
#
# PURE ASCII FILE (PS 5.1 ANSI pitfall - see test-stage2-fontpack.ps1).

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\BuiltinGlyphLibrary.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\TextLayoutService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\GlyphFontPackService.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}
$packPath = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Fonts\hanziwriter-pilot.fontpack.json'
if (-not (Test-Path -LiteralPath $packPath)) { throw "Missing font pack: $packPath" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage2-fontpack-sample-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage2.FontPack.Sample.dll'
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

# ---- Production-like profile (same proven values as the stage2 sample) ------
$p = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$p.ProfileId = 'stage2-fontpack-sample'
$p.DisplayName = 'stage2 fontpack sample'
$p.PenControlMode = 'Z'
$p.PenUpValue = 0.0
$p.PenDownValue = 5.0
$p.AxisMapping = New-Object GrblPlotter.AutoWriter.Models.CoordinateMapping
$p.AxisMapping.WorkOrigin = 'G54'
$directionProfile = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts\stage1\machine-profile-continuous-pen-confirmed.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$p.AxisMapping.XDirection = $directionProfile.axisMapping.xDirection
$p.AxisMapping.YDirection = $directionProfile.axisMapping.yDirection
if ($p.AxisMapping.XDirection -notin @(-1, 1) -or $p.AxisMapping.YDirection -notin @(-1, 1)) {
    throw 'The machine profile must declare both axis directions before sample generation.'
}
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

$yong = [string][char]0x6C38
$he   = [string][char]0x548C
$guo  = [string][char]0x56FD
$gong = [string][char]0x5DE5
$wang = [string][char]0x738B
$yi   = [string][char]0x4E00
$er   = [string][char]0x4E8C
$san  = [string][char]0x4E09
$text = ($yong + $he + $guo + $gong) + "`n" + ($wang + $yi + $er + $san)
$sizeMm = 6.0

$pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile($packPath)
$strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::Layout($text, $pack.ToResolver(), 2.0, 4.0,
    [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::LineHeightMm, $sizeMm / [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::BodySizeMm)

$options = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
$options.RequireVerifiedRun = $false
$options.AlignStartToOrigin = $true
$options.ParkPosition = [GrblPlotter.AutoWriter.Models.WritingPoint]::new(0.0, 0.0)

$program = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($p, $strokes, $options)

# ---- Offline verification of the artifact itself ----------------------------
$log = New-Object System.Collections.Generic.List[string]
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
    $log.Add("ok - $name")
}

$violations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($program, 5.0)
Assert-True ($violations.Count -eq 0) 'continuous queue invariant holds for the whole text'
Assert-True ($program.Contains('G1 Z5 F10000') -and $program.Contains('G1 Z0 F10000')) 'pen values are the proven Z0/Z5 with F10000'
$code = @($program -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
Assert-True ($code[-1] -eq 'G1 X0 Y0 F1800') 'final code line parks on the origin'

$xs = @(); $ys = @()
foreach ($line in $code) {
    if ($line -match '^G1 X(-?[\d.]+) Y(-?[\d.]+) ') { $xs += [double]$Matches[1]; $ys += [double]$Matches[2] }
}
$minX = ($xs | Measure-Object -Minimum).Minimum; $maxX = ($xs | Measure-Object -Maximum).Maximum
$minY = ($ys | Measure-Object -Minimum).Minimum; $maxY = ($ys | Measure-Object -Maximum).Maximum
Assert-True (($minX -ge -5.0) -and ($maxX -le 45.0)) "ink X extent $minX..$maxX inside the profile bounds"
Assert-True (($minY -ge -25.0) -and ($maxY -le 25.0)) "ink Y extent $minY..$maxY inside the profile bounds"
$log.Add("info - ink extents X $minX..$maxX  Y $minY..$maxY  lines=$($code.Count)")
$log.Add('')

$outDir = Join-Path $repoRoot 'artifacts\stage2'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$outFile = Join-Path $outDir 'stage2-fontpack-sample-6mm.nc'
[IO.File]::WriteAllText($outFile, $program, [Text.UTF8Encoding]::new($false))
$log.Add("info - artifact written: $outFile")
$log.Add('')
$log.Add('PASS: font-pack sample program generated through the production path and offline-verified.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\stage2-fontpack-sample.txt'), $log)
Write-Host ($log -join [Environment]::NewLine)
