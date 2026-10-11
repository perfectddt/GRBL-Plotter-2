$ErrorActionPreference = 'Stop'
# Full production loader, page layout, and continuous writing validation. No machine connection.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\BuiltinGlyphLibrary.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\TextLayoutService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\GlyphFontPackService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProjectService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\RegionLayoutService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingEditorText.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}
$packPath = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Fonts\hanziwriter-full-metrics.fontpack.json'
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
$packPath = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Fonts\hanziwriter-full-metrics.fontpack.json'
$pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile($packPath)
if ($pack.GlyphCount -ne 9619) { throw 'Wrong full pack glyph count' }
$json = Get-Content $packPath -Encoding UTF8 -Raw | ConvertFrom-Json
foreach ($g in $json.glyphs) {
    if (-not $pack.Contains([char]$g.c)) { throw 'Production loader lost a character' }
    foreach ($s in $g.strokes) { foreach ($v in $s) { if ($v -lt 0 -or $v -gt 12) { throw 'Out of em coordinate' } } }
}
$resolve = [Func[char, GrblPlotter.AutoWriter.Services.BuiltinGlyph]] { param([char]$c) $pack.ResolveWithFallback($c) }
foreach ($c in '0123456789'.ToCharArray()) {
    $glyph = $pack.Resolve($c)
    if ($glyph.AdvanceMm -ne 8) { throw 'Digit advance must be 8 mm' }
    foreach ($stroke in $glyph.Strokes) { foreach ($point in $stroke.Points) {
        if ($point.X -lt 0 -or $point.X -gt $glyph.AdvanceMm) { throw 'Digit ink exceeds its cell' }
    } }
}
if ($pack.Resolve([char]',').AdvanceMm -ge 12) { throw 'ASCII comma should be narrow' }
$pilot = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile((Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Fonts\hanziwriter-pilot.fontpack.json'))
if ($pilot.Resolve([char]0x6C38).AdvanceMm -ne 12) { throw 'Old font fallback changed' }
# Invalid widths and widths smaller than actual ink must fail in the production loader.
$tiny = '{"schemaVersion":1,"emSizeMm":12,"advanceCjkMm":12,"advanceSpaceMm":6,"glyphs":[{"c":"!","advanceMm":WIDTH,"strokes":[[1,1,2,2]]}]}'
foreach ($badWidth in @('0','-1','49','1','1e309','NaN')) {
    $badPath = Join-Path $tempRoot ('bad-' + $badWidth + '.json')
    [IO.File]::WriteAllText($badPath,$tiny.Replace('WIDTH',$badWidth))
    $rejected = $false
    try { [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile($badPath) | Out-Null } catch { $rejected=$true }
    if (-not $rejected) { throw "Invalid glyph advance accepted: $badWidth" }
}
$mixedPage = New-Object GrblPlotter.AutoWriter.Services.PageLayoutOptions
$mixedPage.WidthMm=28; $mixedPage.HeightMm=100; $mixedPage.AutoWrap=$true
$mixed = [GrblPlotter.AutoWriter.Services.TextLayoutService]::LayoutPage(([string][char]0x6C38 + '12,34!'),$resolve,0,0,14,1,$mixedPage)
# CJK 12 + two digits 8 + 8 fit exactly; comma begins the next line.
$firstDigit = $pack.Resolve([char]'1').Strokes.Count
$chineseStrokes = $pack.Resolve([char]0x6C38).Strokes.Count
$secondDigitStrokes = $pack.Resolve([char]'2').Strokes.Count
$commaIndex = $chineseStrokes + $firstDigit + $secondDigitStrokes
$firstRowY = @($mixed[0..($commaIndex-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
$secondRowY = @($mixed[$commaIndex..($mixed.Count-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
if ([Math]::Abs(($firstRowY | Measure-Object -Minimum).Minimum - ($secondRowY | Measure-Object -Maximum).Maximum) -gt 1e-9) { throw 'Mixed wrap must have zero actual ink gap' }
$mixedProject = New-Object GrblPlotter.AutoWriter.Services.WritingProject
$mixedProject.Version=1; $mixedProject.Text=([string][char]0x6C38 + '12,34!'); $mixedProject.FontPath=$packPath
$mixedProject.FontSha256=[GrblPlotter.AutoWriter.Services.WritingProjectService]::Fingerprint($packPath)
$mixedProject.PageEnabled=$true; $mixedProject.Page=$mixedPage; $mixedProject.FontSizeMm=10
$mixedProjectPath=Join-Path $tempRoot 'mixed.awproject'
[GrblPlotter.AutoWriter.Services.WritingProjectService]::Save($mixedProjectPath,$mixedProject)
$restored=[GrblPlotter.AutoWriter.Services.WritingProjectService]::Load($mixedProjectPath)
$again=[GrblPlotter.AutoWriter.Services.TextLayoutService]::LayoutPage($restored.Text,$resolve,$restored.LetterSpacingMm,$restored.LineSpacingMm,14,$restored.FontSizeMm/10,$restored.Page)
if ($again.Count -ne $mixed.Count) { throw 'Mixed project lost strokes' }
for ($i=0; $i -lt $mixed.Count; $i++) { for ($j=0; $j -lt $mixed[$i].Points.Count; $j++) {
    if ($again[$i].Points[$j].X -ne $mixed[$i].Points[$j].X -or $again[$i].Points[$j].Y -ne $mixed[$i].Points[$j].Y) { throw 'Mixed project geometry changed' }
} }
$text = -join ([char[]]@(0x7EB8,0x5F20,0x5C3A,0x5BF8,0x3001,0x56DB,0x8FB9,0x8FB9,0x8DDD,0x3002,0x4FDD,0x5B58,0x9879,0x76EE,0xFF0C,0x91CD,0x65B0,0x751F,0x6210,0x3002))
$text += " 2026"
$page = New-Object GrblPlotter.AutoWriter.Services.PageLayoutOptions
$page.WidthMm=210; $page.HeightMm=297; $page.LeftMm=10; $page.RightMm=10; $page.TopMm=10; $page.BottomMm=10; $page.AutoWrap=$true
$strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::LayoutPage($text,$resolve,2,4,14,0.6,$page)
if ($strokes.Count -lt 50) { throw 'Missing paragraph strokes' }
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
 $p.UsableBoundsWorkMm.MaxX = 210.0
 $p.UsableBoundsWorkMm.MinY = -297.0
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

$violations = [GrblPlotter.AutoWriter.Services.WritingProgramService]::FindContinuousViolations($program,5)
if ($violations.Count -ne 0) { throw 'Continuous pen motion violation' }
$artifact = Join-Path $repoRoot 'artifacts\stage2\full-writing-font-sample.gcode'
[IO.File]::WriteAllText($artifact,$program)
Write-Host "PASS: production loader 9619 glyphs; all coordinates in em; paragraph LayoutPage; continuous Gcode ($($strokes.Count) strokes)."
