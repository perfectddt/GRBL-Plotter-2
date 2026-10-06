$ErrorActionPreference = 'Stop'

# Offline test for the 「解锁」 button decision logic (AutoWriter/AlarmUnlock.cs).
#
# AlarmUnlock deliberately has no WinForms dependency, matching ResetGuard, so the whole decision
# can be asserted here. The control layer itself (ControlSerialForm.BtnGRBLUnlock_Click and the
# hook in ProcessGrblStateChange) is NOT covered - that needs an on-screen pass.
#
# The critical assertion is ShouldReadSettings: it must refuse to fire while the controller is not
# IDLE, because ProcessSend() only releases a '$' line from the queue in IDLE or CHECK. Firing any
# earlier would re-wedge the very send queue that '$X' just cleared - which is the exact failure
# candidate I-a fixed on 2026-10-06.
#
# Assertions stay ASCII-only, so this script is safe to run from Windows PowerShell 5.1 regardless
# of the console code page. AlarmUnlock.cs carries a UTF-8 BOM so the legacy csc.exe below reads
# its Chinese text correctly.

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceFile = Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\AlarmUnlock.cs'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-alarm-unlock-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$assemblyPath = Join-Path $tempRoot 'AutoWriter.AlarmUnlock.Tests.dll'

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
$type = $assembly.GetType('GrblPlotter.AutoWriter.AlarmUnlock', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic

$buildMethod = $type.GetMethod('BuildMessage', $flags)
$shouldMethod = $type.GetMethod('ShouldReadSettings', $flags)
if (-not $buildMethod) { throw 'AlarmUnlock.BuildMessage(bool,bool) not found.' }
if (-not $shouldMethod) { throw 'AlarmUnlock.ShouldReadSettings(bool,bool,bool) not found.' }
$unlockField = $type.GetField('UnlockCommand', $flags)
$titleField = $type.GetField('Title', $flags)
$logField = $type.GetField('LogSettingsRecovered', $flags)
if (-not $unlockField) { throw 'AlarmUnlock.UnlockCommand not found.' }
if (-not $titleField) { throw 'AlarmUnlock.Title not found.' }
if (-not $logField) { throw 'AlarmUnlock.LogSettingsRecovered not found.' }

$unlockCommand = [string]$unlockField.GetValue($null)
$title = [string]$titleField.GetValue($null)
$logRecovered = [string]$logField.GetValue($null)

$inAlarm = [string]$buildMethod.Invoke($null, @($true, $false))
$notAlarm = [string]$buildMethod.Invoke($null, @($false, $false))
$inAlarmStreaming = [string]$buildMethod.Invoke($null, @($true, $true))

function Assert-Contains([string] $text, [string] $needle, [string] $name) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "$name did not contain '$needle'." }
}
function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name was expected to be true." }
}
function Assert-False([bool] $condition, [string] $name) {
    if ($condition) { throw "$name was expected to be false." }
}

# The command sent must be exactly GRBL's unlock command - RequestSend routes it around the queue.
Assert-True ($unlockCommand -eq '$X') 'UnlockCommand'
Assert-True ($title.Length -gt 0) 'Title'

# Every prompt variant must be non-empty and must name the command it sends.
foreach ($pair in @(@($inAlarm,'inAlarm'), @($notAlarm,'notAlarm'), @($inAlarmStreaming,'inAlarmStreaming'))) {
    Assert-True ($pair[0].Length -gt 0) ("BuildMessage " + $pair[1])
    Assert-Contains $pair[0] '$X' ("BuildMessage " + $pair[1])
}

# The alarm variant must explain the alarm recovery, the idle variant must not claim there is one.
Assert-Contains $inAlarm 'ALARM:3' 'BuildMessage(inAlarm)'
Assert-Contains $inAlarm '$H'    'BuildMessage(inAlarm)'
Assert-Contains $inAlarm 'Idle'   'BuildMessage(inAlarm)'

# The safety facts must survive in every variant: unlocking is not homing, and this machine cannot
# home, so a manual re-datum is still required after a position-losing alarm.
Assert-Contains $notAlarm         '$H'    'BuildMessage(notAlarm)'
Assert-Contains $inAlarmStreaming '$H'    'BuildMessage(inAlarmStreaming)'

# Only the streaming variants add the "program will not resume" line.
Assert-True ($inAlarmStreaming.Length -gt $inAlarm.Length) 'BuildMessage(inAlarm,streaming) adds a streaming warning'
Assert-Contains $inAlarmStreaming 'ALARM:3' 'BuildMessage(inAlarm,streaming)'

# ShouldReadSettings: the whole point is that it fires ONLY in IDLE with a live connection.
Assert-True  ([bool]$shouldMethod.Invoke($null, @($true,  $true,  $true)))  'pending + idle + connected'
Assert-False ([bool]$shouldMethod.Invoke($null, @($true,  $true,  $false))) 'pending + idle + disconnected'
Assert-False ([bool]$shouldMethod.Invoke($null, @($true,  $false, $true)))  'pending + not idle'
Assert-False ([bool]$shouldMethod.Invoke($null, @($false, $true,  $true)))  'not pending'
Assert-False ([bool]$shouldMethod.Invoke($null, @($false, $false, $false))) 'nothing at all'

# The recovery log line must repeat the re-datum warning, because this is the first idle moment
# after an alarm.
Assert-Contains $logRecovered 'ALARM:3' 'LogSettingsRecovered'
Assert-Contains $logRecovered '$X'      'LogSettingsRecovered'

Write-Host ('PASS: AlarmUnlock gates the deferred settings read on IDLE only and keeps the re-datum ' +
    'warning in every prompt (inAlarm ' + $inAlarm.Length + ' chars, notAlarm ' + $notAlarm.Length +
    ' chars, streaming ' + $inAlarmStreaming.Length + ' chars). Temporary artifacts: ' + $tempRoot)