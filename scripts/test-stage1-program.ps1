$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MachineProfileService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MotionCalibrationService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\CalibrationProgramService.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage1-program-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.Program.Tests.dll'
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

function Assert-Contains([string] $text, [string] $needle, [string] $name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "$name did not contain '$needle'." }
}
function Assert-Throws([scriptblock] $action, [string] $name) {
    $thrown = $false
    try { & $action } catch { $thrown = $true }
    if (-not $thrown) { throw "$name was expected to reject its input." }
}

$profile = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$profile.ProfileId = 'verified-test-machine'
$profile.DisplayName = 'Verified test machine'
$profile.VerificationStatus = 'Verified'
$profile.VerifiedAtUtc = [DateTime]::UtcNow
$profile.VerificationEvidence = New-Object 'System.Collections.Generic.List[string]'
$profile.VerificationEvidence.Add('Stage 1 bench verification record')
$profile.PenControlMode = 'Z'
$profile.PenUpValue = 5.0
$profile.PenDownValue = 0.0
$profile.AxisMapping = New-Object GrblPlotter.AutoWriter.Models.CoordinateMapping
$profile.AxisMapping.WorkOrigin = 'G54'
$profile.UsableBoundsWorkMm = New-Object GrblPlotter.AutoWriter.Models.CoordinateBounds
$profile.UsableBoundsWorkMm.MinX = 0.0
$profile.UsableBoundsWorkMm.MaxX = 30.0
$profile.UsableBoundsWorkMm.MinY = 0.0
$profile.UsableBoundsWorkMm.MaxY = 30.0
$profile.UsableBoundsWorkMm.MinZ = -1.0
$profile.UsableBoundsWorkMm.MaxZ = 6.0
$profile.Speeds = New-Object GrblPlotter.AutoWriter.Models.MachineSpeeds
$profile.Speeds.DrawingMmPerMin = 1000.0
$profile.Speeds.TravelMmPerMin = 2000.0
$profile.Speeds.ZMmPerMin = 300.0
$profile.Delays = New-Object GrblPlotter.AutoWriter.Models.MachineDelays
$profile.Delays.PenUpMs = 100
$profile.Delays.PenDownMs = 200

$line = [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($profile, 1.0, 1.0, 10.0)
Assert-Contains $line 'G21' 'line units'
Assert-Contains $line 'G90' 'line coordinates'
Assert-Contains $line 'G94' 'line feed mode'
Assert-Contains $line 'G54' 'verified work frame selector'
Assert-Contains $line 'Review inherited G92/TLO' 'offset review comment'
Assert-Contains $line 'G1 X1 Y1 F2000' 'XY travel feed'
Assert-Contains $line 'G1 X11 Y1 F1000' 'drawing feed'
Assert-Contains $line 'G1 Z5 F300' 'Z feed'
Assert-Contains $line 'G4 P0.1' 'pen-up delay'
Assert-Contains $line 'G4 P0.2' 'pen-down delay'
if ($line.IndexOf('G0', [StringComparison]::Ordinal) -ge 0 -or $line.IndexOf('M', [StringComparison]::Ordinal) -ge 0) { throw 'Generated program contains forbidden G0 or M commands.' }

$cross = [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateCross($profile, 15.0, 15.0, 5.0)
$square = [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateSquare10Mm($profile, 1.0, 1.0)
Assert-Contains $cross 'X10 Y15' 'cross horizontal start'
Assert-Contains $cross 'X15 Y20' 'cross vertical end'
Assert-Contains $square 'X11 Y11' '10mm square corner'

$unverified = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$unverified.ProfileId = 'unverified'
$unverified.DisplayName = 'Unverified'
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($unverified, 1, 1, 10) } 'unverified profile'
$outside = $profile
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($outside, 25, 1, 10) } 'out-of-bounds line'
$savedWorkOrigin = $profile.AxisMapping.WorkOrigin
$profile.AxisMapping.WorkOrigin = $null
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($profile, 1, 1, 10) } 'missing work frame'
$profile.AxisMapping.WorkOrigin = $savedWorkOrigin
$savedEvidence = $profile.VerificationEvidence
$profile.VerificationEvidence = $null
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($profile, 1, 1, 10) } 'missing verification evidence'
$profile.VerificationEvidence = $savedEvidence
$badSpeed = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$profile.Capabilities = New-Object GrblPlotter.AutoWriter.Models.MachineCapabilities
$profile.Capabilities.RequiresContinuousPenMotion = $true
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($profile, 1, 1, 10) } 'continuous pen with positive dwell'
$profile.Delays.PenUpMs = 0
$profile.Delays.PenDownMs = 0
$continuousLine = [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($profile, 1, 1, 10)
if ($continuousLine -match 'G4') { throw 'Zero dwell must not emit synchronizing G4 blocks.' }
Assert-Contains $continuousLine 'do not wait for Idle between blocks' 'continuous streaming requirement'
$badSpeed.ProfileId = 'bad-speed'
$badSpeed.DisplayName = 'Bad speed'
$badSpeed.VerificationStatus = 'Verified'
$badSpeed.PenControlMode = 'Z'
$badSpeed.PenUpValue = 5
$badSpeed.PenDownValue = 0
$badSpeed.UsableBoundsWorkMm = $profile.UsableBoundsWorkMm
$badSpeed.Speeds = $profile.Speeds
$badSpeed.Speeds.DrawingMmPerMin = [double]::NaN
$badSpeed.Delays = $profile.Delays
Assert-Throws { [GrblPlotter.AutoWriter.Services.CalibrationProgramService]::GenerateShortLine($badSpeed, 1, 1, 10) } 'NaN drawing speed'

Write-Host ('PASS: stage1 calibration program service compiled and validated. Temporary artifacts: ' + $tempRoot)
