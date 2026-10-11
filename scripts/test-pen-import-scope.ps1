param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ApplicationPath))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
function T($name){ $asm.GetType($name,$true) }
function Obj($name){ [Activator]::CreateInstance((T $name)) }
function SetProp($o,$p,$v){ $o.GetType().GetProperty($p,$flags).SetValue($o,$v,$null) }
function Assert($v,$m){ if(!$v){throw $m}; $script:checks++ }
$checks=0
$profile=Obj 'GrblPlotter.AutoWriter.Models.MachineProfile'
SetProp $profile PenControlMode 'Z'; SetProp $profile PenUpValue ([double]0); SetProp $profile PenDownValue ([double]5)
$speeds=Obj 'GrblPlotter.AutoWriter.Models.MachineSpeeds';SetProp $speeds ZMmPerMin ([double]10000);SetProp $speeds DrawingMmPerMin ([double]1000);SetProp $profile Speeds $speeds
$sync=T 'GrblPlotter.AutoWriter.Services.PenControlSync'
$begin=$sync.GetMethod('Begin',$flags);$current=$sync.GetProperty('Current',$flags)
$tools=T 'GrblPlotter.UserControls.MyControl'
$original=$tools.GetMethod('GetToolsProperties',$flags).Invoke($null,@())
$toolType=T 'GrblPlotter.ToolList';$list=$toolType.GetField('toolListArray',$flags).GetValue($null)
$settings=(T 'GrblPlotter.Properties.Settings').GetProperty('Default',$flags).GetValue($null,$null)
$before=$settings.DeviceRouterZUp
$entry=Obj 'GrblPlotter.ToolProperty'; SetProp $entry ToolName 'keep metadata'
$list.Add($entry)
$router=$entry.Router; $laser=$entry.Laser; $plotter=$entry.Plotter
$routerBefore=$router.SaveZ; $laserBefore=$laser.SaveZ; $plotterBefore=$plotter.SaveZ
$scope=$begin.Invoke($null,@($profile,$null))
try {
 Assert ($null -ne $current.GetValue($null,$null)) 'scope missing'
 Assert ([object]::ReferenceEquals($original,$tools.GetMethod('GetToolsProperties',$flags).Invoke($null,@()))) 'device object changed'
 Assert ($list.Count -eq 1 -and $entry.ToolName -eq 'keep metadata') 'scope changed tools or metadata'
 Assert ($router.SaveZ -eq $routerBefore -and $laser.SaveZ -eq $laserBefore -and $plotter.SaveZ -eq $plotterBefore) 'scope changed device/tool pen fields'
 Assert ($settings.DeviceRouterZUp -eq $before) 'settings changed'
 $image=[Runtime.Serialization.FormatterServices]::GetUninitializedObject((T 'GrblPlotter.GCodeFromImage'))
 $image.GetType().GetMethod('LoadExtern',$flags).Invoke($image,@('nonexistent-scope-test.png',$false)) | Out-Null
 $captured=$image.GetType().GetField('penImportContext',$flags).GetValue($image)
 Assert ([object]::ReferenceEquals($captured,$current.GetValue($null,$null))) 'deferred image did not capture scope'
} finally {$scope.Dispose()}
Assert ($null -eq $current.GetValue($null,$null)) 'scope leaked after cancel'
$failed=$begin.Invoke($null,@($profile,$null))
try { try { throw 'simulated importer failure' } finally { $failed.Dispose() } } catch { }
Assert ($null -eq $current.GetValue($null,$null)) 'scope leaked after importer exception'
$deferred=$sync.GetMethod('Enter',$flags).Invoke($null,@($captured))
try {
 $sync.GetMethod('ApplyToGeneratedCode',$flags).Invoke($null,@()) | Out-Null
 $g=T 'GrblPlotter.Gcode';$z=$g.GetNestedType('OptionZAxis',$flags)
 Assert ($z.GetProperty('Up',$flags).GetValue($null,$null) -eq 0) 'deferred up wrong'
 Assert ($z.GetProperty('Down',$flags).GetValue($null,$null) -eq 5) 'deferred down wrong'
 Assert ($z.GetProperty('Feed',$flags).GetValue($null,$null) -eq 10000) 'deferred zfeed wrong'
 Assert ($g.GetProperty('GcodeXYFeed',$flags).GetValue($null,$null) -eq 1000) 'deferred drawfeed wrong'
}finally{$deferred.Dispose()}
Assert ($null -eq $current.GetValue($null,$null)) 'deferred scope leaked'
SetProp $profile PenControlMode 'unknown'
$rejected=$false;try{$begin.Invoke($null,@($profile,$null))|Out-Null}catch{$rejected=$true}
Assert $rejected 'unknown mode accepted'
SetProp $profile PenControlMode 'Z';SetProp $speeds ZMmPerMin ([double]-1)
$rejected=$false;try{$begin.Invoke($null,@($profile,$null))|Out-Null}catch{$rejected=$true}
Assert $rejected 'invalid speed accepted'
Assert ($null -eq $current.GetValue($null,$null)) 'invalid scope partially applied'
Write-Output "PASS: $checks pen import scope checks"
