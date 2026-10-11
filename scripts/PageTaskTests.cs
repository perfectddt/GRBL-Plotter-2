using System;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Services;

class PageTaskTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    static PageCompletionWitness Physical()
    {
        return PageCompletionWitness.FromController(true, true, true, true, true);
    }

    static PageTaskService Arm(params string[] programs)
    {
        var task = new PageTaskService();
        string refusal;
        Check(task.Bind("fingerprint", programs, out refusal) == PageTaskBindResult.Created, "bind " + programs.Length);
        Check(refusal == null, "bind refusal empty");
        Check(task.PeekUnloadedProgram() == programs[0], "first page offered once");
        task.NoteLoaded();
        Check(task.PeekUnloadedProgram() == null, "loaded page is not offered again");
        Check(task.Phase == PageTaskPhase.WaitingStart, "waiting to start");
        Check(task.StatusText.Contains("等待开始"), "status waiting");
        return task;
    }

    static void Main()
    {
        var percent = new PageCompletionWitness { ReportedPercent = true, ReportedLastOk = true, AllSent = true, AllAcknowledged = true };
        Check(!percent.IsPhysicalComplete, "percent and last ok are not physical completion");
        Check(!PageCompletionWitness.FromController(true, true, true, true, false).IsPhysicalComplete, "missing idle barrier");
        Check(!PageCompletionWitness.FromController(true, true, false, true, true).IsPhysicalComplete, "pen still down");
        Check(!PageCompletionWitness.FromController(true, true, true, false, true).IsPhysicalComplete, "not connected");
        Check(!PageCompletionWitness.FromController(true, false, true, true, true).IsPhysicalComplete, "not acknowledged");
        Check(Physical().IsPhysicalComplete, "idle barrier completes");

        var task = Arm("PAGE-1", "PAGE-2");
        Check(task.PageCount == 2 && task.CurrentPage == 1 && task.CompletedCount == 0, "two pages armed");
        Check(task.StartRefusal("OTHER") != null, "unrelated code blocked while bound");
        Check(task.StartRefusal("PAGE-1") == null, "armed page may start");
        Check(task.NoteStarted(), "start once");
        Check(!task.NoteStarted(), "second start ignored");
        Check(task.StartRefusal("PAGE-1") != null, "repeat start refused");
        Check(task.NoteDispatch(true, false) && task.StatusText.Contains("已发送，等待应答"), "sent is not paper change");
        Check(task.Phase == PageTaskPhase.Writing, "still writing after send");
        Check(task.NoteDispatch(true, true) && task.StatusText.Contains("已应答，等待空闲"), "ack waits for idle");
        task.NoteFinish(percent);
        Check(task.Phase == PageTaskPhase.Unknown && task.CurrentPage == 1 && task.CompletedCount == 0, "finish without idle is unknown");
        Check(task.StatusText.Contains("未知"), "unknown shown");
        Check(!task.ConfirmPaper(), "unknown does not prepare the next page");
        Check(task.StartRefusal("PAGE-1") != null, "unknown page cannot be resent");
        Check(task.StartRefusal("G0 X0") == null, "other programs stay available");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NotePaused();
        Check(task.Phase == PageTaskPhase.Paused && task.StatusText.Contains("暂停"), "paused");
        task.NoteFinish(Physical());
        Check(task.Phase == PageTaskPhase.Paused, "pause does not enter paper change");
        task.NoteResumed();
        Check(task.Phase == PageTaskPhase.Writing, "resume");
        task.NoteStop(false);
        Check(task.Phase == PageTaskPhase.Cancelled && task.CurrentPage == 1 && task.CompletedCount == 0, "stop does not complete");
        Check(!task.ConfirmPaper(), "cancelled does not prepare next");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteStop(true);
        Check(task.Phase == PageTaskPhase.Unknown && task.StatusText.Contains("未知"), "unknown stop");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteAlarm();
        Check(task.Phase == PageTaskPhase.Unknown && task.CurrentPage == 1, "alarm does not advance");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteDisconnect();
        Check(task.Phase == PageTaskPhase.Unknown && !task.ConfirmPaper(), "disconnect does not advance");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteCommandError();
        Check(task.Phase == PageTaskPhase.Fault && task.CompletedCount == 0, "command error is not completion");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteFinish(Physical());
        Check(task.Phase == PageTaskPhase.WaitingPaper && task.CompletedCount == 1 && task.CurrentPage == 1, "first page waits for paper");
        Check(task.StatusText.Contains("等待换纸"), "paper status");
        Check(task.PaperButtonEnabled, "paper button on");
        Check(task.StartRefusal("PAGE-1") != null && task.StartRefusal("PAGE-2") != null, "next page blocked before confirm");
        Check(task.ConfirmPaper(), "confirm prepares next");
        Check(task.Phase == PageTaskPhase.WaitingStart && task.CurrentPage == 2 && task.LoadedPage == 0, "next page not loaded yet");
        Check(!task.ConfirmPaper(), "second confirm does not advance");
        Check(task.PeekUnloadedProgram() == "PAGE-2", "page 2 offered once");
        task.NoteLoaded();
        Check(task.PeekUnloadedProgram() == null, "page 2 not loaded twice");
        Check(task.StartRefusal("PAGE-2") == null, "confirmed page can be started manually");
        Check(task.StartRefusal("PAGE-1") != null, "finished page is not sent again");
        string again;
        Check(task.Bind("fingerprint", new[] { "PAGE-1", "PAGE-2" }, out again) == PageTaskBindResult.AlreadyArmed, "same freeze does not reload");
        Check(task.CurrentPage == 2 && task.LoadedPage == 2, "already armed keeps the prepared page");

        task.NoteStarted();
        task.NoteFinish(Physical());
        Check(task.Phase == PageTaskPhase.Completed && task.CompletedCount == 2, "last page completes the task");
        Check(!task.PaperButtonEnabled && !task.ConfirmPaper(), "no paper change after the last page");

        task = Arm("ONLY");
        task.NoteStarted();
        task.NoteFinish(Physical());
        Check(task.Phase == PageTaskPhase.Completed && task.CurrentPage == 1, "single page completes directly");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.NoteFinish(Physical());
        task.Invalidate("文本已改");
        Check(task.Phase == PageTaskPhase.Fault && task.StatusText.Contains("异常"), "edit drops the frozen task");
        Check(!task.ConfirmPaper() && task.StartRefusal("PAGE-1") != null, "old page code cannot continue");

        task = Arm("PAGE-1", "PAGE-2");
        task.NoteStarted();
        task.Invalidate("映射已改");
        Check(task.Phase == PageTaskPhase.Writing && task.StatusText.Contains("任务已失效"), "edit during motion does not pretend to finish");
        task.NoteFinish(Physical());
        Check(task.Phase == PageTaskPhase.Fault && task.CurrentPage == 1 && task.CompletedCount == 0, "invalidated motion does not reach paper change");

        string refusal;
        var empty = new PageTaskService();
        Check(empty.Bind("x", new List<string>(), out refusal) == PageTaskBindResult.Refused, "empty task refused");
        var busy = Arm("PAGE-1", "PAGE-2");
        busy.NoteStarted();
        Check(busy.Bind("other", new[] { "A" }, out refusal) == PageTaskBindResult.Refused, "cannot replace a running task");
        Check(busy.StartRefusal(null) != null, "null editor refused while bound");
        Check(new PageTaskService().StartRefusal("G21") == null, "unbound streaming stays open");

        Console.WriteLine("PASS: " + checks + " page task checks");
    }
}
