$ErrorActionPreference = 'Stop'

# Constructs the AutoWriter machine setup dialog (including the new apply/writing tab) through
# reflection so the constructor and control wiring are exercised without opening a window.
# It also checks the send-integration wiring: the Action<string> constructor and the
# "load into main preview" button (matched via Unicode escapes to keep this script ASCII-only).
# It does not validate layout or interaction; those still need a human or on-screen pass.

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot 'GRBL-Plotter\bin\Debug\GRBL-Plotter.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build Debug first; missing $exePath" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-stage1-ui-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'UiSmoke.cs'
$assemblyPath = Join-Path $tempRoot 'AutoWriter.Stage1.UiSmoke.exe'

$source = @'
using System;
using System.Reflection;
using System.Windows.Forms;

internal static class UiSmoke
{
    private static Button FindButton(Control parent, string textPart)
    {
        foreach (Control control in parent.Controls)
        {
            Button button = control as Button;
            if (button != null && button.Text != null && button.Text.Contains(textPart)) return button;
            Button nested = FindButton(control, textPart);
            if (nested != null) return nested;
        }
        return null;
    }

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Assembly assembly = Assembly.LoadFrom(@"__EXE__");
        Type type = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);

        using (Form form = (Form)Activator.CreateInstance(type, true))
        {
            TabControl tabs = null;
            foreach (Control control in form.Controls)
                if (control is TabControl) tabs = (TabControl)control;
            if (tabs == null) { Console.Error.WriteLine("FAIL: no TabControl found"); return 2; }
            Console.WriteLine("TABPAGES=" + tabs.TabPages.Count);
            if (tabs.TabPages.Count != 4) { Console.Error.WriteLine("FAIL: expected 4 tabs"); return 3; }
            if (FindButton(form, "\u4E3B\u754C\u9762\u9884\u89C8") == null) { Console.Error.WriteLine("FAIL: load-into-preview button missing"); return 4; }
        }

        ConstructorInfo withCallback = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new[] { typeof(Action<string>) }, null);
        if (withCallback == null) { Console.Error.WriteLine("FAIL: no Action<string> constructor"); return 5; }
        Action<string> sink = s => { };
        using (Form form2 = (Form)withCallback.Invoke(new object[] { sink }))
        {
            if (form2 == null) { Console.Error.WriteLine("FAIL: callback constructor returned null"); return 6; }
        }
        Console.WriteLine("CALLBACK_CTOR=ok");
        Console.WriteLine("OK");
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

& $assemblyPath
if ($LASTEXITCODE -ne 0) { throw "UI smoke test failed with exit code $LASTEXITCODE." }

Write-Host ('PASS: AutoWriter setup dialog (4 tabs incl. apply/writing) + load-into-preview wiring constructed. Temporary artifacts: ' + $tempRoot)
