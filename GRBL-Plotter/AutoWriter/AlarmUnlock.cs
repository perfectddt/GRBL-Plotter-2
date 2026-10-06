using System.Text;

namespace GrblPlotter.AutoWriter
{
    /// <summary>
    /// AutoWriter: the "解锁" (unlock) action for the COM CNC window.
    ///
    /// Why this exists. On 2026-10-06 the operator hit "重置" (soft reset / Ctrl-X) while the
    /// machine was still moving, so GRBL answered ALARM:3 "Reset while in motion" and locked every
    /// G-code and '$' command. Recovering took three separate button presses that were easy to
    /// get wrong:
    ///   1. '$X'          - unlock the controller (works, because RequestSend writes it straight to
    ///                      the port and drops whatever the alarm period left in the send queue),
    ///   2. 「检查GRBL」   - re-read the settings that candidate I-a deliberately skipped,
    ///   3. hope the operator guessed step 1 and not step 2.
    ///
    /// This type holds the *decision* for that button so it can be asserted offline. It has no
    /// WinForms dependency, matching ResetGuard, because the control layer lives in the partial
    /// ControlSerialForm files.
    ///
    /// What the button actually does, in order:
    ///   - sends '$X' (candidate I-a: bypasses the queue and clears the wedge),
    ///   - shows a warning that unlocking does NOT restore the known position - ALARM:3 means the
    ///     steppers were killed mid-move, and this machine cannot home ($22 = 0, '$H' off limits),
    ///     so a manual re-datum is still mandatory,
    ///   - arms a deferred settings read that fires automatically once the controller reports IDLE,
    ///     so the operator does not have to remember step 2.
    /// </summary>
    internal static class AlarmUnlock
    {
        /// <summary>The GRBL command that clears the alarm lock.</summary>
        internal const string UnlockCommand = "$X";

        internal const string Title = "解除报警（Unlock）";

        /// <summary>Log line written when the operator triggers the unlock.</summary>
        internal const string LogSending = "▶ 发送 $X 解除报警锁…（不解锁也能发，但报警态下队列发不出去）";

        /// <summary>
        /// Text of the confirmation dialog. Never null.
        /// </summary>
        /// <param name="inAlarm">True when the controller currently reports ALARM.</param>
        /// <param name="streaming">True when a program is currently being sent.</param>
        internal static string BuildMessage(bool inAlarm, bool streaming)
        {
            var text = new StringBuilder();
            text.Append("发送 $X 解除报警锁，让控制器回到 Idle。\r\n");
            text.Append("\r\n");
            if (inAlarm)
            {
                text.Append("· 报警态下 GRBL 不接受运动指令，$X 是官方解锁命令。\r\n");
                text.Append("· 会顺带清空报警期间积压的发送队列，并自动重新读取设置。\r\n");
            }
            else
            {
                text.Append("· 当前不在报警态，发送 $X 基本没有副作用。\r\n");
            }
            text.Append("\r\n");
            text.Append("⚠ 解锁 ≠ 位置已知：\r\n");
            text.Append("· 若刚发生 ALARM:3（运动中复位），位置很可能已丢失。\r\n");
            text.Append("· 本机没有回零、又禁用 $H，解锁后第一次动之前必须人工重新对位。\r\n");
            if (streaming)
            {
                text.Append("· 正在发送：解锁不会自动续画，程序仍需重新开始。\r\n");
            }
            text.Append("\r\n");
            text.Append("确定要解锁吗？");
            return text.ToString();
        }

        /// <summary>
        /// Log line written when the deferred settings read fires after the unlock. Keeps the
        /// operator informed that step 2 happened by itself, and repeats the re-datum warning
        /// because this is the first idle moment after an alarm.
        /// </summary>
        internal const string LogSettingsRecovered =
            "✔ $X 已生效，控制器回到 Idle，已自动重新读取设置（无需再点「检查GRBL」）。" +
            "若此前发生过 ALARM:3，动笔前请先人工重新对位。";

        /// <summary>
        /// True when the deferred settings read should fire.
        /// </summary>
        /// <param name="pending">A previous unlock armed the deferred read.</param>
        /// <param name="isIdle">The controller currently reports IDLE.</param>
        /// <param name="connected">A GRBL connection is open.</param>
        /// <remarks>
        /// Deliberately requires <paramref name="isIdle"/>: ProcessSend() only lets a '$' line out
        /// of the queue in IDLE or CHECK, so firing any earlier would re-wedge the queue we just
        /// cleared - the exact failure candidate I-a fixed.
        /// </remarks>
        internal static bool ShouldReadSettings(bool pending, bool isIdle, bool connected)
        {
            return pending && isIdle && connected;
        }
    }
}