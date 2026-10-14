# Measures how long the font-pack discovery + full parse takes on the UI path.
# This is the suspected cause of the multi-second stall when the setup window opens.
# Usage: powershell -File scripts/test-fontpack-timing.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "exe missing: $exe" }

$source = @'
using System;
using System.Diagnostics;
using System.Reflection;

public static class FontPackTiming
{
    public static int Main()
    {
        Assembly asm = Assembly.LoadFrom(@"__EXE__");
        Type svc = asm.GetType("GrblPlotter.AutoWriter.Services.GlyphFontPackCatalog", true);
        Type pack = asm.GetType("GrblPlotter.AutoWriter.Services.GlyphFontPack", true);
        var discover = svc.GetMethod("DiscoverPackPaths", BindingFlags.Public | BindingFlags.Static);
        var load = pack.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);
        var peek = pack.GetMethod("Peek", BindingFlags.Public | BindingFlags.Static);
        var paths = (System.Collections.IList)discover.Invoke(null, new object[0]);
        Console.WriteLine("PACKS=" + paths.Count);
        double peekTotal = 0, loadTotal = 0;
        foreach (string p in paths)
        {
            var sw = Stopwatch.StartNew();
            object info = peek.Invoke(null, new object[] { p });
            sw.Stop();
            peekTotal += sw.Elapsed.TotalMilliseconds;
            Type infoType = asm.GetType("GrblPlotter.AutoWriter.Services.GlyphFontPackInfo", true);
            string pname = (string)infoType.GetProperty("Name").GetValue(info, null);
            int pcount = (int)infoType.GetProperty("GlyphCount").GetValue(info, null);
            Console.WriteLine("PEEK ms=" + sw.ElapsedMilliseconds + " glyphs=" + pcount + " name=" + pname + " file=" + System.IO.Path.GetFileName(p));
        }
        foreach (string p in paths)
        {
            var sw = Stopwatch.StartNew();
            object result = load.Invoke(null, new object[] { p });
            sw.Stop();
            string name = (string)pack.GetProperty("Name").GetValue(result, null);
            int count = (int)pack.GetProperty("GlyphCount").GetValue(result, null);
            loadTotal += sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("LOAD ms=" + sw.ElapsedMilliseconds + " glyphs=" + count + " name=" + name + " file=" + System.IO.Path.GetFileName(p));
        }
        Console.WriteLine("PEEK_TOTAL_MS=" + peekTotal.ToString("F0"));
        Console.WriteLine("LOAD_TOTAL_MS=" + loadTotal.ToString("F0"));
        return 0;
    }
}
'@
$source = $source.Replace('__EXE__', $exe.Replace('\', '\\'))
# Compile beside the target exe: discovery paths are relative to BaseDirectory.
$binDir = Split-Path -Parent $exe
$cs = Join-Path $binDir 'fontpack-timing.probe.cs'
$out = Join-Path $binDir 'fontpack-timing.probe.exe'
Set-Content -LiteralPath $cs -Value $source -Encoding ASCII
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /out:$out $cs | Out-Null
try { & $out } finally {
    Remove-Item -LiteralPath $cs -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
}
