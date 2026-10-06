$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\MachineControl\PenZClassifier.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-penzclassifier-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.PenZClassifier.Tests.dll'
$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'Could not find the .NET Framework C# compiler.' }

& $csc /nologo /target:library /out:$assemblyPath $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Add-Type -Path $assemblyPath

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
}

# The shipped machine profile: the pen is lifted at Z0 and pressed at Z5.
$up = 0.0
$down = 5.0

function Test-Down([double] $oldZ, [double] $newZ) {
    return [GrblPlotter.PenZClassifier]::IsPenDownMove($oldZ, $newZ, $true, $up, $down)
}
function Test-Up([double] $oldZ, [double] $newZ) {
    return [GrblPlotter.PenZClassifier]::IsPenUpMove($oldZ, $newZ, $true, $up, $down)
}

# 1) The two transitions the profile actually produces.
Assert-True (Test-Down 0 5) 'going from the pen-up value to the pen-down value is a pen-down move'
Assert-False (Test-Up 0 5) 'that same move is not a pen-up move'
Assert-True (Test-Up 5 0) 'going from the pen-down value back to the pen-up value is a pen-up move'
Assert-False (Test-Down 5 0) 'that same move is not a pen-down move'

# 2) Only the exact transition counts. This is what keeps a plain 2D job - one that never touches
#    Z, or holds it at a single value - behaving exactly as before.
Assert-False (Test-Down 0 0) 'holding at the pen-up value is not a pen-down move'
Assert-False (Test-Up 0 0) 'holding at the pen-up value is not a pen-up move'
Assert-False (Test-Down 5 5) 'holding at the pen-down value is not a pen-down move'
Assert-False (Test-Up 5 5) 'holding at the pen-down value is not a pen-up move'
Assert-False (Test-Down 0 3) 'a move to neither pen value changes nothing'
Assert-False (Test-Up 0 3) 'a move to neither pen value changes nothing'
Assert-False (Test-Down 3 5) 'a move that only ends at the pen-down value is not a full transition'
Assert-False (Test-Up 3 0) 'a move that only ends at the pen-up value is not a full transition'
Assert-False (Test-Down -5 5) 'a move from a negative Z is left to the built-in rule'
Assert-False (Test-Up 5 -5) 'a move to a negative Z is left to the built-in rule'

# 3) Values arriving as text (or via JSON) must not depend on exact equality.
Assert-True (Test-Down 0 5.0000001) 'a rounding error inside the tolerance still counts as the pen-down value'
Assert-False (Test-Down 0 5.001) 'a value outside the tolerance does not count as the pen-down value'

# 4) A switched-off rule, or a profile whose pen values are equal, never reports a transition.
Assert-False ([GrblPlotter.PenZClassifier]::IsPenDownMove(0, 5, $false, $up, $down)) 'a disabled rule reports no pen-down move'
Assert-False ([GrblPlotter.PenZClassifier]::IsPenUpMove(5, 0, $false, $up, $down)) 'a disabled rule reports no pen-up move'
Assert-False ([GrblPlotter.PenZClassifier]::IsPenDownMove(0, 0, $true, 0, 0)) 'equal pen values never report a pen-down move'
Assert-False ([GrblPlotter.PenZClassifier]::IsPenUpMove(0, 0, $true, 0, 0)) 'equal pen values never report a pen-up move'

# 5) Replay the program the generator actually produces and check the stroke / travel split.
#    This mirrors the view's own state machine: it starts on the pen-up path and the first G1 after
#    the G0-modal start is treated as a pen-down move.
$ncPath = Join-Path $repoRoot 'artifacts\stage1\writing-gong-park-origin.nc'
if (-not (Test-Path -LiteralPath $ncPath)) { throw "Missing reference program: $ncPath" }

function Get-PenPathByMove([bool] $ruleEnabled) {
    $result = New-Object System.Collections.ArrayList
    $modalZ = 0.0
    $previousZ = 0.0
    $previousMotion = 0
    $path = 'penUp'
    foreach ($line in [IO.File]::ReadAllLines($ncPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        if ($trimmed.StartsWith(';')) { continue }
        if (-not ($trimmed -match '^G1\s')) { continue }
        $zMatch = [Regex]::Match($trimmed, 'Z(-?\d+(?:\.\d+)?)')
        if ($zMatch.Success) { $modalZ = [double]::Parse($zMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) }
        $newZ = $modalZ
        # The view's own state machine. Every line here is a G1, so the "G0 -> G1" branch fires only
        # once (on the first G1, whose old motion mode is the G0 default) and the "G1 -> G0" branch
        # never fires.
        $zDown = [GrblPlotter.PenZClassifier]::IsPenDownMove($previousZ, $newZ, $ruleEnabled, $up, $down)
        if (($previousMotion -eq 0) -or $zDown) { $path = 'penDown' }
        $zUp = [GrblPlotter.PenZClassifier]::IsPenUpMove($previousZ, $newZ, $ruleEnabled, $up, $down)
        if ($zUp) { $path = 'penUp' }
        $hasXy = ($trimmed -match 'X') -or ($trimmed -match 'Y')
        if ($hasXy) { [void]$result.Add(@{ Line = $trimmed; Pen = $path }) }
        $previousZ = $newZ
        $previousMotion = 1
    }
    return ,$result
}

$withRule = Get-PenPathByMove $true
Assert-True ($withRule.Count -eq 7) 'the reference program has seven moves that change X or Y'

$downMoves = @($withRule | Where-Object { $_.Pen -eq 'penDown' })
$upMoves = @($withRule | Where-Object { $_.Pen -eq 'penUp' })
Assert-True ($downMoves.Count -eq 4) 'the strokes plus the opening move are classified as pen-down'
Assert-True ($upMoves.Count -eq 3) 'the three travel moves are classified as pen-up'

# The opening "G1 X0 Y0" is zero length (the view starts at the origin), so classifying it as
# pen-down draws nothing. It is the one artefact of the built-in "first G1 is a pen-down move"
# rule and it disappears as soon as the first stroke does not start at the origin.
Assert-True ($downMoves[0].Line -eq 'G1 X0 Y0 F1800') 'the opening move comes first in the file'
Assert-True ($downMoves[1].Line -eq 'G1 X10 Y0 F1000') 'the first stroke is the top bar'
Assert-True ($downMoves[2].Line -eq 'G1 X5 Y0 F1000') 'the second stroke is the vertical'
Assert-True ($downMoves[3].Line -eq 'G1 X10 Y-10 F1000') 'the third stroke is the bottom bar'
Assert-True ($upMoves[0].Line -eq 'G1 X5 Y-10 F1800') 'the first travel is a pen-up move'
Assert-True ($upMoves[1].Line -eq 'G1 X0 Y-10 F1800') 'the second travel is a pen-up move'
Assert-True ($upMoves[2].Line -eq 'G1 X0 Y0 F1800') 'the closing park move is a pen-up move'

# 6) The same program with the rule off reproduces the old behaviour: every move is one pen-down
#    path, which is exactly why the "Pen-Up path" toggle had nothing to show.
$withoutRule = Get-PenPathByMove $false
Assert-True (@($withoutRule | Where-Object { $_.Pen -eq 'penUp' }).Count -eq 0) 'without the rule no move is classified as pen-up'
Assert-True (@($withoutRule | Where-Object { $_.Pen -eq 'penDown' }).Count -eq 7) 'without the rule every move is classified as pen-down'

Write-Host ('PASS: stage1 pen Z classifier validated. Temporary artifacts: ' + $tempRoot)
