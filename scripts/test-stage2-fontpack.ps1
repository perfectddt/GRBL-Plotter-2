$ErrorActionPreference = 'Stop'

# Stage 2 font-pack offline suite (font route A, doc section 2).
# Compiles the production sources standalone (same pattern as the other suites),
# loads the committed pilot font pack, and asserts geometry, the resolution
# chain (pack -> built-in -> loud failure), layout with mixed sources, and one
# end-to-end program generation. Never connects to a controller.
#
# PURE ASCII FILE - PS 5.1 reads .ps1 as ANSI/GBK; Chinese text in comments or
# string literals gets misread (a CJK char's last UTF-8 byte can even swallow
# the newline and comment out the NEXT line - the exact bug this suite once
# hit). Chinese characters are built from code points; assertions match ASCII
# substrings only.

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

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage2-fontpack-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage2.FontPack.dll'
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

# ---- 1. Load and sanity-check the pack -------------------------------------
$pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile($packPath)
$packJson = Get-Content -LiteralPath $packPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedCount = @($packJson.glyphs).Count
Assert-True ($pack.GlyphCount -eq $expectedCount) "pack glyph count matches JSON ($($pack.GlyphCount))"
Assert-True ($pack.GlyphCount -gt 500) 'pilot pack covers >500 characters'
Assert-True ($packJson.bodySizeMm -eq 10.0 -and $packJson.baselineMm -eq 1.0) 'calibration: body 10mm on baseline 1mm'
Assert-True ($pack.License -like '*Arphic*') 'license attribution present'

$yong = [char]0x6C38   # U+6C38
$he   = [char]0x548C   # U+548C
$guo  = [char]0x56FD   # U+56FD
$gong = [char]0x5DE5   # U+5DE5
$wang = [char]0x738B   # U+738B
$yi   = [char]0x4E00   # U+4E00
$er   = [char]0x4E8C   # U+4E8C
$san  = [char]0x4E09   # U+4E09
$mu   = [char]0x6728   # U+6728
$gu   = [char]0x9AA8   # U+9AA8 (deliberately outside the pilot pack)

# ---- 2. Resolution chain ----------------------------------------------------
Assert-True ($pack.Contains($wang) -and $pack.Contains($yong) -and $pack.Contains($mu)) 'sample characters resolve from the pack'
Assert-True (-not $pack.Contains(' ')) 'space is not a pack glyph (built-in owns it)'
$space = $pack.ResolveWithFallback(' ')
Assert-True ($space.AdvanceMm -eq [double]6.0 -and $space.Strokes.Count -eq 0) 'space falls back to the built-in advance, no strokes'
$thrown = $false
try { $pack.ResolveWithFallback($gu) | Out-Null } catch [InvalidOperationException] { $thrown = $true }
Assert-True $thrown 'missing character fails loudly'
$thrown = $false
try { $pack.ResolveWithFallback($gu) | Out-Null } catch { $thrown = $_.Exception.Message -like '*generate-stage2-fontpack.py*' }
Assert-True $thrown 'missing-character message points at the pack extension script'

# ---- 3. Geometry against the frozen calibration -----------------------------
$mu_ink = Measure-Ink $pack.Resolve($mu)
$muHeight = [Math]::Round($mu_ink.MaxY - $mu_ink.MinY, 3)
Assert-True (($mu_ink.MaxY - $mu_ink.MinY) -gt 9.4 -and ($mu_ink.MaxY - $mu_ink.MinY) -lt 10.2) "full-height glyph U+6728 is ~10mm tall ($muHeight)"
$wang_ink = Measure-Ink $pack.Resolve($wang)
$wangHeight = [Math]::Round($wang_ink.MaxY - $wang_ink.MinY, 3)
Assert-True (($wang_ink.MaxY - $wang_ink.MinY) -gt 6.6 -and ($wang_ink.MaxY - $wang_ink.MinY) -lt 7.2) "sparse glyph U+738B keeps its true proportion ($wangHeight)"
$yi_ink = Measure-Ink $pack.Resolve($yi)
Assert-True (($yi_ink.MaxY - $yi_ink.MinY) -lt 1.5) "thin glyph U+4E00 stays a thin bar ($([Math]::Round($yi_ink.MaxY - $yi_ink.MinY, 3)))"
Assert-True ($wang_ink.MinY -ge 1.9 -and $wang_ink.MinY -le 2.3) "baseline: U+738B ink floor at 1mm + 94 source units ($([Math]::Round($wang_ink.MinY, 3)))"
foreach ($symbol in @($yong, $he, $guo, $gong, $wang, $yi, $mu)) {
    $ink = Measure-Ink $pack.Resolve($symbol)
    Assert-True (($ink.MinX -ge 0.0) -and ($ink.MaxX -le 12.0) -and ($ink.MinY -ge 0.0) -and ($ink.MaxY -le 12.0)) "glyph U+$('0x{0:X4}' -f [int]$symbol) ink stays inside the 12mm em box"
}

# ---- 4. Mixed-source layout through the production path ---------------------
$resolve = $pack.ToResolver()
$line1 = [string]$yong + $he + $guo + $gong
$line2 = [string]$wang + $yi + $er + $san
$text = $line1 + "`n" + $line2
$expectedStrokes = 0
foreach ($symbol in $text.ToCharArray()) {
    if ($symbol -eq "`r" -or $symbol -eq "`n") { continue }   # layout handles newlines itself
    $expectedStrokes += $pack.ResolveWithFallback($symbol).Strokes.Count
}
$strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::Layout($text, $resolve, 0.5, 0.0,
    [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::LineHeightMm, 0.6)
Assert-True ($strokes.Count -eq $expectedStrokes) "layout preserves every stroke ($($strokes.Count))"

# ---- 5. End-to-end program generation ---------------------------------------
$p = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$p.ProfileId = 'stage2-fontpack-test'
$p.DisplayName = 'stage2 fontpack test'
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
Assert-True ($violations.Count -eq 0) 'continuous queue invariant holds for the pack program'
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

$outDir = Join-Path $repoRoot 'artifacts\stage0'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$log.Add('PASS: font pack offline suite.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\stage2-fontpack-test.txt'), $log)
Write-Host ($log -join [Environment]::NewLine)
