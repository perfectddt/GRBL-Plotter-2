/* 2026-10-06 candidates E + F (user-approved design: throttle N=4 + feed-hold '!')
 *
 * Pure decision logic for the streaming flow-control changes, kept free of
 * WinForms / firmware types so scripts/test-stage1-stream-throttle.ps1 can
 * compile this file standalone and assert every rule.
 *
 * Background: grbl's RX buffer (127 bytes) plus planner (~16 blocks) used to
 * hold ~24 short writing lines, so the software pause needed ~10 s to drain
 * and the stop point was uncontrollable (R2). Simply injecting '!' stops the
 * machine instantly BUT leaves the queued pen-down strokes in the planner:
 * after any pause longer than the servo's ~0.5 s spring-back those queued
 * strokes would execute with the pen physically up on resume - a new missing
 * -ink segment of up to ~2 characters that candidate K cannot fix (its
 * re-press lines can only queue behind the planner content).
 *
 * The approved compromise caps the in-flight (sent-but-unconfirmed) line
 * count, so the residual queue is tiny:
 *  - E (pause): send '!' immediately; with <= MaxInFlightLines lines in
 *    flight the machine halts almost at once and the post-resume air-draw
 *    risk is bounded to <= ~4 short strokes (<= ~2 cm), which the user
 *    accepted. Resume releases the hold with '~' before the restore lines.
 *  - F (stop): send '!' on stop; once the hold settles the stop finalizes
 *    and '~' releases the hold so the <= residual lines run out and the
 *    machine returns to IDLE with position intact (no lingering HOLD state,
 *    no reset, no next-start guard needed).
 *  - Pause/stop settling must accept GrblState.hold (not only idle/check),
 *    because after '!' the controller never reaches IDLE by itself.
 */
namespace GrblPlotter.AutoWriter
{
    internal static class StreamThrottle
    {
        /* Max lines allowed to be sent-but-unconfirmed at any moment.
         * 4 lines ~= <= 60 bytes, safely below the 127-byte RX buffer, and
         * ~= <= 2 cm of writing at the usual 5 mm stroke pitch: the accepted
         * upper bound for the resume air-draw residual after a hold. */
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
         * while a pause/stop request is pending and the pause has not settled
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
}
