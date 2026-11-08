param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $repo 'GRBL-Plotter\bin\Debug'
if ($Configuration -eq 'Release') { $bin = Join-Path $repo 'artifacts\autowriter\Release' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
$output = Join-Path $bin 'AutoWriter.PageFeatures.Tests.exe'
$refs = @('GRBL-Plotter.exe','UglyToad.PdfPig.dll','UglyToad.PdfPig.Core.dll','UglyToad.PdfPig.Fonts.dll','UglyToad.PdfPig.DocumentLayoutAnalysis.dll') | ForEach-Object { '/r:' + (Join-Path $bin $_) }
& $compiler /nologo /codepage:65001 /target:exe "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll $refs (Join-Path $PSScriptRoot 'PageFeaturesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Feature test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $bin 'GRBL-Plotter.exe.config') -Destination ($output + '.config') -Force
Push-Location $bin
try {
    & $output (Join-Path $repo 'artifacts\page-features')
    if ($LASTEXITCODE -ne 0) { throw 'Feature checks failed.' }
} finally {
    Pop-Location
    Remove-Item -LiteralPath $output,($output + '.config') -Force -ErrorAction SilentlyContinue
}
