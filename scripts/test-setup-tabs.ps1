$ErrorActionPreference = 'Stop'

# Setup-tab layout test (2026-10-12): constructs the real MachineSetupForm with a throwaway
# state file, embeds it the way the modern shell does (TopLevel=false + Dock=Fill), shows it, and
# then checks the two things that decide whether the regrouped tabs look tidy:
#   1. every card of a tab starts at the same x and has the same width
#   2. every control inside one row shares the same vertical centre (one baseline)
# plus the older checks (every expected control present, visible, non-zero size, inside its card).
# Cosmetic only - no machine I/O.

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build Debug first; missing $exePath" }

$outDir = Join-Path $repoRoot 'artifacts\stage0'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dumpPath = Join-Path $outDir 'setup-tabs-dump.txt'
$sourcePath = Join-Path $outDir 'SetupTabsProbe.g.cs'   # kept on purpose: easiest place to debug a harness compile error

$binDir = Split-Path -Parent $exePath
$assemblyPath = Join-Path $binDir 'AutoWriter.SetupTabs.Probe.exe'

$source = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal static class SetupTabsProbe
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static readonly List<string> Failures = new List<string>();

    private static void W(string line) { Log.AppendLine(line); }

    private static string Text2(Control c)
    {
        string t = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
        if (t.Length > 22) t = t.Substring(0, 22) + "..";
        return t;
    }

    private static IEnumerable<Control> Enumerate(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Enumerate(child)) yield return nested;
        }
    }

    // UiGroup's card wrapper is a Panel holding exactly [rounded body, caption label], and each
    // row is a UiRow. The harness cannot reference those internal types at compile time, so
    // both are recognised by shape / type name.
    private static bool IsCard(Control control)
    {
        return control is Panel && control.Controls.Count == 2 &&
               control.Controls[0] is Panel && control.Controls[1] is Label;
    }

    private static bool IsRow(Control control)
    {
        return control.GetType().Name == "UiRow";
    }

    private static int CompareByTop(Control a, Control b)
    {
        int result = a.Top.CompareTo(b.Top);
        return result != 0 ? result : a.Left.CompareTo(b.Left);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(); }
        catch (Exception error) { W("EXCEPTION " + error); }

        // The result goes into the dump file as well: the harness process can die during
        // WinForms teardown (0xC000041D) before the console output is flushed.
        if (Failures.Count > 0)
        {
            W("RESULT=FAIL count=" + Failures.Count);
            foreach (string failure in Failures) W("  " + failure);
        }
        else W("RESULT=ok");

        File.WriteAllText(@"__DUMP__", Log.ToString(), Encoding.UTF8);
        if (Failures.Count > 0)
        {
            Console.Error.WriteLine("FAIL");
            foreach (string failure in Failures) Console.Error.WriteLine("  " + failure);
            Console.Error.WriteLine("FAILED=" + Failures.Count);
        }
        else Console.WriteLine("SETUP_TABS=ok");
        Console.Out.Flush();
        Environment.Exit(0);
        return 0;
    }

    private static void Run()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "autowriter-setup-tabs-state.json");
        try { if (File.Exists(statePath)) File.Delete(statePath); } catch (Exception) { }

        Application.EnableVisualStyles();
        Assembly assembly = Assembly.LoadFrom(@"__EXE__");
        Type setupType = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);

        object[] ctorArgs = new object[] { null, statePath, null, null, null, null };
        Form form = (Form)Activator.CreateInstance(setupType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, ctorArgs, null);

        // Embedding is the layout this test is about.
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
        host.Size = new Size(760, 620);
        host.Controls.Add(form);
        form.Dock = DockStyle.Fill;
        host.Show();
        form.Show();
        Application.DoEvents();

        W("FORM client=" + form.ClientSize + " visible=" + form.Visible);

        TabControl tabs = null;
        foreach (Control c in Enumerate(form)) { tabs = c as TabControl; if (tabs != null) break; }
        if (tabs == null) { W("RESULT=FAIL count=1"); W("  no TabControl found"); Console.Error.WriteLine("FAILED=1"); Environment.Exit(0); return; }

        DumpTab(tabs, "\u5e94\u7528\u4e0e\u5199\u5b57\u9884\u89c8", ApplyExpected());
        DumpTab(tabs, "\u533a\u57df\u6392\u7248", RegionExpected());
    }

    private static string[] ApplyExpected()
    {
        return new[] {
            "\u5bfc\u5165UTF-8\u6587\u672c", "\u5bfc\u5165\u56fe\u7247\u6216SVG", "\u7f3a\u5b57\u62a5\u544a",
            "\u8865\u5145\u7f3a\u5b57\u5b57\u5e93", "\u4e0a\u4e00\u9875", "\u4e0b\u4e00\u9875",
            "\u751f\u6210\u5199\u5b57\u9884\u89c8", "\u751f\u6210\u5e76\u8f7d\u5165\u4e3b\u754c\u9762\u9884\u89c8",
            "\u5237\u65b0\u5e94\u7528\u62a5\u544a", "\u68c0\u67e5\u8fde\u7eed\u6392\u961f",
            "\u5bfc\u51fa\u5199\u5b57\u7a0b\u5e8f", "\u4fdd\u5b58\u5199\u5b57\u9879\u76ee",
            "\u6253\u5f00\u5199\u5b57\u9879\u76ee" };
    }

    private static string[] RegionExpected()
    {
        return new[] {
            "\u591a\u533a\u57df\u6392\u7248", "\u4e0a\u4e00\u9875", "\u4e0b\u4e00\u9875",
            "\u7eb8\u5f20\u4e0e\u5b57\u4f53\u8bbe\u7f6e", "\u751f\u6210\u8868\u683c",
            "\u7f16\u8f91\u9009\u4e2d\u533a\u57df", "\u590d\u5236\u9009\u4e2d\u533a\u57df",
            "\u5220\u9664\u9009\u4e2d\u533a\u57df", "\u4e66\u5199\u987a\u5e8f\u63d0\u524d",
            "\u4e66\u5199\u987a\u5e8f\u63a8\u540e", "\u64a4\u9500", "\u91cd\u505a",
            "\u533a\u57df\u6a21\u677f", "\u7b14\u8d77\u70b9", "\u8bfb\u53d6\u9ed1\u8272\u8d77\u70b9",
            "\u533a\u57df\u7b14\u8ff9\u9884\u89c8", "\u751f\u6210\u5e76\u8f7d\u5165\u4e3b\u754c\u9762\u9884\u89c8",
            "\u4fdd\u5b58\u5199\u5b57\u9879\u76ee", "\u6253\u5f00\u5199\u5b57\u9879\u76ee",
            "\u5438\u96441mm", "\u7f51\u683c" };
    }

    private static void DumpTab(TabControl tabs, string tabName, string[] expected)
    {
        TabPage page = null;
        foreach (TabPage candidate in tabs.TabPages) if (candidate.Text == tabName) { page = candidate; break; }
        if (page == null) { Failures.Add("tab not found: " + tabName); return; }

        // A TabControl only renders its selected page, so select it before measuring anything.
        tabs.SelectedTab = page;
        Application.DoEvents();
        W("TAB " + tabName + " selected=" + (tabs.SelectedTab == page) + " client=" + page.ClientSize);

        var cards = new List<Control>();
        foreach (Control c in Enumerate(page)) if (IsCard(c)) cards.Add(c);
        W("TAB " + tabName + " cards=" + cards.Count);
        if (cards.Count == 0) { Failures.Add(tabName + ": no cards found"); return; }

        // (1) all cards on one left edge, all the same width, none collapsed
        Control first = cards[0];
        foreach (Control card in cards)
        {
            if (card.Left != first.Left) Failures.Add(tabName + " card left mismatch: " + card.Left + " vs " + first.Left);
            if (card.Width != first.Width) Failures.Add(tabName + " card width mismatch: " + card.Width + " vs " + first.Width);
            if (card.Height < 24) Failures.Add(tabName + " collapsed card h=" + card.Height);
        }

        // (2) one baseline per row: controls that share a visual line must share a centre.
        // A wrapped row legitimately has several lines, so group by Top before comparing.
        foreach (Control c in Enumerate(page))
        {
            if (!IsRow(c) || c.Controls.Count < 2) continue;
            var items = new List<Control>();
            foreach (Control child in c.Controls)
                if (child.Dock == DockStyle.None) items.Add(child);   // AddWide fills the whole row
            if (items.Count < 2) continue;
            items.Sort(CompareByTop);
            int lineTop = items[0].Top;
            int reference = items[0].Top + items[0].Height / 2;
            for (int index = 1; index < items.Count; index++)
            {
                Control child = items[index];
                if (child.Top - lineTop > 10)   // wrapped onto the next line
                {
                    lineTop = child.Top;
                    reference = child.Top + child.Height / 2;
                    continue;
                }
                int centre = child.Top + child.Height / 2;
                if (Math.Abs(centre - reference) > 1)
                    Failures.Add(tabName + " row baseline off by " + (centre - reference) + "px (" + Text2(child) + ")");
            }
        }

        // (3) every expected control still exists, is visible and sits inside its card
        foreach (string text in expected)
        {
            Control found = null;
            foreach (Control c in Enumerate(page))
            {
                if (c.Text != text) continue;
                if (c is Button || c is CheckBox) { found = c; break; }
            }
            if (found == null) { Failures.Add(tabName + " missing control: " + text); continue; }
            if (!found.Visible) { Failures.Add(tabName + " hidden control: " + text); continue; }
            if (found.Width <= 0 || found.Height <= 0) { Failures.Add(tabName + " zero-size control: " + text); continue; }
            Rectangle view = found.Parent.ClientRectangle;
            if (found.Bottom > view.Height + 4)
                Failures.Add(tabName + " control below card: " + text + " bottom=" + found.Bottom + " cardH=" + view.Height);
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
        if ($output -notmatch 'SETUP_TABS=ok') { throw "Setup-tab layout test failed.`n$output" }
    }
    finally { Pop-Location }
}
finally {
    if (Test-Path -LiteralPath $assemblyPath) { Remove-Item -LiteralPath $assemblyPath -Force }
}

Write-Host "PASS: setup tab cards share one left edge/width and every row shares one baseline. Dump: $dumpPath"
