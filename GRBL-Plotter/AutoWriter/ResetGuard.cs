using System.Text;

namespace GrblPlotter.AutoWriter
{
    /// <summary>
    /// Candidate G (AutoWriter safety guard): give the operator one chance to back out
    /// before a soft reset ("Reset" / Ctrl-X) is actually sent.
    ///
    /// A soft reset is a hard stop: there is no deceleration ramp, the controller reports the
    /// machine position as lost (ALARM:3) and this machine has no homing ($20/$21/$22 = 0,
    /// and $H is off limits), so the only recovery is a manual re-datum. Measured on
    /// 2026-10-05 (R4): the machine stops within about one second, the pen stays down, and
    /// about 15 bytes of in-flight commands are dropped.
    ///
    /// Only the prompt text lives here; the caller shows the dialog. That keeps this file free
    /// of WinForms references so the offline test harness can compile and assert on it.
    /// </summary>
    internal static class ResetGuard
    {
        internal const string Title = "确认软复位（重置）";

        /// <summary>Text of the confirmation dialog. Never null.</summary>
        /// <param name="whileStreaming">True when a program is currently being sent.</param>
        internal static string BuildMessage(bool whileStreaming)
        {
            var text = new StringBuilder();
            text.Append("「重置」= 软复位 (Ctrl-X)：立即硬停，没有减速过程。\r\n");
            text.Append("\r\n");
            text.Append("· 位置会丢失 (ALARM:3)，本机没有回零、又禁用 $H，只能人工重新对位。\r\n");
            text.Append("· 不会抬笔：复位那一刻笔停在原处，可能压着纸。\r\n");
            text.Append("· 报警态下机器不接受运动指令，要先发 $X 才能再动。\r\n");
            if (whileStreaming)
            {
                text.Append("· 正在发送：程序会被立即中断，不能续画。\r\n");
            }
            text.Append("\r\n");
            text.Append("确定要软复位吗？");
            return text.ToString();
        }
    }
}
