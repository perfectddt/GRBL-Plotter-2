using System.Globalization;
using System.Text;

namespace GrblPlotter.AutoWriter
{
    /// <summary>
    /// AutoWriter: the "置零" (software re-datum) action for the COM CNC window.
    ///
    /// What the operator asked for on 2026-10-06, verbatim: 「软件里变零，图示里也要回原点，
    /// 物理不用动」. That rules out '$H': this machine has $20/$21/$22 = 0 and no limit switches,
    /// so a physical home cycle cannot run at all. It also rules out pretending a manual move
    /// happened - nothing is commanded, no axis turns, the pen stays exactly where it is.
    ///
    /// What it does instead. GRBL keeps machine position (MPos) and a work-coordinate offset
    /// (WCO) apart, and ControlSerialFormInterface.ProcessGrblRealTimeStatus() recomputes
    ///     posWork = posMachine - posWCO
    /// on every status report. So issuing "G10 L20 P0 X0 Y0 Z0" declares "wherever the head is
    /// standing right now is work zero" by changing only the offset. MPos is untouched, the
    /// steppers never move, and every subsequent read of posWork reports the new zero - which is
    /// also what the 2D view draws, because OnRaisePosEvent() invalidates pictureBox1 whenever
    /// posWork moves. The picture therefore follows the firmware with no extra bookkeeping here.
    ///
    /// Why G10 L20 and not G92. G92 offsets the *current* coordinate system and is cleared by
    /// G92.1; G10 L20 writes the work offset explicitly and is what MainForm already uses for
    /// zeroCmd ("G10 L20 P0", GUI/MainForm.cs:111). Marlin does not accept G10 L20, so the caller
    /// substitutes G92 for it, exactly like MainFormInterface.cs:438 does.
    ///
    /// Kept free of WinForms types (like ResetGuard and AlarmUnlock) so the offline harness can
    /// compile and assert on it; the control layer lives in ControlSerialForm.
    /// </summary>
    internal static class SoftwareDatum
    {
        /// <summary>GRBL command for Marlin, which has no G10 L20.</summary>
        internal const string MarlinCommand = "G92";

        /// <summary>GRBL command for everything else: set the work offset of G54.</summary>
        internal const string GrblCommand = "G10 L20 P0";

        internal const string Title = "软件置零（当前位置 → 原点）";

        /// <summary>Log line written just before the command goes out.</summary>
        internal const string LogSending = "▶ 发送软件置零指令：当前位置被设为工件原点（电机不动、不回零）…";

        /// <summary>
        /// Log line written after the controller accepts the offset. <paramref name="isMarlin"/>
        /// only changes the wording of the command that was used, for a truthful log.
        /// </summary>
        internal static string LogDone(bool isMarlin)
        {
            return "✔ 软件置零完成：" + ZeroCommand(isMarlin) +
                   " 已生效，工件坐标 (0,0,0) = 当前位置；2D 图上的当前点已同步到原点。" +
                   "注意：这只改了软件坐标，机器没有回零，物理基准仍需你自己保证。";
        }

        /// <summary>
        /// Build the command for the given controller. Axes are written explicitly so the intent is
        /// visible in the log and in the G-code view - an empty "G10 L20 P0" would leave the
        /// omitted axis untouched, which is a trap when the operator assumes Z follows too.
        /// </summary>
        /// <param name="isMarlin">True for Marlin, which needs G92 instead of G10 L20.</param>
        /// <param name="zeroX">Include X in the offset.</param>
        /// <param name="zeroY">Include Y.</param>
        /// <param name="zeroZ">Include Z.</param>
        /// <param name="zeroA">Include A, when a tangential/4th axis is enabled.</param>
        /// <param name="hasA">True when axis A exists.</param>
        internal static string BuildCommand(bool isMarlin, bool zeroX, bool zeroY, bool zeroZ, bool hasA, bool zeroA)
        {
            var sb = new StringBuilder(isMarlin ? MarlinCommand : GrblCommand);
            if (zeroX) sb.Append(" X0");
            if (zeroY) sb.Append(" Y0");
            if (zeroZ) sb.Append(" Z0");
            if (hasA && zeroA) sb.Append(" A0");
            return sb.ToString();
        }

        /// <summary>
        /// Text of the confirmation dialog. Never null.
        /// </summary>
        /// <param name="inAlarm">True when the controller currently reports ALARM.</param>
        /// <param name="streaming">True when a program is currently being sent.</param>
        /// <param name="position">Human-readable current work position, e.g. "9.875, -10.000, 5.000".</param>
        /// <param name="hasA">True when axis A exists, so the dialog can mention it.</param>
        /// <param name="isMarlin">True for Marlin, so the dialog names the real command.</param>
        internal static string BuildMessage(bool inAlarm, bool streaming, string position, bool hasA, bool isMarlin)
        {
            var text = new StringBuilder();
            text.Append("把「当前位置」直接定义为工件原点：\r\n");
            text.Append("\r\n");
            text.Append("    当前工件坐标  X/Y/Z = ").Append(position ?? "?").Append("\r\n");
            text.Append("    置零后        X/Y/Z = 0 / 0 / 0");
            if (hasA) text.Append("    (A 轴不动)");
            text.Append("\r\n");
            text.Append("    将发送：").Append(ZeroCommand(isMarlin)).Append("\r\n");
            text.Append("\r\n");
            text.Append("· 只改软件坐标：电机不转、笔不动，不是物理回零。\r\n");
            text.Append("· 这不是 $H 回零：本机没有回零开关，$H 用不了，对位由你手工完成。\r\n");
            text.Append("· 2D 图上的当前点会跟着回到原点，图上看到的仍是同一段图形。\r\n");
            text.Append("· 先手动把笔移到你要当原点的位置（例如台面左前角），再点确定。\r\n");
            text.Append("· 位置基准由你负责确认，机器无法自行验证对位是否正确。\r\n");
            if (inAlarm)
            {
                text.Append("\r\n");
                text.Append("⚠ 当前处于报警态：GRBL 不接受任何指令，请先点「解锁」。\r\n");
            }
            if (streaming)
            {
                text.Append("\r\n");
                text.Append("⚠ 正在发送程序：坐标会中途改变，剩余路径将整体平移。\r\n");
            }
            text.Append("\r\n");
            text.Append("确定要置零吗？");
            return text.ToString();
        }

        /// <summary>The command text as shown to the operator, e.g. "G10 L20 P0 X0 Y0 Z0".</summary>
        internal static string ZeroCommand(bool isMarlin)
        {
            return BuildCommand(isMarlin, true, true, true, false, false);
        }

        /// <summary>
        /// True when the zeroing may proceed.
        /// </summary>
        /// <param name="connected">A GRBL connection is open.</param>
        /// <param name="isIdle">Controller reports IDLE.</param>
        /// <param name="inAlarm">Controller reports ALARM.</param>
        /// <param name="hasAxis">True when at least one axis is selected - "G10 L20 P0" alone would
        /// be accepted by GRBL but does nothing, which is a confusing no-op.</param>
        /// <remarks>
        /// Alarm is checked separately from Idle because the firmware reports Alarm *instead of*
        /// Idle, so an alarm controller is not "idle". G10 L20 is a non-motion command and GRBL
        /// would technically parse it, but honouring it while locked would silently desync the
        /// offset from the still-locked motion state.
        /// </remarks>
        internal static bool CanExecute(bool connected, bool isIdle, bool inAlarm, bool hasAxis)
        {
            return connected && isIdle && !inAlarm && hasAxis;
        }

        /// <summary>Reason shown when <see cref="CanExecute"/> refuses. Never null.</summary>
        internal static string RefusalReason(bool connected, bool inAlarm, bool hasAxis)
        {
            if (!connected) return "尚未连接 GRBL，无法置零。";
            if (inAlarm) return "控制器处于报警态：GRBL 不接受任何指令，请先点「解锁」。";
            if (!hasAxis) return "没有选择任何轴，无法置零。";
            return "";
        }

        /// <summary>Format a work position for the dialog, invariant culture, 3 decimals.</summary>
        internal static string FormatPosition(double x, double y, double z)
        {
            return x.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   y.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
                   z.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}