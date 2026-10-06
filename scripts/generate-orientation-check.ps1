param(
    [string] $ProfilePath = '',
    [string] $OutputPath = '',
    [switch] $OriginAlign,
    [switch] $Park,
    [double] $ParkX = 0,
    [double] $ParkY = 0
)

$ErrorActionPreference = 'Stop'

# Regenerates the asymmetric orientation check program (R7b) from the SHIPPED machine profile
# through the real WritingProgramService, so the emitted coordinates demonstrate the
# axisMapping.xDirection / yDirection transform.
#
# Why this exists: the hand-written artifacts/stage1/R7-orientation-asym-f-shang.nc bypasses the
# generator, so re-sending it can never prove the direction fix. Only a generator-produced
# program can. See artifacts/stage1/R7-orientation-result-20261005.md.
#
# Start-point alignment and the end park stay OFF here by default, deliberately: the archived
# R7b / R7c evidence must remain reproducible from this script, and a direction check is easier
# to read with one transform variable fewer. Pass -OriginAlign to translate the set so the first
# pen-down point is the work origin, and -Park to raise the pen and return to a work point at the
# end (default 0,0 = the work origin). Used together they give a direction check that both starts
# and finishes on the operator's zero: see artifacts/stage1/R7d-orientation-posY-origin.nc
# (generated with -OriginAlign -Park; R7c stays untouched as the transform-free diagnostic).

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProfilePath) { $ProfilePath = Join-Path $repoRoot 'artifacts\stage1\machine-profile-continuous-pen-confirmed.json' }
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'artifacts\stage1\R7b-orientation-fixed.nc' }

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

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-orientation-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'OrientationCheck.cs'
$assemblyPath = Join-Path $tempRoot 'OrientationCheck.exe'

$source = @'
using System;
using System.Collections.Generic;
using System.IO;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;

internal static class OrientationCheck
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
        bool alignStart = args.Length > 2 && args[2] == "1";
        bool park = args.Length > 3 && args[3] == "1";
        double parkX = args.Length > 4 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
        double parkY = args.Length > 5 ? double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0.0;
        MachineProfile profile = new MachineProfileService().Load(profilePath);

        // Design coordinates: +X right, +Y "up" on the paper as the author sees it.
        // Left: letter F (vertical, top bar, middle bar). Right: Chinese shang (long low bar,
        // right vertical, short middle bar). Both are asymmetric, so a mirrored Y is visible.
        var strokes = new List<WritingStroke>();
        strokes.Add(S(1, 1, 1, 11));
        strokes.Add(S(1, 11, 8, 11));
        strokes.Add(S(1, 6, 5, 6));
        strokes.Add(S(12, 1, 22, 1));
        strokes.Add(S(19, 1, 19, 11));
        strokes.Add(S(13, 7, 19, 7));

        var options = new WritingProgramOptions
        {
            RequireVerifiedRun = false,
            DrawingMmPerMin = 1000.0,
            TravelMmPerMin = 1800.0,
            ZMmPerMin = 10000.0
        };
        if (alignStart) options.AlignStartToOrigin = true;
        if (park) options.ParkPosition = new WritingPoint(parkX, parkY);

        string program = WritingProgramService.Generate(profile, strokes, options);
        File.WriteAllText(outputPath, program, new System.Text.UTF8Encoding(false));

        int? xdir = profile.AxisMapping == null ? null : profile.AxisMapping.XDirection;
        int? ydir = profile.AxisMapping == null ? null : profile.AxisMapping.YDirection;
        Console.WriteLine("xDirection=" + (xdir.HasValue ? xdir.Value.ToString() : "null"));
        Console.WriteLine("yDirection=" + (ydir.HasValue ? ydir.Value.ToString() : "null"));
        Console.WriteLine("startAtOrigin=" + (alignStart ? "yes" : "no (raw design coordinates)"));
        Console.WriteLine("endPark=" + (park ? ("X" + parkX.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Y" + parkY.ToString(System.Globalization.CultureInfo.InvariantCulture)) : "none"));
        Console.WriteLine("bytes=" + program.Length);
        foreach (string line in program.Replace("\r\n", "\n").Split('\n'))
            if (line.StartsWith("G1 ") && line.IndexOf('Y') >= 0) Console.WriteLine(line);
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

$alignFlag = if ($OriginAlign) { '1' } else { '0' }
$parkFlag = if ($Park) { '1' } else { '0' }
& $assemblyPath $ProfilePath $OutputPath $alignFlag $parkFlag ($ParkX.ToString([CultureInfo]::InvariantCulture)) ($ParkY.ToString([CultureInfo]::InvariantCulture))
if ($LASTEXITCODE -ne 0) { throw "Orientation check generation failed with exit code $LASTEXITCODE." }

Write-Host ("PASS: orientation check program written to " + $OutputPath + ". Temporary artifacts: " + $tempRoot)
