/* The acknowledgement window limits host traffic, not the GRBL planner's motion queue.
 * Feed hold stops motion after deceleration and retains that queue. Ordinary pause may resume;
 * a red stop retains HOLD and forbids resume until a controller welcome confirms a reset.
 */
namespace GrblPlotter.AutoWriter
{
    internal static class StreamThrottle
    {
        // Host sent-but-unacknowledged window; not a distance, time or planner-size bound.
        public const int MaxInFlightLines = 4;

        /* Extra gate for the PreProcessStreaming send loop. The byte-level
         * grblBufferFree check stays; this caps the line-level in-flight
         * count. A negative diff (cursor rewound by candidate J) must always
         * allow sending - those lines still have to reach grbl. */
        public static bool OkToSendMore(int indexSent, int indexConfirmed)
        { return (indexSent - indexConfirmed) < MaxInFlightLines; }

        /* '!' on pause/stop only makes sense while a job is actually moving
         * the machine; when disconnected or already holding/idle the byte
         * would at best litter the log. */
        public static bool ShouldSendFeedHold(bool isConnected, bool machineIsRunning)
        { return isConnected && machineIsRunning; }

        /* Settle-on-hold replaces settle-on-idle exactly once per pause: only
         * while a pause request is pending and the pause has not settled
         * yet. A manual '!' during streaming (no request pending, the R1 raw
         * path) must NOT trigger the software-pause bookkeeping. */
        public static bool ShouldSettleOnHold(bool pauseOrStopRequested, bool alreadyPaused)
        { return pauseOrStopRequested && !alreadyPaused; }

        /* '~' on resume only when the controller is really holding. If the
         * machine drained to IDLE before '!' took effect (very short pause
         * near program end), releasing would be a no-op but confusing log
         * noise - skip it. */
        public static bool ShouldReleaseHold(bool grblStateIsHold)
        { return grblStateIsHold; }
    }

    internal static class StopHoldGuard
    {
        public const string RefusalMessage = "[停止锁存：控制器仍保留运动队列，禁止开始、恢复及发送指令。请手动重置，收到复位响应后重新确认原点再开始；$X 解锁不能清空队列。]";

        public static bool ShouldLatch(bool connected, bool streaming, bool running, bool holding)
        { return connected && (streaming || running || holding); }

        public static bool AllowsLine(string command)
        {
            if (command == null) return false;
            string query = command.Trim().ToUpperInvariant();
            return query == "?" || query == "$G" || query == "$#" || query == "$$" || query == "$I";
        }

        public static bool AllowsRealtime(byte command)
        { return command == (byte)'?' || command == (byte)'!' || command == 0x18 || command == 0x85; }
    }

}
