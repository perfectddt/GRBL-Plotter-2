$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ink-gap-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$files = @('Models/WritingStroke.cs','Services/BuiltinGlyphLibrary.cs','Services/TextLayoutService.cs','Services/GlyphFontPackService.cs') | ForEach-Object { Join-Path $root ('GRBL-Plotter/AutoWriter/' + $_) }
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:library /out:$temp/test.dll /r:System.Runtime.Serialization.dll $files
if ($LASTEXITCODE) { throw 'Compilation failed' }
Add-Type -Path "$temp/test.dll"
$pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile((Join-Path $root 'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full-compact.fontpack.json'))
$resolve = [Func[char, GrblPlotter.AutoWriter.Services.BuiltinGlyph]] { param([char]$c) $pack.ResolveWithFallback($c) }
$first = '你好，世界！'; $second = '今天开始测试写字，123。'
$n = 0; foreach ($c in $first.ToCharArray()) { $n += $pack.Resolve($c).Strokes.Count }
$page = New-Object GrblPlotter.AutoWriter.Services.PageLayoutOptions
$page.WidthMm=210; $page.HeightMm=297; $page.LeftMm=10; $page.RightMm=10; $page.TopMm=10; $page.BottomMm=10; $page.AutoWrap=$true
foreach ($size in @(6,10)) { foreach ($gap in @(0,2)) {
    $strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::LayoutPage(($first+"`n"+$second),$resolve,2,$gap,14,($size/10),$page)
    $a = @($strokes[0..($n-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
    $b = @($strokes[$n..($strokes.Count-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
    $actual = ($a | Measure-Object -Minimum).Minimum - ($b | Measure-Object -Maximum).Maximum
    if ([Math]::Abs($actual-$gap) -gt 1e-9) { throw "Actual ink gap $actual instead of $gap" }
    if ([Math]::Abs(($a | Measure-Object -Maximum).Maximum + 10) -gt 1e-9) { throw 'Ink top margin incorrect' }
    Write-Host "PASS size=$size requestedGap=$gap actualInkGap=$actual"
} }
