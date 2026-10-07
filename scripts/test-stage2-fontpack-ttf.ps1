$ErrorActionPreference = 'Stop'

# Stage 2 route-B offline suite (TTF skeleton extraction, doc/2-font-route section 3).
# Generates a SMALL pack from the Windows-bundled KaiTi through the real python
# pipeline, loads it with the production GlyphFontPackService, and asserts the
# same invariants as the route-A suite. Never connects to a controller.
#
# Requirements: python with numpy, fonttools, pillow (see artifacts report or
# pip install numpy fonttools pillow). Fails loudly when the font or python
# environment is missing - no silent skips.
#
# PURE ASCII FILE (PS 5.1 ANSI/GBK pitfall - see test-stage2-fontpack.ps1).

$repoRoot = Split-Path -Parent $PSScriptRoot
$fontPath = Join-Path $env:WINDIR 'Fonts\simkai.ttf'
if (-not (Test-Path -LiteralPath $fontPath)) { throw "Route-B suite needs the Windows KaiTi font at $fontPath." }

$generator = Join-Path $repoRoot 'scripts\generate-stage2-fontpack-ttf.py'
if (-not (Test-Path -LiteralPath $generator)) { throw "Missing generator: $generator" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage2-ttf-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$packPath = Join-Path $tempRoot 'fontpack-ttf-test.json'
$reportPath = Join-Path $tempRoot 'fontpack-ttf-test-report.txt'

& python $generator --limit 24 --out $packPath --report $reportPath
if ($LASTEXITCODE -ne 0) { throw "Route-B pack generation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $packPath)) { throw 'Route-B pack was not written.' }

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
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage2.Ttf.dll'
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

$log = New-Object System.Collections.Generic.List[string]
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
    $log.Add("ok - $name")
}
function Measure-Ink([GrblPlotter.AutoWriter.Services.BuiltinGlyph] $glyph) {
    $xs = @(); $ys = @()
    foreach ($stroke in $glyph.Strokes) {
        foreach ($point in $stroke.Points) { $xs += $point.X; $ys += $point.Y }
    }
    return @{
        MinX = ($xs | Measure-Object -Minimum).Minimum; MaxX = ($xs | Measure-Object -Maximum).Maximum
        MinY = ($ys | Measure-Object -Minimum).Minimum; MaxY = ($ys | Measure-Object -Maximum).Maximum
    }
}

# ---- 1. Load and sanity-check ------------------------------------------------
$pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile($packPath)
$packJson = Get-Content -LiteralPath $packPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedCount = @($packJson.glyphs).Count
Assert-True ($pack.GlyphCount -eq $expectedCount) "pack glyph count matches JSON ($($pack.GlyphCount))"
Assert-True ($pack.GlyphCount -eq 24) 'limit-24 pilot generation produced 24 glyphs'
Assert-True ($packJson.bodySizeMm -eq 10.0 -and $packJson.baselineMm -eq 1.0) 'calibration: body 10mm on baseline 1mm'

$yi   = [char]0x4E00   # U+4E00 (thin bar)
$san  = [char]0x4E09   # U+4E09 (three bars, mid height)
$zhong= [char]0x4E2D   # U+4E2D (full height)
$yan  = [char]0x4E25   # U+4E25 (full height, dense)
$gu   = [char]0x9AA8   # U+9AA8 (outside the pack)

# ---- 2. Resolution chain ------------------------------------------------------
Assert-True ($pack.Contains($yi) -and $pack.Contains($zhong) -and $pack.Contains($yan)) 'sample characters resolve from the pack'
$space = $pack.ResolveWithFallback(' ')
Assert-True ($space.AdvanceMm -eq [double]6.0 -and $space.Strokes.Count -eq 0) 'space falls back to the built-in advance, no strokes'
$thrown = $false
try { $pack.ResolveWithFallback($gu) | Out-Null } catch [InvalidOperationException] { $thrown = $true }
Assert-True $thrown 'missing character fails loudly'

# ---- 3. Geometry ---------------------------------------------------------------
$zhong_ink = Measure-Ink $pack.Resolve($zhong)
$zhongHeight = [Math]::Round($zhong_ink.MaxY - $zhong_ink.MinY, 3)
Assert-True (($zhong_ink.MaxY - $zhong_ink.MinY) -gt 8.5 -and ($zhong_ink.MaxY - $zhong_ink.MinY) -lt 10.8) "full-height glyph U+4E2D is ~10mm tall ($zhongHeight)"
$yan_ink = Measure-Ink $pack.Resolve($yan)
Assert-True (($yan_ink.MaxY - $yan_ink.MinY) -gt 8.5 -and ($yan_ink.MaxY - $yan_ink.MinY) -lt 10.8) "full-height glyph U+4E25 is ~10mm tall ($([Math]::Round($yan_ink.MaxY - $yan_ink.MinY, 3)))"
$yi_ink = Measure-Ink $pack.Resolve($yi)
Assert-True (($yi_ink.MaxY - $yi_ink.MinY) -lt 2.0) "thin glyph U+4E00 stays a thin bar ($([Math]::Round($yi_ink.MaxY - $yi_ink.MinY, 3)))"
$san_ink = Measure-Ink $pack.Resolve($san)
Assert-True (($san_ink.MaxY - $san_ink.MinY) -gt 3.5 -and ($san_ink.MaxY - $san_ink.MinY) -lt 7.5) "mid glyph U+4E09 keeps its mid height ($([Math]::Round($san_ink.MaxY - $san_ink.MinY, 3)))"
foreach ($symbol in @($yi, $san, $zhong, $yan)) {
    $ink = Measure-Ink $pack.Resolve($symbol)
    $strokeCount = $pack.Resolve($symbol).Strokes.Count
    Assert-True ($strokeCount -ge 1) "glyph U+$('0x{0:X4}' -f [int]$symbol) has at least one stroke ($strokeCount)"
    Assert-True (($ink.MinX -ge -0.5) -and ($ink.MaxX -le 12.5) -and ($ink.MinY -ge -0.5) -and ($ink.MaxY -le 12.5)) "glyph U+$('0x{0:X4}' -f [int]$symbol) ink stays inside the 12mm em box (+-0.5mm, descenders dip)"
}

# ---- 4. Layout + end-to-end program --------------------------------------------
$resolve = $pack.ToResolver()
$text = ([string]$yi + $san + $zhong + $yan) + "`n" + [string]$zhong + $yan + $san + $yi
$expectedStrokes = 0
foreach ($symbol in $text.ToCharArray()) {
    if ($symbol -eq "`r" -or $symbol -eq "`n") { continue }
    $expectedStrokes += $pack.ResolveWithFallback($symbol).Strokes.Count
}
$strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::Layout($text, $resolve, 0.5, 0.0,
    [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::LineHeightMm, 0.6)
Assert-True ($strokes.Count -eq $expectedStrokes) "layout preserves every stroke ($($strokes.Count))"

$p = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$p.ProfileId = 'stage2-ttf-test'
$p.DisplayName = 'stage2 ttf test'
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

$options = New-Object GrblPlotter.AutoWriter.Services.WritingProgramOptions
$options.RequireVerifiedRun = $false
$options.AlignStartToOrigin = $true
$options.ParkPosition = [GrblPlotter.AutoWriter.Models.WritingPoint]::new(0.0, 0.0)
$program = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($p, $strokes, $options)

$violations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($program, 5.0)
Assert-True ($violations.Count -eq 0) 'continuous queue invariant holds for the route-B program'
Assert-True ($program.Contains('G1 Z5 F10000') -and $program.Contains('G1 Z0 F10000')) 'pen values are the proven Z0/Z5 with F10000'
$code = @($program -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith(';') })
Assert-True ($code[-1] -eq 'G1 X0 Y0 F1800') 'final code line parks on the origin'

$outDir = Join-Path $repoRoot 'artifacts\stage0'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$log.Add('')
$log.Add('PASS: route-B (TTF skeleton) offline suite.')
[IO.File]::WriteAllLines((Join-Path $outDir 'stage2-fontpack-ttf-test.txt'), $log)
Write-Host ($log -join [Environment]::NewLine)
