/* 2026-10-06 candidate D (R6 follow-up): block manual motion while a stream is paused.
 *
 * Owns the decision whether a hand-typed COM CNC command may leave the PC
 * while a software pause has settled. Pure functions, no WinForms / firmware
 * dependencies, so scripts/test-stage1-stream-throttle.ps1 compiles this file
 * standalone alongside StreamThrottle.cs.
 *
 * Why: R6 showed the risky path - the operator typed "G91 G0 X5 Y3" during a
 * pause and the resume had to fire [Restore Position], a G0 rapid back to the
 * pause point with the pen possibly down. Re-datum commands (G10/G92) during
 * a pause are worse: they silently change the coordinate frame the restore
 * logic relies on. Queries and state commands stay allowed: $G / $$ / $# /
 * $X (unlock must remain reachable), M-codes, G4, G90/G91 mode words.
 *
 * Rules:
 *  - "$J=..." jog commands          -> blocked (explicit jog).
 *  - other "$..." commands          -> allowed (queries / settings / unlock).
 *  - any command carrying an axis word (X/Y/Z/A/B/C followed by a number)
 *                                     -> blocked (this catches G0/G1 moves,
 *                                        G10 L20 datum, G92 offsets).
 *  - everything else (M5, G4, G90)  -> allowed.
 */
using System.Text.RegularExpressions;

namespace GrblPlotter.AutoWriter
{
    internal static class PauseMoveGuard
    {
        public const string RefusalMessage = "[Blocked while paused: resume or stop before moving / re-datum]";

        private static readonly Regex AxisWord = new Regex("[XYZABC]\\s*-?\\d", RegexOptions.Compiled);

        public static bool IsMotionCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;
            string cmd = command.Trim().ToUpperInvariant();
            if (cmd.StartsWith("$J="))
                return true;
            if (cmd.StartsWith("$"))
                return false;
            return AxisWord.IsMatch(cmd);
        }

        /* The guard only applies while a pause has settled (isStreamingPause);
         * during active streaming the command box is already disabled upstream,
         * and outside streaming manual motion is the whole point of the box. */
        public static bool ShouldBlock(bool isStreamingPause, string command)
        { return isStreamingPause && IsMotionCommand(command); }
    }
}
