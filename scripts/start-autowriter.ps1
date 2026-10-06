[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('Offline','Inspect','Normal')][string]$Mode = 'Offline'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$application = Join-Path $repoRoot "GRBL-Plotter\bin\$Configuration\GRBL-Plotter.exe"
if (!(Test-Path -LiteralPath $application)) { throw 'Build the project first with scripts/build.ps1.' }
# Offline  = no controller connection, safe baseline (default).
# Inspect  = inspection whitelist, no offline lock (only approved lines may be sent).
# Normal   = no flags: the real app. It can connect and send; use only with the machine ready.
$arguments = if ($Mode -eq 'Normal') { @() } elseif ($Mode -eq 'Inspect') { @('--inspect-machine') } else { @('--offline','--inspect-machine') }
# Start-Process rejects an empty -ArgumentList, so only pass it when there is something to pass.
if ($arguments.Count -gt 0) { Start-Process -FilePath $application -ArgumentList $arguments -WindowStyle Normal }
else { Start-Process -FilePath $application -WindowStyle Normal }
