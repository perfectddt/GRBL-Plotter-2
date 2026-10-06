[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoPath 'GRBL-Plotter'
$outputPath = Join-Path $projectPath "bin\$Configuration"
$reportPath = Join-Path $repoPath 'artifacts\stage0'
New-Item -ItemType Directory -Force $reportPath | Out-Null
[xml]$project = Get-Content -LiteralPath (Join-Path $projectPath 'GRBL-Plotter.csproj') -Raw
$checked = 0
$missing = @()
foreach ($item in $project.Project.ItemGroup.ChildNodes) {
    if ($item.CopyToOutputDirectory -in @('Always','PreserveNewest')) {
        $checked++
        $relativePath = [Uri]::UnescapeDataString($item.Include)
        if (!(Test-Path -LiteralPath (Join-Path $outputPath $relativePath))) { $missing += $relativePath }
    }
}
$dependencies = @(Get-ChildItem -LiteralPath $outputPath -Filter '*.dll' | ForEach-Object {
    [PSCustomObject]@{
        Name = $_.Name
        AssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($_.FullName).Version.ToString()
        FileVersion = $_.VersionInfo.FileVersion
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $outputPath 'GRBL-Plotter.exe'))
$resources = [Resources.ResourceManager]::new('GrblPlotter.MainForm', $assembly)
$fileMenu = $resources.GetString('fileToolStripMenuItem.Text', [Globalization.CultureInfo]::GetCultureInfo('zh-CN'))
$result = [PSCustomObject]@{
    Configuration = $Configuration
    CopiedResourceCount = $checked
    MissingResources = $missing
    ChineseFileMenu = $fileMenu
    Dependencies = $dependencies
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportPath "verify-$Configuration.json") -Encoding UTF8
if ($missing.Count -gt 0) { throw "Missing output resources: $($missing -join ', ')" }
if ($fileMenu -ne '文件') { throw 'Chinese resource loading failed.' }
Write-Output "$Configuration verified: $checked copied resources; Chinese File menu: $fileMenu."
