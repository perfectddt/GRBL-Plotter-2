$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\GUI\ViewTransform.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-viewtransform-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.ViewTransform.Tests.dll'
$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'Could not find the .NET Framework C# compiler.' }

& $csc /nologo /target:library /out:$assemblyPath /r:System.Drawing.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Add-Type -Path $assemblyPath

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
}
function Assert-Near([double] $actual, [double] $expected, [string] $name) {
    if ([Math]::Abs($actual - $expected) -gt 0.0001) { throw "$name was expected to be $expected but was $actual." }
}

# Offset of a world point after the view transform, relative to the drawing centre.
# Directions are the ones the operator sees on screen: +X right, +Y up. The view's own scaling
# (ScaleTransform(s, -s)) is what makes the world Y point up on screen, so no extra flip belongs
# here - this helper isolates the transform this file is about.
function Get-ViewOffset($view, [double] $wx, [double] $wy, [double] $cx, [double] $cy) {
    $matrix = $view.BuildMatrix([float]$cx, [float]$cy)
    try {
        $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new([float]$wx, [float]$wy))
        $matrix.TransformPoints($points)
        return @(($points[0].X - $cx), ($points[0].Y - $cy))
    } finally { $matrix.Dispose() }
}

# 1) The default state must be a true no-op, so an untouched view keeps behaving exactly as before.
$view = New-Object GrblPlotter.ViewTransform
Assert-True $view.IsIdentity 'a fresh view transform is the identity'
Assert-False $view.SwapsAxes 'a fresh view transform does not swap the axes'
$offset = Get-ViewOffset $view 15 20 5 5
Assert-Near $offset[0] 10 'identity keeps X'
Assert-Near $offset[1] 15 'identity keeps Y'

# 2) One clockwise turn, as seen on screen: right goes down and up goes right.
$view.RotateClockwise()
Assert-False $view.IsIdentity 'one clockwise turn is no longer the identity'
Assert-True $view.SwapsAxes 'one clockwise turn swaps the visible axes'
Assert-True ($view.QuarterTurns -eq 1) 'one clockwise turn counts as a single quarter turn'
$offset = Get-ViewOffset $view 15 5 5 5
Assert-Near $offset[0] 0 'clockwise: the right hand point leaves the right'
Assert-Near $offset[1] -10 'clockwise: the right hand point goes down'
$offset = Get-ViewOffset $view 5 15 5 5
Assert-Near $offset[0] 10 'clockwise: the upper point goes right'
Assert-Near $offset[1] 0 'clockwise: the upper point leaves the top'

# 3) The opposite turn is the exact reverse: right goes up and up goes left.
$view.Reset()
$view.RotateCounterClockwise()
Assert-True ($view.QuarterTurns -eq 3) 'one counter-clockwise turn equals three quarter turns'
$offset = Get-ViewOffset $view 15 5 5 5
Assert-Near $offset[0] 0 'counter-clockwise: the right hand point leaves the right'
Assert-Near $offset[1] 10 'counter-clockwise: the right hand point goes up'
$offset = Get-ViewOffset $view 5 15 5 5
Assert-Near $offset[0] -10 'counter-clockwise: the upper point goes left'
Assert-Near $offset[1] 0 'counter-clockwise: the upper point leaves the top'

$view.Reset()
$view.RotateClockwise()
$view.RotateCounterClockwise()
Assert-True $view.IsIdentity 'a clockwise and a counter-clockwise turn cancel out'

# 4) Four quarter turns come back to the start.
$view.Reset()
for ($i = 0; $i -lt 4; $i++) { $view.RotateClockwise() }
Assert-True $view.IsIdentity 'four clockwise turns return to the identity'
Assert-False $view.SwapsAxes 'four clockwise turns do not swap the axes'

# 5) Mirroring swaps one axis and leaves the other alone.
$view = New-Object GrblPlotter.ViewTransform
$view.ToggleMirrorX()
Assert-True $view.MirrorX 'mirror X is on'
Assert-False $view.SwapsAxes 'mirroring does not swap the visible axes'
$offset = Get-ViewOffset $view 15 15 5 5
Assert-Near $offset[0] -10 'mirror X moves a right hand point to the left'
Assert-Near $offset[1] 10 'mirror X leaves the vertical position alone'

$view.ToggleMirrorX()
Assert-True $view.IsIdentity 'mirror X twice is the identity again'

$view.ToggleMirrorY()
$offset = Get-ViewOffset $view 15 15 5 5
Assert-Near $offset[0] 10 'mirror Y leaves the horizontal position alone'
Assert-Near $offset[1] -10 'mirror Y moves an upper point to the bottom'

# 6) Both mirrors together are a half turn, which is also what two quarter turns produce.
$view.ToggleMirrorX()
Assert-True ($view.MirrorX -and $view.MirrorY) 'both mirrors are on'
$both = Get-ViewOffset $view 15 15 5 5
$view.Reset()
$view.RotateClockwise()
$view.RotateClockwise()
$turned = Get-ViewOffset $view 15 15 5 5
Assert-Near $turned[0] $both[0] 'half turn matches both mirrors in X'
Assert-Near $turned[1] $both[1] 'half turn matches both mirrors in Y'
Assert-Near $both[0] -10 'both mirrors send a right hand point to the left'
Assert-Near $both[1] -10 'both mirrors send an upper point to the bottom'

# 7) A mirrored and turned view is still a rigid motion: the distance from the centre survives.
$view.Reset()
$view.ToggleMirrorX()
$view.RotateClockwise()
$offset = Get-ViewOffset $view 15 5 5 5
$radius = [Math]::Sqrt($offset[0] * $offset[0] + $offset[1] * $offset[1])
Assert-Near $radius 10 'a mirrored and turned point keeps its distance from the centre'

# 8) The centre of the transform is a fixed point, whatever the state.
$view.Reset()
$view.RotateClockwise()
$view.ToggleMirrorY()
$offset = Get-ViewOffset $view 7 9 7 9
Assert-Near $offset[0] 0 'the transform centre does not move in X'
Assert-Near $offset[1] 0 'the transform centre does not move in Y'

# 9) Reset clears everything and the description follows the state.
$view.Reset()
Assert-True $view.IsIdentity 'reset returns to the identity'
Assert-True ($view.Describe() -eq 'normal') 'the identity is described as normal'

$described = New-Object GrblPlotter.ViewTransform
$described.RotateClockwise()
$described.ToggleMirrorX()
Assert-True ($described.Describe().IndexOf('rotate 90 deg CW') -ge 0) 'the description names the quarter turn'
Assert-True ($described.Describe().IndexOf('mirror X') -ge 0) 'the description names the mirror'
Assert-True ($described.Describe().IndexOf('mirror Y') -lt 0) 'the description leaves out the mirror that is off'

# Factory-compatible startup/reset mirrors the paper Y direction without changing coordinates.
$factory = New-Object GrblPlotter.ViewTransform -ArgumentList $true
Assert-True $factory.MirrorY 'factory view starts with vertical mirror'
Assert-True $factory.IsDefault 'factory view starts at its default'
Assert-False $factory.IsIdentity 'factory view differs from machine-coordinate view'
$offset = Get-ViewOffset $factory 15 20 5 5
Assert-Near $offset[0] 10 'factory view preserves X'
Assert-Near $offset[1] -15 'factory view reverses Y'
$factory.RotateClockwise()
$offset = Get-ViewOffset $factory 15 5 5 5
Assert-Near $offset[0] 0 'factory clockwise leaves right'
Assert-Near $offset[1] -10 'factory clockwise moves right down even with mirror'
$factory.Reset()
$matrix = $factory.BuildMatrix(5, 5)
try {
    $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(15, 20))
    $matrix.TransformPoints($points)
    $matrix.Invert()
    $matrix.TransformPoints($points)
    Assert-Near $points[0].X 15 'inverse restores machine X'
    Assert-Near $points[0].Y 20 'inverse restores machine Y'
} finally { $matrix.Dispose() }
$factory.ToggleMirrorY()
Assert-True $factory.IsIdentity 'operator can show unmirrored machine coordinates'
Assert-False $factory.IsDefault 'unmirrored view is not factory default'
$factory.RotateClockwise()
$factory.ToggleMirrorX()
$factory.Reset()
Assert-True $factory.IsDefault 'reset restores factory default'
Assert-True $factory.MirrorY 'reset restores vertical mirror'
Assert-False $factory.MirrorX 'reset clears horizontal mirror'
Assert-True ($factory.QuarterTurns -eq 0) 'reset clears rotation'

Write-Host ('PASS: stage1 view transform validated. Temporary artifacts: ' + $tempRoot)

# Every visual keypad anchor must round-trip through the actual display matrix.
$anchorChecks = 0
foreach ($mx in @(0,1)) { foreach ($my in @(0,1)) { foreach ($turn in 0..3) {
    $view = [GrblPlotter.ViewTransform]::new()
    if ($mx) { $view.ToggleMirrorX() }
    if ($my) { $view.ToggleMirrorY() }
    for ($i=0; $i -lt $turn; $i++) { $view.RotateClockwise() }
    foreach ($anchor in 1..9) {
        $world = $view.MapViewAnchorToWorld($anchor)
        $point = Get-ViewOffset $view (($world-1)%3-1) (1-[Math]::Floor(($world-1)/3)) 0 0
        Assert-Near $point[0] (($anchor-1)%3-1) 'Visual anchor X'
        Assert-Near $point[1] (1-[Math]::Floor(($anchor-1)/3)) 'Visual anchor Y'
        $anchorChecks++
    }
}}}
$flipped = [GrblPlotter.ViewTransform]::new($true)
Assert-Near ($flipped.MapViewAnchorToWorld(1)) 7 'Mirrored visual top-left uses minimum Y'
Assert-Near ($flipped.MapViewAnchorToWorld(7)) 1 'Mirrored visual bottom-left uses maximum Y'
Write-Host "PASS: $anchorChecks visual origin mappings across all mirror/rotation states."