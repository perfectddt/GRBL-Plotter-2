$ErrorActionPreference = 'Stop'

# Offline test for the resume-restore G-code sequence (AutoWriter/ResumeRestore.cs).
#
# Why this exists (R6 rerun, 2026-10-06): after a software pause the physical pen has always
# sprung back (~0.5 s spring-back), but grbl still believes Z=5. The old restore emitted
# "G0 Z5.000" - a zero-length move that grbl drops - so the servo never re-fired and the
# first stroke after resume drew air (paper: top horizontal of the 4th character missing
# exactly at the resume point). The first R6 run escaped only by luck: its resume landed on
# a Z0 line, whose real transition re-armed the pen.
#
# What must be asserted:
#   - Pen down at pause => a REAL Z0->Z5 transition pair is emitted, even when XY never
#     moved (the common resume case - it was silently broken before).
#   - The wiggle lines carry an explicit G90 because the operator may have left G91 active.
#   - Z moves use "G1 .. F10000" (the only pattern proven to drive this machine's servo).
#   - Pen up + nothing moved => empty sequence (a zero-length "G0 Z0" is dropped anyway).
#   - Numbers are formatted invariantly - a decimal comma would corrupt the command.
#
# Assertions stay ASCII-only, so this script is safe to run from Windows PowerShell 5.1
# regardless of the console code page.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\ResumeRestore.cs'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-resume-restore-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.ResumeRestore.Tests.dll'

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

& $csc /nologo /target:library /out:$assemblyPath $sourceFile
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('GrblPlotter.AutoWriter.ResumeRestore', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic

$buildLines = $type.GetMethod('BuildLines', $flags)
$isPenDown = $type.GetMethod('IsPenDown', $flags)
if (-not $buildLines) { throw 'ResumeRestore.BuildLines not found.' }
if (-not $isPenDown)  { throw 'ResumeRestore.IsPenDown not found.' }

$log = New-Object System.Collections.Generic.List[string]
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
    $log.Add("ok - $name")
}
function Assert-Equal($expected, $actual, [string] $name) {
    if ($expected -ne $actual) { throw "$name : expected '$expected' but got '$actual'." }
    $log.Add("ok - $name")
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
    $log.Add("ok - $name")
}
function Invoke-Build([bool] $xyMoved, [bool] $zMoved, [double] $px, [double] $py, [double] $pz) {
    # The leading comma stops PowerShell from unrolling single-element (or empty) arrays,
    # which would otherwise turn the result into a bare string / $null and break .Count.
    return ,[string[]]$buildLines.Invoke($null, @($xyMoved, $zMoved, $px, $py, $pz))
}

# ---- IsPenDown: machine convention Z0=up / Z5=down, any positive Z counts as down. ----
Assert-False ([bool]$isPenDown.Invoke($null, @(0.0)))  'IsPenDown(0)'
Assert-True  ([bool]$isPenDown.Invoke($null, @(5.0)))  'IsPenDown(5)'
Assert-True  ([bool]$isPenDown.Invoke($null, @(2.5)))  'IsPenDown(2.5)'

# ---- Case A: the R6 rerun itself - XY moved (+5/+3), pen down at (22,6,Z5). ----
$a = Invoke-Build $true $true 22.0 6.0 5.0
Assert-Equal 4 $a.Count 'Case A line count'
Assert-Equal 'G90 G0 X22.000 Y6.000' $a[0] 'Case A XY restore'
Assert-Equal 'G4 P0.5'               $a[1] 'Case A dwell'
Assert-Equal 'G90 G1 Z0 F10000'      $a[2] 'Case A wiggle down-to-up'
Assert-Equal 'G1 Z5.000 F10000'      $a[3] 'Case A wiggle re-press'
Assert-False (($a -join '|').Contains('G0 Z5')) 'Case A must not emit a bare zero-length G0 Z5'

# ---- Case B: the common resume - nothing moved, but the pen was down. Previously broken. ----
$b = Invoke-Build $false $false 22.0 6.0 5.0
Assert-Equal 2 $b.Count 'Case B line count'
Assert-Equal 'G90 G1 Z0 F10000' $b[0] 'Case B wiggle down-to-up'
Assert-Equal 'G1 Z5.000 F10000' $b[1] 'Case B wiggle re-press'

# ---- Case C: nothing moved, pen up - old behaviour, no output at all. ----
$c = Invoke-Build $false $false 10.0 2.0 0.0
Assert-Equal 0 $c.Count 'Case C emits nothing'

# ---- Case D: XY moved, pen up - no pointless zero-length Z line. ----
$d = Invoke-Build $true $false 10.0 2.0 0.0
Assert-Equal 2 $d.Count 'Case D line count'
Assert-Equal 'G90 G0 X10.000 Y2.000' $d[0] 'Case D XY restore'
Assert-Equal 'G4 P0.5'               $d[1] 'Case D dwell'

# ---- Case E: only Z jogged, pen-up target - old G0 restore preserved. ----
$e = Invoke-Build $false $true 10.0 2.0 0.0
Assert-Equal 1 $e.Count 'Case E line count'
Assert-Equal 'G0 Z0.000' $e[0] 'Case E Z restore'

# ---- Case F: only Z jogged, pen-down target - wiggle restores and re-presses. ----
$f = Invoke-Build $false $true 10.0 2.0 5.0
Assert-Equal 2 $f.Count 'Case F line count'
Assert-Equal 'G90 G1 Z0 F10000' $f[0] 'Case F wiggle down-to-up'
Assert-Equal 'G1 Z5.000 F10000' $f[1] 'Case F wiggle re-press'

# ---- Partial Z still counts as pen-down. ----
$g = Invoke-Build $false $false 0.0 0.0 2.5
Assert-Equal 'G1 Z2.500 F10000' $g[1] 'Partial Z re-press value'

# ---- Culture proof: a comma-decimal locale must not corrupt the numbers. ----
$oldCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    $h = Invoke-Build $true $false 22.0 6.0 0.0
    Assert-Equal 'G90 G0 X22.000 Y6.000' $h[0] 'Invariant format under de-DE'
} finally {
    [Threading.Thread]::CurrentThread.CurrentCulture = $oldCulture
}

$log.Add('')
$log.Add('PASS: resume restore re-presses the pen with a real Z0->Z5 transition whenever the pause')
$log.Add('point had the pen down (even with no XY motion), keeps the old behaviour for every other')
$log.Add('case, and formats numbers invariantly.')
[IO.File]::WriteAllLines((Join-Path $repoRoot 'artifacts\stage0\resume-restore-test.txt'), $log)
