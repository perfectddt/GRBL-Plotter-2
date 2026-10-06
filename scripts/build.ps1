[CmdletBinding()]
param([ValidateSet('Debug','Release','All')][string]$Configuration = 'All', [switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswherePath)) { throw 'Install Visual Studio 2022 with .NET desktop development.' }
$vsPath = & $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$buildTool = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
if (!(Test-Path -LiteralPath $buildTool)) { throw 'MSBuild was not found.' }
$referencePath = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
if (!(Test-Path -LiteralPath $referencePath)) { throw 'Install the Microsoft .NET Framework 4.8 Developer Pack (Targeting Pack and SDK).' }
$logPath = Join-Path $repoPath 'artifacts\stage0'
New-Item -ItemType Directory -Force -Path $logPath | Out-Null
Push-Location $repoPath
try {
    if (!$SkipRestore) {
        & $buildTool 'GRBL-Plotter.sln' /t:Restore /p:RestorePackagesConfig=true /v:minimal /fl "/flp:logfile=$logPath\restore.log;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed. See artifacts/stage0/restore.log.' }
    }
    $buildConfigurations = if ($Configuration -eq 'All') { @('Debug','Release') } else { @($Configuration) }
    foreach ($buildConfiguration in $buildConfigurations) {
        & $buildTool 'GRBL-Plotter.sln' /t:Build "/p:Configuration=$buildConfiguration" '/p:Platform=Any CPU' /v:minimal /fl "/flp:logfile=$logPath\$buildConfiguration.log;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { throw "$buildConfiguration build failed. See artifacts/stage0/$buildConfiguration.log." }
    }
} finally { Pop-Location }
