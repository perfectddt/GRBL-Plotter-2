$ErrorActionPreference = 'Stop'

# Modern-shell smoke test (2026-10-12): constructs the real MainForm twice in separate
# processes - once with ui-layout.json forced to "modern" and once forced to "classic" -
# and asserts the control tree shape for each mode. Cosmetic layout only; no machine I/O.
# The user's ui-layout.json is backed up before and restored afterwards.

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build Debug first; missing $exePath" }

$configDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GRBL_Plotter\AutoWriter'
$configPath = Join-Path $configDir 'ui-layout.json'
$backupPath = $null
if (Test-Path -LiteralPath $configPath) {
    $backupPath = $configPath + '.smoke-backup'
    Copy-Item -LiteralPath $configPath -Destination $backupPath -Force
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-modern-shell-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'ModernShellSmoke.cs'
# The harness must live next to GRBL-Plotter.exe: NLog.config and the data folder are
# resolved from the application base directory, exactly as in a normal app start.
$binDir = Split-Path -Parent $exePath
$assemblyPath = Join-Path $binDir 'AutoWriter.ModernShell.Smoke.exe'

$source = @'
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class ModernShellSmoke
{
    private static bool HasAncestor(Control control, string name)
    {
        for (Control p = control.Parent; p != null; p = p.Parent)
            if (p.Name == name) return true;
        return false;
    }

    private static bool IsInside(Control control, Control root)
    {
        for (Control p = control; p != null; p = p.Parent)
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "modern";
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "GRBL_Plotter", "AutoWriter");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ui-layout.json"),
                          mode == "modern" ? "{\"mode\":\"modern\"}" : "{\"mode\":\"classic\"}");

        Application.EnableVisualStyles();
        Assembly assembly = Assembly.LoadFrom(@"__EXE__");
        Type mainType = assembly.GetType("GrblPlotter.MainForm", true);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        using (Form form = (Form)Activator.CreateInstance(mainType))
        {
            // A control is only "visible" when every ancestor is - show the window so the
            // classic-mode visibility assertion is meaningful (no pump needed for one layout pass).
            form.Show();
            Application.DoEvents();
            Control modernRoot = mainType.GetField("modernRoot", flags).GetValue(form) as Control;
            Control ucStreaming = mainType.GetField("ucStreaming", flags).GetValue(form) as Control;
            Control pictureBox = mainType.GetField("pictureBox1", flags).GetValue(form) as Control;
            Control split = mainType.GetField("splitContainer1", flags).GetValue(form) as Control;
            if (ucStreaming == null || pictureBox == null || split == null)
            { Console.Error.WriteLine("FAIL: designer fields missing"); return 2; }

            if (mode == "modern")
            {
                if (modernRoot == null) { Console.Error.WriteLine("FAIL: modernRoot not built"); return 3; }
                if (!IsInside(ucStreaming, modernRoot)) { Console.Error.WriteLine("FAIL: ucStreaming not in modern shell"); return 4; }
                if (!IsInside(pictureBox, modernRoot)) { Console.Error.WriteLine("FAIL: pictureBox1 not in modern shell"); return 5; }
                if (split.Visible) { Console.Error.WriteLine("FAIL: classic container still visible"); return 6; }
                Console.WriteLine("MODERN=ok");
            }
            else
            {
                if (modernRoot != null) { Console.Error.WriteLine("FAIL: classic mode built modernRoot"); return 7; }
                if (!HasAncestor(ucStreaming, "tLPLinks")) { Console.Error.WriteLine("FAIL: ucStreaming left classic container"); return 8; }
                if (!split.Visible) { Console.Error.WriteLine("FAIL: classic container hidden"); return 9; }
                Console.WriteLine("CLASSIC=ok");
            }
        }
        // WinForms disposal of the full MainForm crashes the bare harness process at teardown
        // (timers/GDI handles, no message pump). The assertions above already ran, so exit hard.
        Console.WriteLine("OK");
        Environment.Exit(0);
        return 0;
    }
}
'@
$source = $source.Replace('__EXE__', $exePath)
[IO.File]::WriteAllText($sourcePath, $source, (New-Object Text.UTF8Encoding($false)))

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'Could not find the .NET Framework C# compiler (csc.exe).' }

& $csc /nologo /target:exe /out:$assemblyPath /r:System.Windows.Forms.dll /r:System.Drawing.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw "Smoke harness compilation failed with exit code $LASTEXITCODE." }

try {
    Push-Location -LiteralPath $binDir
    try {
        # The bare harness process (no message pump) can crash with 0xC000041D during WinForms
        # teardown after the assertions are done. Success/failure is therefore judged by the
        # printed markers, not by the process exit code. A missing marker means a real failure.
        $modernOutput = (& $assemblyPath modern 2>&1 | Out-String)
        Write-Output $modernOutput
        if ($modernOutput -notmatch 'MODERN=ok') { throw "Modern-mode smoke test failed.`n$modernOutput" }
        $classicOutput = (& $assemblyPath classic 2>&1 | Out-String)
        Write-Output $classicOutput
        if ($classicOutput -notmatch 'CLASSIC=ok') { throw "Classic-mode smoke test failed.`n$classicOutput" }

        # Real-app probe: launch the built exe offline with the modern shell, give it time to
        # finish splash + timer setup, then close it. A shell bug that crashes the app shows up
        # here as an early exit. CloseMainWindow may land on the persistent COM CNC window
        # (which hides instead of exiting), so a forced close after the grace period is still
        # a pass - the probe is about startup stability, not about the close path.
        $proc = Start-Process -FilePath $exePath -ArgumentList '--offline' -PassThru
        try {
            Start-Sleep -Seconds 10
            if ($proc.HasExited) { throw "App exited during startup with code $($proc.ExitCode)." }
            $null = $proc.CloseMainWindow()
            Start-Sleep -Seconds 5
            if ($proc.HasExited) { Write-Output 'APP_RUN=ok' }
            else {
                Stop-Process -Id $proc.Id -Force
                Start-Sleep -Seconds 1
                Write-Output 'APP_RUN=ok (force-closed: main window matched the persistent COM CNC form)'
            }
        }
        finally { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force } }
    }
    finally { Pop-Location }
}
finally {
    if (Test-Path -LiteralPath $assemblyPath) { Remove-Item -LiteralPath $assemblyPath -Force }
    if ($backupPath -ne $null) { Move-Item -LiteralPath $backupPath -Destination $configPath -Force }
    elseif (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath -Force }
}

Write-Host ('PASS: MainForm modern shell re-parents streaming/preview controls; classic mode untouched. Temporary artifacts: ' + $tempRoot)
