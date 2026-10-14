$ErrorActionPreference = 'Stop'

# Machine-setup nav width probe (2026-10-14): constructs the real MachineSetupForm, shows it as a
# normal top-level window at its default size, then dumps the SplitContainer metrics of every
# settings tab (SplitterDistance, Panel1MinSize, splitter width, card width) so we can see what the
# left navigation column really measures at runtime. Cosmetic only - no machine I/O.

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build Debug first; missing $exePath" }

$outDir = Join-Path $repoRoot 'artifacts\stage0'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dumpPath = Join-Path $outDir 'setup-nav-dump.txt'
$sourcePath = Join-Path $outDir 'SetupNavProbe.g.cs'   # kept on purpose: easiest place to debug a harness compile error

$binDir = Split-Path -Parent $exePath
$assemblyPath = Join-Path $binDir 'AutoWriter.SetupNav.Probe.exe'

$source = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal static class SetupNavProbe
{
    private static readonly StringBuilder Log = new StringBuilder();

    private static void W(string line) { Log.AppendLine(line); }

    private static IEnumerable<Control> Enumerate(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Enumerate(child)) yield return nested;
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(); }
        catch (Exception error) { W("EXCEPTION " + error); }

        File.WriteAllText(@"__DUMP__", Log.ToString(), Encoding.UTF8);
        Console.WriteLine(Log.ToString());
        Console.Out.Flush();
        Environment.Exit(0);
        return 0;
    }

    private static void Run()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "autowriter-setup-nav-state.json");
        try { if (File.Exists(statePath)) File.Delete(statePath); } catch (Exception) { }

        Application.EnableVisualStyles();
        Assembly assembly = Assembly.LoadFrom(@"__EXE__");
        Type setupType = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);

        object[] ctorArgs = new object[] { null, statePath, null, null, null, null };
        Form form = (Form)Activator.CreateInstance(setupType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, ctorArgs, null);

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-2000, -2000);
        form.Load += (s, e) =>
        {
            W("AT-LOAD form=" + form.Size);
            foreach (Control c in Enumerate(form))
            {
                SplitContainer split = c as SplitContainer;
                if (split != null) W("AT-LOAD split w=" + split.Width + " h=" + split.Height + " dist=" + split.SplitterDistance);
            }
        };
        form.Show();
        Application.DoEvents();

        W("SCREEN formSize=" + form.Size + " client=" + form.ClientSize);

        TabControl tabs = null;
        foreach (Control c in Enumerate(form)) { tabs = c as TabControl; if (tabs != null) break; }
        if (tabs == null) { W("no TabControl found"); return; }

        DumpTab(tabs, "\u673a\u5668\u914d\u7f6e");        // machine config tab
        DumpTab(tabs, "\u6b65\u8fdb\u8ba1\u7b97\u4e0e\u6821\u51c6"); // calibration tab
        DumpTab(tabs, "\u5e94\u7528\u4e0e\u5199\u5b57\u9884\u89c8"); // apply & writing tab

        // Root-cause check: can the distance be assigned after the form is shown and laid out?
        tabs.SelectedIndex = 0;
        Application.DoEvents();
        foreach (Control c in Enumerate(tabs.SelectedTab))
        {
            SplitContainer split = c as SplitContainer;
            if (split == null) continue;
            try { split.SplitterDistance = 360; W("POSTSHOW assign ok -> dist=" + split.SplitterDistance); }
            catch (Exception error) { W("POSTSHOW assign failed: " + error.GetType().Name + " " + error.Message); }
        }
    }

    private static void DumpTab(TabControl tabs, string tabName)
    {
        TabPage page = null;
        foreach (TabPage candidate in tabs.TabPages) if (candidate.Text == tabName) { page = candidate; break; }
        if (page == null) { W("TAB " + tabName + " not found"); return; }

        tabs.SelectedTab = page;
        Application.DoEvents();
        W("TAB " + tabName + " client=" + page.ClientSize);

        foreach (Control c in Enumerate(page))
        {
            SplitContainer split = c as SplitContainer;
            if (split == null) continue;
            W("  SPLIT w=" + split.Width + " h=" + split.Height +
              " dist=" + split.SplitterDistance +
              " panel1Min=" + split.Panel1MinSize + " panel2Min=" + split.Panel2MinSize +
              " splitterW=" + split.SplitterWidth + " fixed=" + split.IsSplitterFixed +
              " panel1w=" + split.Panel1.Width + " panel2w=" + split.Panel2.Width);
        }

        // card widths inside the left column (a UiGroup card = Panel with [rounded body, caption])
        foreach (Control c in Enumerate(page))
        {
            if (c is Panel && c.Controls.Count == 2 && c.Controls[0] is Panel && c.Controls[1] is Label)
                W("  CARD w=" + c.Width + " left=" + c.Left + " text=" + c.Controls[1].Text);
        }
    }
}
'@
$source = $source.Replace('__EXE__', $exePath).Replace('__DUMP__', $dumpPath)
[IO.File]::WriteAllText($sourcePath, $source, (New-Object Text.UTF8Encoding($false)))

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'Could not find the .NET Framework C# compiler (csc.exe).' }

& $csc /nologo /target:exe /out:$assemblyPath /r:System.Windows.Forms.dll /r:System.Drawing.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed with exit code $LASTEXITCODE. Source kept at $sourcePath" }

try {
    Push-Location -LiteralPath $binDir
    try {
        $output = (& $assemblyPath 2>&1 | Out-String)
        Write-Output $output
    }
    finally { Pop-Location }
}
finally {
    if (Test-Path -LiteralPath $assemblyPath) { Remove-Item -LiteralPath $assemblyPath -Force }
}

Write-Host "DONE. Dump: $dumpPath"
