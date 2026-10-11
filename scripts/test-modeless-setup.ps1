$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('autowriter-modeless-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$main = [IO.File]::ReadAllText((Join-Path $repoRoot 'GRBL-Plotter\GUI\MainForm.cs'))
$start = $main.IndexOf('        private void ShowMachineSetup()')
$end = $main.IndexOf('        private void MainForm_FormClosing', $start)
$method = $main.Substring($start, $end - $start)
$source = @'
using System;
using System.Windows.Forms;
namespace AutoWriter {
 public class MachineSetupForm : Form {
  public Action<string> Sink; public static int Saved;
  public MachineSetupForm(Action<string> sink){Sink=sink;}
  protected override void OnFormClosing(FormClosingEventArgs e){base.OnFormClosing(e);if(!e.Cancel)Saved++;}
 }
}
class MainForm : Form {
 private bool shutDown;
 private AutoWriter.MachineSetupForm machineSetupForm;
 private int loads;
 private void LoadWritingIntoPreview(string code){loads++;}
 METHOD
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 [STAThread] static void Main(){
  using(var main=new MainForm()) {
   var edit=new TextBox();main.Controls.Add(edit);main.Show();
   main.ShowMachineSetup();Application.DoEvents();var first=main.machineSetupForm;
   Check(main.Enabled && edit.Enabled,"main remains enabled");
   edit.Text="edited while setup open";Check(edit.Text.Length>0,"main editor can change");
   Check(first.Owner==main && !first.Modal,"owned modeless setup");
   main.ShowMachineSetup();Check(main.machineSetupForm==first && main.OwnedForms.Length==1,"single instance");
   first.WindowState=FormWindowState.Minimized;main.ShowMachineSetup();Check(first.WindowState==FormWindowState.Normal,"restore minimized");
   first.Sink("code");Check(main.loads==1,"live callback");
   first.Close();Check(main.machineSetupForm==null && first.IsDisposed,"close disposes and clears reference");
   main.ShowMachineSetup();var second=main.machineSetupForm;Check(second!=first,"reopen fresh instance");
   main.shutDown=true;second.Sink("code");Check(main.loads==1,"shutdown callback ignored");
   var sink=second.Sink;main.Close();Application.DoEvents();
   Check(second.IsDisposed && AutoWriter.MachineSetupForm.Saved==2,"owner close closes setup and runs save hook");
   sink("code");Check(main.loads==1,"disposed callback ignored");
  }
  Console.WriteLine("PASS modeless lifecycle: main enabled/editable, single instance, restore, close/reopen, owner close/save, callback guards");
 }
}
'@
$source = $source.Replace(' METHOD', $method)
$sourcePath = Join-Path $tempRoot 'ModelessTests.cs'
[IO.File]::WriteAllText($sourcePath, $source)
$testExe = Join-Path $tempRoot 'ModelessTests.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /out:$testExe /r:System.Windows.Forms.dll /r:System.Drawing.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Modeless test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Modeless lifecycle checks failed.' }
