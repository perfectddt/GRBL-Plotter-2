param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio 2022 with .NET desktop development.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
$out = Join-Path $repo ("artifacts\autowriter\" + $Configuration + "\")
New-Item -ItemType Directory -Force -Path $out | Out-Null
& $msbuild (Join-Path $repo 'GRBL-Plotter\GRBL-Plotter.csproj') /t:Build /p:Configuration=$Configuration /p:Platform=AnyCPU /p:OutDir=$out /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'AutoWriter publish build failed.' }
Write-Host ("AutoWriter entry: " + (Join-Path $out 'GRBL-Plotter.exe'))
