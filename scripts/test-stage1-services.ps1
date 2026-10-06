$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MachineProfileService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MotionCalibrationService.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage1-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.Services.Tests.dll'
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

function Assert-Nearly([double] $actual, [double] $expected, [string] $name) {
    if ([Math]::Abs($actual - $expected) -gt 0.000001) {
        throw "$name expected $expected but was $actual"
    }
}
function Assert-Throws([scriptblock] $action, [string] $name) {
    $thrown = $false
    try { & $action } catch { $thrown = $true }
    if (-not $thrown) { throw "$name was expected to reject its input." }
}

$belt = [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateBelt(1.8, 1, 2, 16)
Assert-Nearly $belt.StepsPerMm 6.25 'belt steps/mm'
Assert-Nearly $belt.MmPerStep 0.16 'belt mm/step'
$screw = [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateScrew(1.8, 1, 8)
Assert-Nearly $screw.StepsPerMm 25 'screw steps/mm'
$effective = [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateEffective(7200, 8, 2, 1)
Assert-Nearly $effective.PulsesPerRevolution 3600 'effective external pulses'
Assert-Nearly $effective.StepsPerMm 450 'effective steps/mm'
Assert-Nearly ([GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CorrectMeasuredStepsPerMm(100, 10, 9.5)) ([double](100 * 10 / 9.5)) 'measured correction'
Assert-Throws { [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateBelt(0, 1, 2, 16) } 'zero angle'
Assert-Throws { [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateEffective(7200, 8, 0, 1) } 'zero ratio numerator'
Assert-Throws { [GrblPlotter.AutoWriter.Services.MotionCalibrationService]::CalculateScrew([double]::NaN, 1, 8) } 'NaN angle'

$profilePath = Join-Path $tempRoot 'machine-profile.json'
$profile = New-Object GrblPlotter.AutoWriter.Models.MachineProfile
$profile.ProfileId = 'test-machine'
$profile.DisplayName = 'Stage 1 test machine'
$profile.ProtocolCandidate = 'Grbl'
$profile.PenControlMode = 'Unknown'
$profileService = New-Object GrblPlotter.AutoWriter.Services.MachineProfileService
$profileService.Save($profilePath, $profile)
$loaded = $profileService.Load($profilePath)
if ($loaded.ProfileId -ne 'test-machine' -or $null -ne $loaded.PenUpValue) { throw 'Profile roundtrip did not preserve required or unknown values.' }
$profile.DisplayName = 'Stage 1 second version'
$profileService.Save($profilePath, $profile)
if (-not (Get-ChildItem -Path ($profilePath + '.bak-*') -ErrorAction SilentlyContinue)) { throw 'Profile backup was not retained.' }
$flagIndex = 0
foreach ($continuousFlag in @($null, $true, $false)) {
    $profile.Capabilities = New-Object GrblPlotter.AutoWriter.Models.MachineCapabilities
    $profile.Capabilities.RequiresContinuousPenMotion = $continuousFlag
    $flagPath = Join-Path $tempRoot ('continuous-flag-' + $flagIndex + '.json')
    $profileService.Save($flagPath, $profile)
    $flagLoaded = $profileService.Load($flagPath)
    if ($flagLoaded.Capabilities.RequiresContinuousPenMotion -ne $continuousFlag) { throw 'Continuous pen flag roundtrip lost unknown/true/false distinction.' }
    $flagIndex++
}
[IO.File]::WriteAllText($profilePath, '{"profileId":"missing-schema"}', [Text.Encoding]::UTF8)
Assert-Throws { $profileService.Load($profilePath) } 'missing schema'
[IO.File]::WriteAllText($profilePath, '{not-json', [Text.Encoding]::UTF8)
Assert-Throws { $profileService.Load($profilePath) } 'corrupted JSON'

Write-Host ('PASS: stage1 pure services compiled and validated. Temporary artifacts: ' + $tempRoot)
