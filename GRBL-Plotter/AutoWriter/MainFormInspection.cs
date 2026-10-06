using System;
using System.IO;
using System.Windows.Forms;

namespace GrblPlotter
{
    public partial class MainForm
    {
        private void RunSingleMachineInspection()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.StartupPath, "..", "..", "..", "artifacts", "stage1"));
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, "inspection-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".txt");
            var timer = new Timer { Interval = 12000 };
            var stepTimer = new Timer { Interval = 5000 };
            stepTimer.Tick += (sender, args) =>
            {
                stepTimer.Stop();
                try { _serial_form.RunApprovedAxisStep(); }
                catch (Exception error) { File.WriteAllText(output + ".step-error.txt", error.ToString()); }
                finally { stepTimer.Dispose(); }
            };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                stepTimer.Stop();
                try { _serial_form.SaveMachineInspection(output); }
                catch (Exception error) { File.WriteAllText(output + ".error.txt", error.ToString()); }
                finally { _serial_form.DisconnectFromGrbl(null, null); timer.Dispose(); Close(); }
            };
            try
            {
                _serial_form.ConnectForMachineInspection(Program.InspectionOncePort);
                if (Program.ApprovedAxisStep != null) stepTimer.Start();
                timer.Start();
            }
            catch (Exception error)
            {
                File.WriteAllText(output + ".error.txt", error.ToString());
                timer.Dispose();
                stepTimer.Dispose();
                _serial_form.DisconnectFromGrbl(null, null);
                Close();
            }
        }
    }
}
