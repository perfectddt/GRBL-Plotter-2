/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.

    AutoWriter integration: load a generated writing program into the normal G-code editor so
    the existing preview / simulation / send pipeline can be used. Loading is never a send.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
*/
using System;

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
        private void LoadWritingIntoPreview(string program)
        {
            if (isStreaming)
                throw new InvalidOperationException("正在发送或流式运行，不能载入新的写字程序。请先停止。");
            if (string.IsNullOrWhiteSpace(program))
                throw new InvalidOperationException("没有可载入的写字程序。");

            InsertCodeFromForm(program, "from AutoWriter writing");
        }
    }
}
