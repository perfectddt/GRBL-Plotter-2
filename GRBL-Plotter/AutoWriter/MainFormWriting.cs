/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.

    AutoWriter integration: load a generated writing program into the normal G-code editor so
    the existing preview / simulation / send pipeline can be used. Loading is never a send.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
*/
using GrblPlotter.AutoWriter.Services;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace GrblPlotter
{
    public partial class MainForm
    {
        /// <summary>
        /// Sink used by the AutoWriter setup dialog to load a generated writing program into the
        /// main G-code editor (fCTBCode) through the same <see cref="InsertCodeFromForm"/> path
        /// that the other import forms use.
        ///
        /// It deliberately does NOT send anything and refuses while a stream is active. Programs
        /// produced from an unverified machine profile already contain a "PREVIEW ONLY" comment,
        /// so the operator still has to confirm machine state before running.
        /// </summary>
        private string boundWritingProgram = "";
        private bool boundWritingStale;
        private void LoadWritingIntoPreview(string program)
        {
            if (isStreaming)
                throw new InvalidOperationException("正在发送或流式运行，不能载入新的写字程序。请先停止。");
            if (string.IsNullOrWhiteSpace(program))
                throw new InvalidOperationException("没有可载入的写字程序。");

            InsertCodeFromForm(program, "from AutoWriter writing");
            boundWritingProgram = program;
            boundWritingStale = false;
        }
        private void MarkWritingPreviewStale()
        {
            if (string.IsNullOrEmpty(boundWritingProgram)) return;
            boundWritingStale = true;
            SetInfoLabel("设置已变化。主界面里刚才载入的写字代码已过期，开始前请重新生成并载入。其他程序不受影响。", Color.Fuchsia);
        }

        /// <summary>
        /// Replace the editor with one frozen page. This is not a send and does not merge into
        /// whatever program was already open.
        /// </summary>
        private void LoadBoundWritingPage(string program)
        {
            if (isStreaming)
                throw new InvalidOperationException("正在发送或流式运行，不能载入下一页。请先停止。");
            if (string.IsNullOrWhiteSpace(program))
                throw new InvalidOperationException("没有可载入的写字页。");
            boundWritingProgram = program;
            boundWritingStale = false;
            NewCodeStart(false);
            SetFctbCodeText(program);
            NewCodeEnd();
        }

        private void ConfirmWritingPaperChange()
        {
            try
            {
                if (writingPageTask.Phase == PageTaskPhase.WaitingPaper && isStreaming)
                    throw new InvalidOperationException("仍在发送，不能确认换纸。");
                if (writingPageTask.Phase == PageTaskPhase.WaitingPaper)
                {
                    if (!writingPageTask.ConfirmPaper())
                    {
                        RefreshWritingPageBar();
                        return;
                    }
                }
                string program = writingPageTask.PeekUnloadedProgram();
                if (program == null)
                {
                    RefreshWritingPageBar();
                    SetInfoLabel(writingPageTask.StatusText, Color.Yellow);
                    return;
                }
                LoadBoundWritingPage(program);
                writingPageTask.NoteLoaded();
                RefreshWritingPageBar();
                SetInfoLabel(writingPageTask.StatusText + " 已载入，请手动开始。不会自动回零或发送。", Color.Yellow);
            }
            catch (Exception error)
            {
                SetInfoLabel(error.Message, Color.Fuchsia);
            }
        }

        private void RefreshWritingPageBar()
        {
            if (writingPageStatus == null || writingPageConfirm == null) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshWritingPageBar);
                return;
            }
            string progress = writingPageTask.ProgressText;
            string checkpoint = writingPageTask.ReliablePage > 0 || writingPageTask.Phase == PageTaskPhase.Unknown || writingPageTask.Phase == PageTaskPhase.Fault || writingPageTask.Phase == PageTaskPhase.Cancelled
                ? " " + writingPageTask.CheckpointText : "";
            writingPageStatus.Text = writingPageTask.StatusText + (progress.Length == 0 ? "" : " " + progress) + checkpoint;
            writingPageConfirm.Enabled = writingPageTask.PaperButtonEnabled;
            switch (writingPageTask.Phase)
            {
                case PageTaskPhase.WaitingPaper: writingPageBar.BackColor = Color.Orange; break;
                case PageTaskPhase.Writing: writingPageBar.BackColor = Color.PaleGreen; break;
                case PageTaskPhase.Paused: writingPageBar.BackColor = Color.Khaki; break;
                case PageTaskPhase.Completed: writingPageBar.BackColor = Color.LightGreen; break;
                case PageTaskPhase.Fault:
                case PageTaskPhase.Unknown:
                case PageTaskPhase.Cancelled: writingPageBar.BackColor = Color.MistyRose; break;
                case PageTaskPhase.WaitingStart: writingPageBar.BackColor = Color.LightYellow; break;
                default: writingPageBar.BackColor = SystemColors.Control; break;
            }
        }

        private void NoteWritingDispatchProgress()
        {
            if (_serial_form == null) return;
            bool sent, acknowledged;
            _serial_form.ReadWritingPageDispatch(out sent, out acknowledged);
            int sentLines, acknowledgedLines, totalLines;
            _serial_form.ReadWritingPageLines(out sentLines, out acknowledgedLines, out totalLines);
            writingPageTask.NoteLines(sentLines, acknowledgedLines, totalLines);
            if (writingPageTask.NoteDispatch(sent, acknowledged)) RefreshWritingPageBar();
            else RefreshWritingPageBar();
        }

        private void NoteWritingPageFinish()
        {
            if (_serial_form == null) return;
            PageCompletionWitness witness = _serial_form.WritingPageWitness;
            _serial_form.ClearWritingPageWitness();
            writingPageTask.NoteFinish(witness);
            RefreshWritingPageBar();
        }
    }
}
