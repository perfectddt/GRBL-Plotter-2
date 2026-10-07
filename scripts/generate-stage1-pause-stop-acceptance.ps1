param([string] $OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\stage1\pause-stop-acceptance' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-pause-fixtures-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'Fixtures.cs'
$exe = Join-Path $tempRoot 'Fixtures.exe'
$source = @'
using System;
using System.Collections.Generic;
using System.IO;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
class Fixtures {
    static WritingStroke S(double x, double y, double length) {
        return new WritingStroke(new List<WritingPoint> {
            new WritingPoint(x,y), new WritingPoint(x+length,y) });
    }
    static void Save(MachineProfile profile, List<WritingStroke> strokes, double feed, string path) {
        var options = new WritingProgramOptions {
            RequireVerifiedRun=false, AlignStartToOrigin=true,
            ParkPosition=new WritingPoint(0,0), DrawingMmPerMin=feed,
            TravelMmPerMin=1800, ZMmPerMin=10000 };
        string program=WritingProgramService.Generate(profile,strokes,options);
        if (WritingProgramService.FindContinuousViolations(program,5).Count!=0)
            throw new Exception("Continuous pen invariant failed");
        File.WriteAllText(path,program,new System.Text.UTF8Encoding(false));
        Console.WriteLine(path);
    }
    static void Main(string[] args) {
        var profile=new MachineProfileService().Load(args[0]);
        if(profile.AxisMapping.XDirection!=1 || profile.AxisMapping.YDirection!=-1)
            throw new Exception("Fixtures require the factory-compatible X=1,Y=-1 mapping");
        // Current flipped 2D view: top-left view origin; view +X right, view +Y down.
        // With the factory-compatible YDirection=-1 profile, author rows at negative Y
        // so the emitted machine Y coordinates are positive. Operator verifies G54.
        profile.AxisMapping.WorkOrigin="G54";
        var shortStrokes=new List<WritingStroke>();
        for(int row=0;row<6;row++) for(int col=0;col<4;col++)
            shortStrokes.Add(S(col*5,-row*1.5,4));
        Save(profile,shortStrokes,400,Path.Combine(args[1],"pause-stop-short-strokes.nc"));
        var longStrokes=new List<WritingStroke>();
        for(int row=0;row<4;row++) longStrokes.Add(S(0,-row*2,20));
        Save(profile,longStrokes,100,Path.Combine(args[1],"pause-stop-longline-20mm.nc"));
    }
}
'@
[IO.File]::WriteAllText($sourcePath,$source,[Text.UTF8Encoding]::new($false))
$sources = @(
    'GRBL-Plotter\AutoWriter\Models\MachineProfile.cs',
    'GRBL-Plotter\AutoWriter\Models\WritingStroke.cs',
    'GRBL-Plotter\AutoWriter\Services\MachineProfileService.cs',
    'GRBL-Plotter\AutoWriter\Services\WritingProgramService.cs'
) | ForEach-Object { Join-Path $repoRoot $_ }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /out:$exe /r:System.Runtime.Serialization.dll $sources $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
& $exe (Join-Path $repoRoot 'artifacts\stage1\machine-profile-continuous-pen-confirmed.json') ([IO.Path]::GetFullPath($OutputDirectory))
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
Write-Host 'PASS: offline fixtures generated; no controller connection.'
