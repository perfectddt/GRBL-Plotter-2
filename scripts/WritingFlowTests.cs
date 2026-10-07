using System;
using System.Collections.Generic;
using GrblPlotter;
using GrblPlotter.AutoWriter;

// Transport mock uses the production transaction and production CodeBuffer. No serial ports opened.
internal static class WritingFlowTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static readonly string[] Program = {
        "G21", "G90", "G94", "G54",
        "; Continuous pen motion required: keep pen-down and drawing queued; do not wait for Idle between blocks.",
        "; End park: the pen is raised and the tool returns to work X0 Y0.",
        "G1 Z0 F10000", "G1 X0 Y0 F1800", "G1 Z5 F10000", "G1 X20 Y0 F100", "G1 X20 Y1 F100", "G1 Z0 F10000" };
    private sealed class MockSender
    {
        internal WritingFlowControl Flow = new WritingFlowControl();
        internal CodeBuffer Host = new CodeBuffer();
        internal List<string> Wire = new List<string>();
        internal int ProgramAcks;
        internal MockSender() { Flow.Begin(Program, true); }
        internal void Queue(string line, int tag) { Host.Add(line.Replace(" ", ""), tag); }
        internal void ProgramLine(string line) { Queue(line, 3); Flow.ProgramQueued(line); }
        internal void Send() { while (Host.OkToSend(127)) { Wire.Add(Host.GetSentLine()); Host.LineWasSent(); } }
        internal void Ack(long floor)
        {
            int tag = Host.GetConfirmedLineNr();
            Flow.Ack(tag, floor);
            if (tag >= 0) ProgramAcks++;
            Host.LineWasReceived();
        }
        internal bool Idle(long report) { return Flow.Status(true, false, Host.IndexConfirmed == Host.Count, report); }
    }
    private static MockSender LongStroke()
    {
        var m = new MockSender();
        m.ProgramLine("G1 Z5 F10000"); m.ProgramLine("G1 X20 Y0 F100"); m.Send();
        return m;
    }
    private static void Main(string[] args)
    {
        foreach (string path in args) Check(WritingFlowControl.IsWritingProgram(System.IO.File.ReadAllLines(path)), "production fixture recognized " + path);
        Check(WritingFlowControl.IsWritingProgram(Program), "strict generated program admitted");
        Check(WritingFlowControl.IsWritingProgram(new List<string>(Program).ConvertAll(line => line.Replace("G1 ", "G01 ").Replace("Z0 ", "Z0.000 ").Replace("Z5 ", "Z5.000 "))), "offset formatter equivalent codes admitted");
        Check(WritingFlowControl.NormalizeWritingLine("G01 Z5.000 F10000") == "G1Z5F10000", "offset pen-down normalized for paired transport");
        Check(WritingFlowControl.NormalizeWritingLine("G01 Z0.000 F10000") == "G1Z0F10000", "offset pen-up normalized");
        foreach (string bad in new [] { "G91", "G55", "G10L20P0X0", "G4P1", "M3", "G1Z1F10000", "G0X0Y0", "G1X0Y0" })
        { var p = new List<string>(Program); p.Add(bad); Check(!WritingFlowControl.IsWritingProgram(p), "reject " + bad); }
        var noMarker = new List<string>(Program); noMarker.RemoveAt(4);
        Check(!WritingFlowControl.IsWritingProgram(noMarker), "arbitrary G-code rejected");

        var m = LongStroke();
        Check(m.Flow.Pause(true, m.Queue), "long stroke pause accepted"); m.Send();
        Check(m.Wire[2] == "G90G1Z0F10000", "lift queued after already sent XY");
        Check(!m.Idle(1), "old Idle before ACK ignored");
        Check(!m.Flow.Resume(true, true, m.Queue), "pending resume rejected");
        Check(!m.Flow.Pause(true, m.Queue) && m.Host.Count == 3, "rapid pause idempotent");
        m.Ack(3); Check(!m.Idle(2), "partial ACK ignored"); m.Ack(3); m.Ack(3);
        Check(m.ProgramAcks == 2, "injected ACK does not consume program line");
        Check(!m.Idle(2), "delayed status from pre-ACK query ignored");
        Check(!m.Flow.Status(false, false, true, 3), "all ACK but controller Run ignored");
        Check(m.Idle(3) && m.Flow.SavedPenDown, "fresh Idle completes pause and preserves target Z5");
        Check(m.Flow.Resume(true, true, m.Queue), "resume accepted"); m.ProgramLine("G1 X20 Y1 F100"); m.Send();
        Check(m.Wire[3] == "G90G1Z5F10000" && m.Wire[4] == "G1X20Y1F100", "resume Z5 immediately followed by original XY with original feed");
        Check(m.ProgramAcks == 2, "resume does not skip original lines");

        m = LongStroke(); m.Flow.Pause(true, m.Queue);
        Check(m.Flow.Stop(true, m.Queue), "pause upgraded to stop");
        Check(!m.Flow.Stop(true, m.Queue) && m.Host.Count == 4, "stop idempotent, only one lift and home"); m.Send();
        Check(m.Wire[3] == "G90G1X0Y0F1800", "safe feed return same work frame, no rapid");
        for (int i = 0; i < 3; i++) m.Ack(5);
        Check(!m.Idle(5), "home missing ACK cannot complete"); m.Ack(5);
        Check(!m.Idle(4) && m.Idle(5), "stop completion requires final ACK and causally fresh Idle");
        Check(m.Flow.State == WritingFlowControl.Phase.Stopped && !m.Flow.Resume(true, true, m.Queue), "stopped cannot resume");

        m = LongStroke(); m.Flow.Pause(true, m.Queue); m.Send(); for(int i=0;i<3;i++)m.Ack(1); m.Idle(1);
        Check(m.Flow.Stop(true, m.Queue), "stop from paused"); m.Send();
        Check(m.Wire[3] == "G90G1Z0F10000" && m.Wire[4] == "G90G1X0Y0F1800", "paused stop lift then home");

        m = new MockSender(); m.ProgramLine("G1 Z0 F10000"); m.Send(); m.Ack(1);
        Check(m.Flow.Pause(true, m.Queue), "pause at queue tail"); m.Send(); m.Ack(2);
        Check(m.Idle(2) && !m.Flow.SavedPenDown, "tail pen-up pause completes");
        Check(m.Flow.Resume(true, false, m.Queue), "tail pen-up resume needs no unpaired pen-down");

        foreach (string cause in new [] { "error", "reset", "disconnect", "alarm", "raw hold", "stop lock" })
        {
            m = LongStroke(); m.Flow.Pause(true, m.Queue); m.Send();
            m.Flow.Cancel(); for(int i=0;i<3;i++)m.Ack(1);
            Check(!m.Idle(1) && m.Flow.BlocksProgram && !m.Flow.Resume(true,true,m.Queue) && !m.Flow.Stop(true,m.Queue), "conservative cancel " + cause);
        }
        m = LongStroke(); Check(!m.Flow.Pause(false,m.Queue) && m.Host.Count == 2, "inherited HOLD sends no lift");
        m = LongStroke(); m.Flow.Pause(true,m.Queue); m.Send(); for(int i=0;i<3;i++)m.Ack(1); m.Idle(1);
        Check(!m.Flow.Resume(false,true,m.Queue), "coordinate frame changed blocks resume");
        m = LongStroke(); m.Flow.Pause(true,m.Queue); m.Send(); for(int i=0;i<3;i++)m.Ack(1); m.Idle(1);
        Check(!m.Flow.Resume(true,false,m.Queue), "tail pen-down blocks unpaired Z5");
        m = LongStroke(); m.Ack(4); m.Ack(4);
        Check(!m.Flow.CanFinish(true, 3) && m.Flow.CanFinish(true, 4), "normal queue-tail completion also requires a fresh post-ACK query");
        Check(!m.Flow.CanFinish(false, 4), "residual host queue blocks completion");
        MotionChecks();
        Console.WriteLine("PASS: " + checks + " writing control state and production CodeBuffer transport checks; no machine IO.");
    }
    private static void MotionChecks()
    {
        var noG94 = new List<string>(Program); noG94.Remove("G94");
        Check(!WritingFlowControl.IsWritingProgram(noG94), "time model requires explicit G94");
        double px = 0, py = 0;
        var slices = WritingMotionBudget.Split("G1X20Y0F100", ref px, ref py, 500000);
        Check(slices.Count == 100 && px == 20 && py == 0 && slices[99] == "G1X20Y0F100", "20mm F100 split 100 slices preserving exact endpoint");
        var buffer = new CodeBuffer();
        double previous = 0, maxDuration = 0;
        foreach (string slice in slices)
        {
            double x, y, f; Check(WritingMotionBudget.TryXY(slice, out x, out y, out f), "finite slice parsed");
            maxDuration = Math.Max(maxDuration, (x - previous) * 60 / f);
            Check(y == 0 && f == 100 && x > previous, "geometry and explicit feed preserved"); previous = x;
            buffer.Add(slice, 9);
        }
        Check(maxDuration <= 0.12000001 && buffer.LineNr.TrueForAll(n => n == 9), "per-slice time and source line mapping");
        for (int i = 0; i < 3; i++) { buffer.LineWasSent(); buffer.LineWasReceived(); }
        var flow = new WritingFlowControl(); flow.Begin(Program, true); flow.ProgramQueued("G1Z5F10000");
        var controls = new CodeBuffer();
        flow.Pause(true, (line, tag) => controls.Add(line, tag)); flow.Ack(WritingFlowControl.ControlLine, 1);
        Check(flow.Status(true, false, true, 1), "split pause closes after control ACK and fresh Idle");
        string remaining = buffer.GetSentLine();
        Check(remaining == slices[3] && buffer.IndexConfirmed == 3, "pause does not rewind or skip slice cursor");
        Check(flow.Resume(true, true, (line, tag) => controls.Add(line, tag)), "resume split stroke");
        string packet = WritingMotionBudget.PenDownPacket(controls.Buffer[1].Replace(" ", ""), remaining, "\n");
        Check(packet == "G90G1Z5F10000\n" + slices[3] + "\n", "production packet keeps Z5 immediately adjacent to exactly next slice");
        buffer.LineWasSent(); buffer.LineWasReceived();
        Check(buffer.GetSentLine() == slices[4], "resume continues exactly once at subsequent slice");
        bool threw = false;
        try { WritingMotionBudget.PenDownPacket("G1Z5F10000", "G1Z0F10000", "\n"); } catch (ArgumentException) { threw = true; }
        Check(threw, "unpaired Z5 cannot become wire packet");
        foreach (string bad in new [] { "G1X20Y0F0", "G1X" + new string('9', 400) + "Y0F100", "G1X20Y0F0.000001" })
        {
            threw = false; px = py = 0;
            try { WritingMotionBudget.Split(bad, ref px, ref py, 500000); } catch (ArgumentException) { threw = true; }
            Check(threw && px == 0 && py == 0, "invalid/extreme expansion rejected atomically");
        }
        px = py = 0; threw = false;
        try { WritingMotionBudget.Split("G1X20Y0F100", ref px, ref py, 99); } catch (ArgumentException) { threw = true; }
        Check(threw, "total expanded size limit");
        int free;
        Check(WritingMotionBudget.TryPlannerFree("<Idle|Bf:15,128>", out free) && free == 15, "raw fresh Bf extracted");
        foreach (string bad in new [] { "<Idle|FS:0,0>", "<Idle|Bf:x,128>", "<Idle|Bf:15,0>", "<Idle|Bf:1025,128>", "<Idle|Bf:15,128,128>" })
            Check(!WritingMotionBudget.TryPlannerFree(bad, out free), "missing/malformed Bf rejected");
        var budget = new WritingMotionBudget(); budget.Begin(15);
        Check(!budget.Reserve(1, 0.12), "no invented initial credit");
        budget.QuerySent(true); Check(budget.Report("<Idle|Bf:15,128>", true, false), "fresh post-ACK Idle report admits budget");
        Check(budget.Reserve(2, 0.15) && budget.Reserve(2, 0.24), "bundle plus slices consume four credits");
        Check(!budget.Reserve(1, 0.01), "stale free Bf cannot release another block");
        budget.Report("<Idle|Bf:15,128>", true, false);
        Check(!budget.Reserve(1, 0.01), "unsolicited repeated Bf cannot replenish consumed credit");
        budget.QuerySent(false); budget.Report("<Idle|Bf:15,128>", true, false);
        Check(!budget.Reserve(1, 0.01), "partial ACK query cannot replenish");
        budget.QuerySent(true); budget.Report("<Run|Bf:12,128>", false, true);
        Check(!budget.Reserve(1, 0.01), "three queued plus executing Run block saturates budget");
        budget.QuerySent(true); budget.Report("<Run|Bf:13,128>", false, true);
        Check(budget.Reserve(1, 0.12) && !budget.Reserve(1, 0.01), "Run executing block included in credit calculation");
        budget.QuerySent(true); budget.Report("<Run|Bf:0,128>", false, true);
        Check(!budget.Reserve(1, 0.01), "full planner never sends body");
        budget.QuerySent(true); budget.Report("<Idle|Bf:15,128>", true, false);
        Check(budget.Reserve(1, 0.4) && !budget.Reserve(1, 0.09), "accumulated nominal time horizon limits even spare slots");
        budget.QuerySent(true); Check(!budget.Report("<Idle|Bf:16,128>", true, false) && budget.Fault, "wrong capacity fails closed");
        budget = new WritingMotionBudget(); budget.Begin(15); budget.QuerySent(true);
        Check(!budget.Report("<Idle|FS:0,0>", true, false) && !budget.Reserve(1, 0.12), "missing current report Bf never uses cached Bf");
        px = py = 0; var legacy = WritingMotionBudget.Split("G90", ref px, ref py, 500000);
        Check(legacy.Count == 1 && legacy[0] == "G90", "nonmotion header preserved");
    }
}
