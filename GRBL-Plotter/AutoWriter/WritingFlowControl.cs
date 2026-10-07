using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GrblPlotter.AutoWriter
{
    // Called under the serial sender's sendDataLock. ACK means accepted, never completed motion.
    internal sealed class WritingFlowControl
    {
        internal const int ControlLine = -24001;
        internal enum Phase { Running, Pausing, Paused, Stopping, Stopped, Fault }
        internal Phase State { get; private set; }
        internal bool Enabled { get; private set; }
        internal bool CanReturnOrigin { get; private set; }
        internal bool PenDown { get; private set; }
        internal bool SavedPenDown { get; private set; }
        private int pending;
        private long freshStatusFloor;
        internal bool BlocksProgram { get { return Enabled && State != Phase.Running; } }
        internal bool BlocksManual { get { return Enabled; } }

        internal void Begin(IList<string> lines, bool allowed)
        {
            Enabled = allowed && IsWritingProgram(lines);
            CanReturnOrigin = false;
            foreach (string line in lines)
                if (line.Trim() == "; End park: the pen is raised and the tool returns to work X0 Y0.") CanReturnOrigin = true;
            State = Phase.Running; pending = 0; PenDown = SavedPenDown = false;
        }
        internal static bool IsWritingProgram(IList<string> lines)
        {
            bool marker = false, mm = false, absolute = false, frame = false, perMinute = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith(";"))
                {
                    if (line == "; Continuous pen motion required: keep pen-down and drawing queued; do not wait for Idle between blocks.") marker = true;
                    continue;
                }
                if (line.Length == 0) continue;
                string compact = NormalizeWritingLine(line).Replace(" ", "").ToUpperInvariant();
                if (compact == "G21") { mm = true; continue; }
                if (compact == "G90") { absolute = true; continue; }
                if (compact == "G54") { frame = true; continue; }
                if (compact == "G94") { perMinute = true; continue; }
                // Generated programs contain explicit feeds on every XY and standard pen Z blocks.
                if (!mm || !absolute || !frame || !perMinute) return false;
                if (compact == "G1Z0F10000" || compact == "G1Z5F10000") continue;
                if (!Regex.IsMatch(compact, @"^G1X[-+]?(\d+(\.\d*)?|\.\d+)Y[-+]?(\d+(\.\d*)?|\.\d+)F\d+(\.\d*)?$") ) return false;
            }
            return marker && mm && absolute && frame && perMinute;
        }
        internal void ProgramQueued(string line)
        {
            string compact = line.Replace(" ", "").ToUpperInvariant();
            if (compact == "G1Z0F10000") PenDown = false;
            if (compact == "G1Z5F10000") PenDown = true;
        }

        internal static string NormalizeWritingLine(string line)
        {
            if (line.TrimStart().StartsWith(";")) return line;
            string compact = line.Replace(" ", "").ToUpperInvariant();
            compact = Regex.Replace(compact, @"^G01(?=[XYZ])", "G1");
            if (Regex.IsMatch(compact, @"^G1Z0(?:\.0+)?F10000(?:\.0+)?$")) return "G1Z0F10000";
            if (Regex.IsMatch(compact, @"^G1Z5(?:\.0+)?F10000(?:\.0+)?$")) return "G1Z5F10000";
            return compact;
        }
        private void Queue(string line, Action<string, int> enqueue)
        {
            pending++;
            enqueue(line, ControlLine);
        }
        internal bool Pause(bool safe, Action<string, int> enqueue)
        {
            if (!Enabled || !safe) { Cancel(); return false; }
            if (State != Phase.Running) return false;
            SavedPenDown = PenDown; State = Phase.Pausing;
            Queue("G90 G1 Z0 F10000", enqueue);
            return true;
        }
        internal bool Stop(bool safe, Action<string, int> enqueue)
        {
            if (!Enabled || !safe || !CanReturnOrigin) { Cancel(); return false; }
            if (State == Phase.Stopping || State == Phase.Stopped || State == Phase.Fault) return false;
            if (State != Phase.Pausing) Queue("G90 G1 Z0 F10000", enqueue);
            State = Phase.Stopping;
            Queue("G90 G1 X0 Y0 F1800", enqueue);
            return true;
        }
        internal bool Resume(bool safe, bool hasRemainingXY, Action<string, int> enqueue)
        {
            if (State != Phase.Paused) return false;
            if (!safe || (SavedPenDown && !hasRemainingXY)) { Cancel(); return false; }
            if (SavedPenDown) Queue("G90 G1 Z5 F10000", enqueue);
            State = Phase.Running;
            return true;
        }
        internal bool CanFinish(bool hostEmpty, long reportNumber)
        { return Enabled && State == Phase.Running && pending == 0 && hostEmpty && reportNumber >= freshStatusFloor; }
        internal void Ack(int lineNumber, long firstFreshReport)
        {
            if (lineNumber >= 0) freshStatusFloor = firstFreshReport;
            if (lineNumber == ControlLine && pending > 0)
            { pending--; if (pending == 0) freshStatusFloor = firstFreshReport; }
        }
        internal bool Status(bool idle, bool unsafeState, bool hostEmpty, long reportNumber)
        {
            if (!Enabled) return false;
            if (unsafeState) { Cancel(); return false; }
            // Only a report RECEIVED AFTER the final ACK can close this transaction.
            if (!idle || pending != 0 || !hostEmpty || reportNumber < freshStatusFloor) return false;
            if (State == Phase.Pausing) { State = Phase.Paused; return true; }
            if (State == Phase.Stopping) { State = Phase.Stopped; return true; }
            return false;
        }
        internal void Cancel() { if (Enabled) State = Phase.Fault; }
        internal void End() { Enabled = false; pending = 0; }
    }
}
