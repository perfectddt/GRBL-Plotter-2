using System;
using System.Collections.Generic;
using System.Text;

namespace GrblPlotter.AutoWriter.Services
{
    internal enum PageTaskPhase
    {
        None,
        WaitingStart,
        Writing,
        Paused,
        WaitingPaper,
        Completed,
        Cancelled,
        Fault,
        Unknown
    }

    internal enum PageTaskBindResult
    {
        Created,
        AlreadyArmed,
        Refused
    }

    /// <summary>
    /// Evidence captured when a writing page's sender believes the job ended.
    /// Percent complete and the last ok are recorded so callers can see them, and neither one
    /// is enough to call the page physically finished.
    /// </summary>
    internal sealed class PageCompletionWitness
    {
        public bool AllSent;
        public bool AllAcknowledged;
        public bool PenRaised;
        public bool Connected;
        public bool IdleBarrier;
        public bool ReportedPercent;
        public bool ReportedLastOk;

        public bool IsPhysicalComplete
        {
            get { return AllSent && AllAcknowledged && PenRaised && Connected && IdleBarrier; }
        }

        public static PageCompletionWitness FromController(bool allSent, bool allAcknowledged, bool penRaised, bool connected, bool idleBarrier)
        {
            return new PageCompletionWitness
            {
                AllSent = allSent,
                AllAcknowledged = allAcknowledged,
                PenRaised = penRaised,
                Connected = connected,
                IdleBarrier = idleBarrier
            };
        }
    }

    /// <summary>
    /// Frozen multi-page writing task. It never sends, jogs, or sets a work zero.
    /// The main form feeds it the existing stream events; paper confirmation only releases the next frozen page.
    /// </summary>
    internal sealed class PageTaskService
    {
        private readonly List<string> retiredPrograms = new List<string>();
        private List<string> pages;
        private string fingerprint = "";
        private string reasonText = "";
        private bool retired;
        private bool started;
        private bool allSent;
        private bool allAcknowledged;
        private int loadedPage;
        private DateTime? writingSince;
        private DateTime? pausedSince;
        internal Func<DateTime> Clock = () => DateTime.UtcNow;
        internal int SentLines;
        internal int AcknowledgedLines;
        internal int TotalLines;
        internal double RunSeconds;
        internal double PauseSeconds;
        internal int ReliablePage;
        internal bool CheckpointPositionKnown;
        internal readonly List<string> LogEntries = new List<string>();

        internal event Action Changed;

        internal PageTaskPhase Phase { get; private set; }
        internal int CurrentPage { get; private set; }
        internal int PageCount { get; private set; }
        internal int CompletedCount { get; private set; }
        internal int LoadedPage { get { return loadedPage; } }
        internal string Fingerprint { get { return fingerprint; } }
        internal bool IsActive
        {
            get
            {
                return Phase == PageTaskPhase.WaitingStart || Phase == PageTaskPhase.Writing
                    || Phase == PageTaskPhase.Paused || Phase == PageTaskPhase.WaitingPaper;
            }
        }
        internal bool BlocksManualCommand
        {
            get { return Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused || Phase == PageTaskPhase.WaitingPaper; }
        }
        internal string ProgressText
        {
            get
            {
                if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused && Phase != PageTaskPhase.WaitingPaper && Phase != PageTaskPhase.Completed)
                    return "";
                string lines = TotalLines > 0 ? "已发送 " + SentLines + "/" + TotalLines + " 行，已应答 " + AcknowledgedLines + " 行。" : "";
                return lines + "运行 " + (int)RunSeconds + " 秒，暂停 " + (int)PauseSeconds + " 秒。发送进度不是已写完的字数；只有抬笔并确认空闲才计入完成页。";
            }
        }
        internal string CheckpointText
        {
            get
            {
                if (Phase == PageTaskPhase.Unknown || Phase == PageTaskPhase.Fault || Phase == PageTaskPhase.Cancelled || !CheckpointPositionKnown)
                    return "位置未知或本页尚未确认完成，不会自动续写。最近可靠检查点：第 " + ReliablePage + " 页。";
                return "可靠检查点：第 " + ReliablePage + " 页已抬笔并确认空闲。下一页仍要人工换纸并手动开始，不会自动续写。";
            }
        }
        internal bool PaperButtonEnabled
        {
            get
            {
                if (retired || pages == null) return false;
                if (Phase == PageTaskPhase.WaitingPaper) return true;
                return Phase == PageTaskPhase.WaitingStart && CurrentPage > 1 && loadedPage != CurrentPage;
            }
        }

        internal string StatusText
        {
            get
            {
                if (Phase == PageTaskPhase.None) return "写字任务：未绑定";
                string page = "第 " + CurrentPage + "/" + PageCount + " 页";
                string done = CompletedCount > 0 ? "（已完成 " + CompletedCount + " 页）" : "";
                string detail = "";
                if (Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused)
                {
                    if (allAcknowledged) detail = " · 已应答，等待空闲";
                    else if (allSent) detail = " · 已发送，等待应答";
                    else detail = " · 发送中";
                }
                string reason = reasonText.Length == 0 ? "" : " " + reasonText;
                switch (Phase)
                {
                    case PageTaskPhase.WaitingStart: return page + " · 等待开始" + done;
                    case PageTaskPhase.Writing: return page + " · 书写中" + detail + (retired ? " · 任务已失效，完成后不换纸" : "");
                    case PageTaskPhase.Paused: return page + " · 暂停" + detail + (retired ? " · 任务已失效" : "");
                    case PageTaskPhase.WaitingPaper: return page + " · 等待换纸" + done;
                    case PageTaskPhase.Completed: return page + " · 全部完成" + done;
                    case PageTaskPhase.Cancelled: return page + " · 取消" + reason;
                    case PageTaskPhase.Fault: return page + " · 异常" + reason;
                    case PageTaskPhase.Unknown: return page + " · 未知" + reason;
                    default: return "写字任务：未绑定";
                }
            }
        }

        internal PageTaskBindResult Bind(string nextFingerprint, IList<string> programs, out string refusal)
        {
            refusal = null;
            if (programs == null || programs.Count < 1)
            {
                refusal = "没有可冻结的页面。";
                return PageTaskBindResult.Refused;
            }
            for (int i = 0; i < programs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(programs[i]))
                {
                    refusal = "第 " + (i + 1) + " 页程序是空的，不能冻结。";
                    return PageTaskBindResult.Refused;
                }
            }
            if (Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused)
            {
                refusal = "书写或暂停尚未结束，不能更换冻结任务。";
                return PageTaskBindResult.Refused;
            }
            string key = nextFingerprint ?? "";
            if (Phase == PageTaskPhase.WaitingStart && !started && !retired && key == fingerprint && pages != null && pages.Count == programs.Count)
                return PageTaskBindResult.AlreadyArmed;

            pages = new List<string>(programs);
            fingerprint = key;
            for (int i = 0; i < pages.Count; i++) retiredPrograms.Remove(Canon(pages[i]));
            CurrentPage = 1;
            PageCount = pages.Count;
            CompletedCount = 0;
            loadedPage = 0;
            started = false;
            retired = false;
            allSent = false;
            allAcknowledged = false;
            reasonText = "";
            Phase = PageTaskPhase.WaitingStart;
            ResetProgress();
            AddLog("冻结 " + PageCount + " 页，等待手动开始。");
            Raise();
            return PageTaskBindResult.Created;
        }

        internal string PeekUnloadedProgram()
        {
            if (Phase != PageTaskPhase.WaitingStart || retired || pages == null) return null;
            if (CurrentPage < 1 || CurrentPage > pages.Count) return null;
            if (loadedPage == CurrentPage) return null;
            return pages[CurrentPage - 1];
        }

        internal void NoteLoaded()
        {
            if (Phase == PageTaskPhase.WaitingStart && pages != null && CurrentPage >= 1 && CurrentPage <= pages.Count && loadedPage != CurrentPage)
            {
                loadedPage = CurrentPage;
                Raise();
            }
        }

        internal bool ConfirmPaper()
        {
            if (Phase != PageTaskPhase.WaitingPaper || retired || pages == null) return false;
            if (CurrentPage >= pages.Count) return false;
            CurrentPage++;
            Phase = PageTaskPhase.WaitingStart;
            started = false;
            allSent = false;
            allAcknowledged = false;
            loadedPage = 0;
            reasonText = "";
            Raise();
            return true;
        }

        internal void Invalidate(string reason)
        {
            if (Phase == PageTaskPhase.None || Phase == PageTaskPhase.Fault || Phase == PageTaskPhase.Completed
                || Phase == PageTaskPhase.Cancelled || Phase == PageTaskPhase.Unknown) return;
            if (retired && (Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused)) return;
            reasonText = string.IsNullOrEmpty(reason) ? "冻结内容已变化。" : reason;
            if (Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused)
            {
                retired = true;
                Raise();
                return;
            }
            RememberRetired();
            Phase = PageTaskPhase.Fault;
            retired = true;
            ClearPages();
            Raise();
        }

        internal string StartRefusal(string editorText)
        {
            string editor = Canon(editorText);
            if (IsActive)
            {
                if (Phase == PageTaskPhase.WaitingPaper)
                    return "第 " + CurrentPage + "/" + PageCount + " 页已写完。请先换纸并点击“已换纸并确认位置”。确认前不能开始下一页，也不会重发本页。";
                if (Phase == PageTaskPhase.Writing || Phase == PageTaskPhase.Paused)
                    return "第 " + CurrentPage + "/" + PageCount + " 页仍在任务中，不会重复发送。";
                if (loadedPage != CurrentPage)
                    return "第 " + CurrentPage + " 页还没有载入编辑器。";
                if (started)
                    return "第 " + CurrentPage + " 页已经开始过，不会重复发送。";
                if (pages == null || editor != Canon(pages[CurrentPage - 1]))
                    return "编辑器内容不是冻结的第 " + CurrentPage + " 页，已阻止发送。";
                return null;
            }
            if (editor.Length > 0 && retiredPrograms.Contains(editor))
                return "编辑器里仍是已失效或未确认完成的写字页，不能再发送。请重新冻结多页任务或载入其他程序。";
            return null;
        }

        internal bool NoteStarted()
        {
            if (Phase != PageTaskPhase.WaitingStart || retired || started || loadedPage != CurrentPage) return false;
            Phase = PageTaskPhase.Writing;
            started = true;
            allSent = false;
            allAcknowledged = false;
            writingSince = Clock();
            AddLog("第 " + CurrentPage + " 页开始书写。");
            Raise();
            return true;
        }

        internal void NotePaused()
        {
            if (Phase != PageTaskPhase.Writing) return;
            Accumulate();
            Phase = PageTaskPhase.Paused;
            pausedSince = Clock();
            AddLog("第 " + CurrentPage + " 页暂停。不进入换纸。");
            Raise();
        }

        internal void NoteResumed()
        {
            if (Phase != PageTaskPhase.Paused) return;
            Accumulate();
            Phase = PageTaskPhase.Writing;
            writingSince = Clock();
            AddLog("第 " + CurrentPage + " 页继续。");
            Raise();
        }

        internal bool NoteDispatch(bool sent, bool acknowledged)
        {
            if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused) return false;
            bool ack = acknowledged && sent;
            if (sent == allSent && ack == allAcknowledged) return false;
            allSent = sent;
            allAcknowledged = ack;
            AddLog("第 " + CurrentPage + " 页" + (ack ? "已应答，等待空闲。" : "已发送，等待应答。") + "这不是写完的字数。");
            Raise();
            return true;
        }

        internal void NoteFinish(PageCompletionWitness witness)
        {
            if (Phase != PageTaskPhase.Writing) return;
            Accumulate();
            if (retired || witness == null || !witness.IsPhysicalComplete)
            {
                CheckpointPositionKnown = false;
                AddLog("第 " + CurrentPage + " 页结束信号不可靠，不推进检查点。");
                RememberRetired();
                if (retired)
                {
                    Phase = PageTaskPhase.Fault;
                    if (reasonText.Length == 0) reasonText = "冻结任务已失效，不进入换纸。";
                }
                else
                {
                    Phase = PageTaskPhase.Unknown;
                    reasonText = "结束信号不是物理完成：需要全部发送、全部应答、收尾抬笔、有效连接和空闲屏障。不进入换纸。";
                }
                ClearPages();
                Raise();
                return;
            }
            if (pages != null && CurrentPage >= 1 && CurrentPage <= pages.Count)
                Retire(pages[CurrentPage - 1]);
            CompletedCount = CurrentPage;
            allSent = true;
            allAcknowledged = true;
            started = false;
            ReliablePage = CurrentPage;
            CheckpointPositionKnown = true;
            AddLog("第 " + CurrentPage + " 页已抬笔并确认空闲，记为可靠检查点。不会自动续写。");
            Phase = CurrentPage >= PageCount ? PageTaskPhase.Completed : PageTaskPhase.WaitingPaper;
            reasonText = "";
            Raise();
        }

        internal void NoteStop(bool motionUnknown)
        {
            if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused) return;
            Accumulate();
            CheckpointPositionKnown = false;
            AddLog("第 " + CurrentPage + " 页停止。不推进检查点，不会自动续写。");
            RememberRetired();
            if (motionUnknown)
            {
                Phase = PageTaskPhase.Unknown;
                reasonText = "已停止，运动结果未知，请人工处理。页码不推进。";
            }
            else
            {
                Phase = PageTaskPhase.Cancelled;
                reasonText = "已停止，本页未完成。页码不推进。";
            }
            ClearPages();
            Raise();
        }

        internal void NoteAlarm()
        {
            EndUnproven("报警，运动结果未知，请人工处理。页码不推进。");
        }

        internal void NoteDisconnect()
        {
            EndUnproven("连接断开，运动结果未知，请人工处理。页码不推进。");
        }

        internal void NoteCommandError()
        {
            if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused) return;
            Accumulate();
            CheckpointPositionKnown = false;
            AddLog("第 " + CurrentPage + " 页命令错误。不推进检查点。");
            RememberRetired();
            Phase = PageTaskPhase.Fault;
            reasonText = "命令错误，本页不标为完成。页码不推进。";
            ClearPages();
            Raise();
        }

        private void EndUnproven(string reason)
        {
            if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused) return;
            Accumulate();
            CheckpointPositionKnown = false;
            AddLog(reason + " 不推进检查点，不会自动续写。");
            RememberRetired();
            Phase = PageTaskPhase.Unknown;
            reasonText = reason;
            ClearPages();
            Raise();
        }

        private void RememberRetired()
        {
            if (pages == null) return;
            for (int i = 0; i < pages.Count; i++) Retire(pages[i]);
        }

        private void Retire(string program)
        {
            string key = Canon(program);
            if (key.Length > 0 && !retiredPrograms.Contains(key)) retiredPrograms.Add(key);
        }

        private void ClearPages()
        {
            pages = null;
            loadedPage = 0;
            retired = true;
        }

        internal void NoteLines(int sent, int acknowledged, int total)
        {
            if (Phase != PageTaskPhase.Writing && Phase != PageTaskPhase.Paused) return;
            SentLines = Math.Max(0, sent);
            AcknowledgedLines = Math.Max(0, acknowledged);
            TotalLines = Math.Max(0, total);
            NoteTick();
        }

        internal void NoteTick()
        {
            DateTime now = Clock();
            if (Phase == PageTaskPhase.Writing && writingSince.HasValue)
            {
                RunSeconds += (now - writingSince.Value).TotalSeconds;
                writingSince = now;
            }
            else if (Phase == PageTaskPhase.Paused && pausedSince.HasValue)
            {
                PauseSeconds += (now - pausedSince.Value).TotalSeconds;
                pausedSince = now;
            }
        }

        private void Accumulate()
        {
            DateTime now = Clock();
            if (writingSince.HasValue) { RunSeconds += (now - writingSince.Value).TotalSeconds; writingSince = null; }
            if (pausedSince.HasValue) { PauseSeconds += (now - pausedSince.Value).TotalSeconds; pausedSince = null; }
        }

        private void ResetProgress()
        {
            SentLines = AcknowledgedLines = TotalLines = 0;
            RunSeconds = PauseSeconds = 0;
            writingSince = pausedSince = null;
            ReliablePage = 0;
            CheckpointPositionKnown = false;
            LogEntries.Clear();
        }

        private void AddLog(string message)
        {
            LogEntries.Add(Clock().ToString("HH:mm:ss") + " " + message);
            if (LogEntries.Count > 100) LogEntries.RemoveAt(0);
        }

        private void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        internal static string Canon(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(line);
            }
            return builder.ToString();
        }
    }
}
