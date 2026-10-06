param(
    [string] $ProfilePath = '',
    [string] $OutputPath = '',
    [double] $ParkX = 0,
    [double] $ParkY = 0,
    [switch] $NoPark,
    [switch] $NoOriginAlign
)

$ErrorActionPreference = 'Stop'

# Regenerates the built-in AutoWriter sample ("工", the same three strokes the setup dialog draws)
# through the real WritingProgramService, so the end-park behaviour can be run on the machine
# without rebuilding the application.
#
# The point of the end park: without it the program stops at the end of the last stroke and the
# operator has to jog the tool back before the next sheet. With it the pen is raised and the tool
# returns to the given work point (0,0 = the work origin the operator set with G10 L20).
#
# Start point (on by default): the whole set is translated so the first pen-down point is the work
# origin. The built-in geometry carries a 1 mm margin, so without this the pen would land at
# (1,11) and the operator could not predict the landing point. -NoOriginAlign restores the raw
# design coordinates, which is only useful for reproducing older artifacts.

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProfilePath) { $ProfilePath = Join-Path $repoRoot 'artifacts\stage1\machine-profile-continuous-pen-confirmed.json' }
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'artifacts\stage1\writing-gong-park-origin.nc' }

$sourceFiles = @(
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\MachineProfileService.cs'),
    (Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs')
)
foreach ($sourceFile in $sourceFiles) {
    if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Missing source file: $sourceFile" }
}
if (-not (Test-Path -LiteralPath $ProfilePath)) { throw "Missing machine profile: $ProfilePath" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-writing-sample-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'WritingSample.cs'
$assemblyPath = Join-Path $tempRoot 'WritingSample.exe'

$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;

internal static class WritingSample
{
    private static WritingStroke S(params double[] xy)
    {
        var points = new List<WritingPoint>();
        for (int i = 0; i < xy.Length; i += 2) points.Add(new WritingPoint(xy[i], xy[i + 1]));
        return new WritingStroke(points);
    }

    private static int Main(string[] args)
    {
        string profilePath = args[0];
        string outputPath = args[1];
        bool park = args[2] == "1";
        double parkX = double.Parse(args[3], CultureInfo.InvariantCulture);
        double parkY = double.Parse(args[4], CultureInfo.InvariantCulture);
        bool alignStart = args[5] == "1";

        MachineProfile profile = new MachineProfileService().Load(profilePath);

        // The same built-in sample geometry the setup dialog uses (top bar, vertical, bottom bar).
        var strokes = new List<WritingStroke>();
        strokes.Add(S(1, 11, 11, 11));
        strokes.Add(S(6, 1, 6, 11));
        strokes.Add(S(1, 1, 11, 1));

        var options = new WritingProgramOptions
        {
            RequireVerifiedRun = false,
            DrawingMmPerMin = 1000.0,
            TravelMmPerMin = 1800.0,
            ZMmPerMin = 10000.0
        };
        if (park) options.ParkPosition = new WritingPoint(parkX, parkY);
        if (alignStart) options.AlignStartToOrigin = true;

        string program = WritingProgramService.Generate(profile, strokes, options);
        File.WriteAllText(outputPath, program, new System.Text.UTF8Encoding(false));

        Console.WriteLine("endPark=" + (park ? ("X" + parkX + " Y" + parkY) : "none"));
        Console.WriteLine("startAtOrigin=" + (alignStart ? "yes" : "no (raw design coordinates)"));
        Console.WriteLine("bytes=" + program.Length);
        foreach (string line in program.Replace("\r\n", "\n").Split('\n'))
            if (line.StartsWith("G1 ")) Console.WriteLine(line);
        return 0;
    }
}
'@
[IO.File]::WriteAllText($sourcePath, $source, (New-Object Text.UTF8Encoding($false)))

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

& $csc /nologo /target:exe /out:$assemblyPath /r:System.Runtime.Serialization.dll $sourceFiles $sourcePath
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }

$parkFlag = if ($NoPark) { '0' } else { '1' }
$alignFlag = if ($NoOriginAlign) { '0' } else { '1' }
& $assemblyPath $ProfilePath $OutputPath $parkFlag ($ParkX.ToString([CultureInfo]::InvariantCulture)) ($ParkY.ToString([CultureInfo]::InvariantCulture)) $alignFlag
if ($LASTEXITCODE -ne 0) { throw "Writing sample generation failed with exit code $LASTEXITCODE." }

Write-Host ("PASS: writing sample written to " + $OutputPath + ". Temporary artifacts: " + $tempRoot)
