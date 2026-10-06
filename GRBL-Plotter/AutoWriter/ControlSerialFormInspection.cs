using System;
using System.IO;
using System.Text;

namespace GrblPlotter
{
    public partial class ControlSerialForm
    {
        private StringBuilder inspectionResponses;
        private System.Windows.Forms.Timer inspectionQueryTimer;
        internal void RunApprovedAxisStep()
        {
            if (Program.ApprovedAxisStep == null || inspectionResponses == null)
                throw new InvalidOperationException("No explicit axis step selected.");
            string responses = inspectionResponses.ToString();
            if (AutoWriter.InspectionPolicy.HasControllerRestart(responses))
                throw new InvalidOperationException("Controller restarted during inspection. Physical position and pen state must be reviewed before any movement.");
            if (!responses.Contains(" RX <Idle|") || !System.Text.RegularExpressions.Regex.IsMatch(responses, @" RX \[GC:G[01] G54 G17 G21 G90 G94 ") ||
                !responses.Contains(" RX [G92:0.000,0.000,0.000]") || !responses.Contains(" RX [TLO:0.000]") ||
                responses.Contains(" RX error:") || responses.Contains(" RX ALARM:"))
                throw new InvalidOperationException("Axis step requires reviewed idle, G54/mm/absolute state and zero G92/TLO offsets.");
            if (Program.ApprovedAxisStep.StartsWith("G1G90 Z", StringComparison.Ordinal))
            {
                var states = System.Text.RegularExpressions.Regex.Matches(responses, @" RX <([^|>]+)\|MPos:([^|>]+)");
                if (states.Count == 0) throw new InvalidOperationException("No current machine position received.");
                var lastState = states[states.Count - 1];
                var coordinates = lastState.Groups[2].Value.Split(',');
                double z;
                if (lastState.Groups[1].Value != "Idle" || coordinates.Length < 3 ||
                    !double.TryParse(coordinates[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z) ||
                    double.IsNaN(z) || double.IsInfinity(z) || z < 0 || z > 5)
                    throw new InvalidOperationException("Absolute pen test requires latest Idle and reported Z within 0..5. Remove pen and review physical clearance first.");
            }
            inspectionResponses.AppendLine("AXIS STEP REQUESTED: " + Program.ApprovedAxisStep);
            if (!RequestSend(Program.ApprovedAxisStep) || !RequestSend("G90"))
                throw new InvalidOperationException("Axis step or absolute-mode restoration was rejected.");
            RequestSend("$G");
        }
        internal void ConnectForMachineInspection(string port)
        {
            if (!Program.MachineInspectionMode || Program.OfflineMode)
                throw new InvalidOperationException("Single inspection requires online inspection mode.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(port, @"^COM[1-9][0-9]*$"))
                throw new ArgumentException("Expected an explicit COM port.");
            cbPort.Text = port;
            cbBaud.Text = "115200";
            CbEthernetUse.Checked = false;
            ConnectToGrbl();
            BeginMachineInspection();
        }

        internal void BeginMachineInspection()
        {
            if (!Program.MachineInspectionMode)
                throw new InvalidOperationException("请使用 --inspect-machine 启动后手动连接，再读取参数。");
            if (!IsConnectedToGrbl()) throw new InvalidOperationException("串口尚未连接。请先让奎享软件断开，并在串口窗口选择实际端口及波特率。");
            inspectionResponses = new StringBuilder();
            inspectionResponses.AppendLine("AutoWriter raw inspection — incomplete until responses reviewed");
            inspectionResponses.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            inspectionResponses.AppendLine("Port: " + cbPort.Text + "; baud: " + cbBaud.Text);
            RealtimeCommand((byte)'?');
            // Firmware welcome handling resets the sender queue. Collect boot messages
            // first, then request the complete inspection after startup has settled.
            if (inspectionQueryTimer != null) { inspectionQueryTimer.Stop(); inspectionQueryTimer.Dispose(); }
            inspectionQueryTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            inspectionQueryTimer.Tick += (sender, args) =>
            {
                inspectionQueryTimer.Stop();
                inspectionQueryTimer.Dispose();
                inspectionQueryTimer = null;
                if (!IsConnectedToGrbl() || IsDisposed || Disposing) return;
                QueueMachineInspectionQueries();
            };
            inspectionQueryTimer.Start();
        }

        private void QueueMachineInspectionQueries()
        {
            foreach (string query in new[] { "$I", "$$", "$G", "$#", "$N" })
            {
                inspectionResponses.AppendLine("QUERY QUEUED: " + query);
                if (!RequestSend(query)) inspectionResponses.AppendLine("QUERY REJECTED: " + query);
            }
        }

        private void CaptureInspectionResponse(string response)
        {
            if (Program.MachineInspectionMode && inspectionResponses != null && !string.IsNullOrEmpty(response))
            {
                if (inspectionResponses.Length < 1048576)
                    inspectionResponses.AppendLine(DateTime.UtcNow.ToString("O") + " RX " + response);
            }
        }

        internal void SaveMachineInspection(string path)
        {
            if (inspectionResponses == null) throw new InvalidOperationException("请先读取参数，等待串口响应后再导出。");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
                writer.Write(inspectionResponses.ToString());
        }
    }
}
