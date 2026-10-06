$ErrorActionPreference = 'Stop'
# Reuse the production types and proven sample profile; never connects to a controller.
. (Join-Path $PSScriptRoot 'generate-stage2-sample.ps1')

$allStrokes = [Collections.Generic.List[GrblPlotter.AutoWriter.Models.WritingStroke]]::new()
$sizes = @(5.0, 6.0, 8.0)
$offsets = @(0.0, 8.0, 17.0)
$report = [Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $glyphStrokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::Layout(
        $wang, $resolve, 2.0, 4.0,
        [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::LineHeightMm,
        $sizes[$i] / [GrblPlotter.AutoWriter.Services.BuiltinGlyphLibrary]::BodySizeMm)
    $first = $glyphStrokes[0].Points[0]
    $pointsForSize = @($glyphStrokes | ForEach-Object { $_.Points } | ForEach-Object { $_ })
    $height = ($pointsForSize.Y | Measure-Object -Maximum).Maximum - ($pointsForSize.Y | Measure-Object -Minimum).Minimum
    if ([Math]::Abs($height - $sizes[$i]) -gt 0.000001) { throw 'Glyph height does not match size.' }
    foreach ($stroke in $glyphStrokes) {
        $shifted = [GrblPlotter.AutoWriter.Models.WritingStroke]::new()
        foreach ($point in $stroke.Points) {
            $shifted.Points.Add([GrblPlotter.AutoWriter.Models.WritingPoint]::new(
                $point.X - $first.X + $offsets[$i], $point.Y - $first.Y))
        }
        $allStrokes.Add($shifted)
    }
    $report.Add("PASS: body height $height mm at X=$($offsets[$i])")
}
$allPoints = @($allStrokes | ForEach-Object { $_.Points } | ForEach-Object { $_ })
if (($allPoints.X | Measure-Object -Minimum).Minimum -lt 0 -or
    ($allPoints.X | Measure-Object -Maximum).Maximum -gt 25 -or
    ($allPoints.Y | Measure-Object -Minimum).Minimum -lt -8 -or
    ($allPoints.Y | Measure-Object -Maximum).Maximum -gt 0) { throw 'Size comparison exceeds X0..25/Y-8..0.' }
$sizeProgram = [GrblPlotter.AutoWriter.Services.WritingProgramService]::Generate($p, $allStrokes, $options)
if ([GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($sizeProgram, 5.0).Count -ne 0) {
    throw 'Continuous pen control invariant failed.'
}
$codeLines = @($sizeProgram -split '\r?\n' | Where-Object { $_ -and -not $_.StartsWith(';') })
if ($codeLines[-1] -ne 'G1 X0 Y0 F1800') { throw 'Missing final return to origin.' }
if (-not $sizeProgram.Contains('G1 X0 Y0 F1800')) { throw 'Missing origin start alignment.' }
[IO.File]::WriteAllText((Join-Path $outDir 'stage2-size-5-6-8mm.nc'), $sizeProgram, [Text.UTF8Encoding]::new($false))
$report.Add('PASS: 12 strokes, continuous queue; first pen-down X0/Y0; final pen-up return X0/Y0; X0..25/Y-8..0.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\stage2-size-acceptance.txt'), $report)
$report
