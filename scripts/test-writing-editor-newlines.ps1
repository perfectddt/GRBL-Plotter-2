$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$temp = Join-Path ([IO.Path]::GetTempPath()) ('writing-newlines-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$files = @('Models/WritingStroke.cs','Services/BuiltinGlyphLibrary.cs','Services/TextLayoutService.cs','Services/GlyphFontPackService.cs','Services/WritingEditorText.cs') | ForEach-Object { Join-Path $root ('GRBL-Plotter/AutoWriter/' + $_) }
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:library /out:$temp/test.dll /r:System.Runtime.Serialization.dll $files
if ($LASTEXITCODE) { throw 'Compilation failed' }
Add-Type -Path "$temp/test.dll"
Add-Type -AssemblyName System.Windows.Forms
Add-Type 'using System; using System.Runtime.InteropServices; public static class WritingEditProbe { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam); }'
$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true; $box.WordWrap = $false
$handle = $box.Handle
try {
    foreach ($case in @(
        @{Text="A`r`nB"; Rows=2}, @{Text="A`nB"; Rows=2},
        @{Text="A`r`r`nB"; Rows=2}, @{Text="A`r`n`nB"; Rows=2},
        @{Text="A`r`n`r`nB"; Rows=3}, @{Text="A`n`nB"; Rows=3},
        @{Text="A`r`n`nB`r`n`r`nC"; Rows=4}
    )) {
        $normalized = [GrblPlotter.AutoWriter.Services.WritingEditorText]::Normalize($case.Text)
        $box.Text = $normalized
        $native = [WritingEditProbe]::SendMessage($handle, 186, [IntPtr]::Zero, [IntPtr]::Zero).ToInt32()
        if ($native -ne $case.Rows -or $box.Lines.Count -ne $case.Rows) { throw 'Native and layout line counts disagree' }
        if ([GrblPlotter.AutoWriter.Services.WritingEditorText]::Normalize($normalized) -cne $normalized) { throw 'Normalization not idempotent' }
        Write-Host "PASS native/managed rows=$native input=$($case.Text.Replace("`r",'<CR>').Replace("`n",'<LF>'))"
    }
    $first = '你好，世界！'; $second = '今天开始测试写字，123。'
    $raw = $first+"`r`n`n"+$second
    $box.Text = $raw
    if ($box.Lines.Count -ne 3 -or [WritingEditProbe]::SendMessage($handle,186,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32() -ne 2) { throw 'Failed to reproduce hidden LF discrepancy' }
    $pack = [GrblPlotter.AutoWriter.Services.GlyphFontPack]::LoadFromFile((Join-Path $root 'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full-compact.fontpack.json'))
    $resolve = [Func[char, GrblPlotter.AutoWriter.Services.BuiltinGlyph]] { param([char]$c) $pack.ResolveWithFallback($c) }
    $n = 0; foreach ($c in $first.ToCharArray()) { $n += $pack.Resolve($c).Strokes.Count }
    $page = New-Object GrblPlotter.AutoWriter.Services.PageLayoutOptions
    $page.WidthMm=210; $page.HeightMm=297; $page.LeftMm=10; $page.RightMm=10; $page.TopMm=10; $page.BottomMm=10; $page.AutoWrap=$true
    foreach($gap in @(0,2)) { foreach($repair in @($false,$true)) {
        $text = if($repair) { [GrblPlotter.AutoWriter.Services.WritingEditorText]::Normalize($raw) } else { $raw }
        $strokes = [GrblPlotter.AutoWriter.Services.TextLayoutService]::LayoutPage($text,$resolve,1,$gap,12,0.6,$page)
        $a = @($strokes[0..($n-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
        $b = @($strokes[$n..($strokes.Count-1)] | ForEach-Object { $_.Points } | ForEach-Object { $_.Y })
        $actual = ($a | Measure-Object -Minimum).Minimum - ($b | Measure-Object -Maximum).Maximum
        if($repair -and [Math]::Abs($actual-$gap) -gt 1e-9) { throw 'Actual ink gap incorrect after repair' }
        Write-Host "PASS corrected=$repair fontsize=6 requestedGap=$gap actualInkGap=$actual"
    } }
} finally { $box.Dispose() }
