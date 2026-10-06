/* 2026-10-06 candidate C: prefer pausing at a stroke boundary.
 *
 * Pure boundary-scan logic, free of WinForms / firmware types so
 * scripts/test-stage1-stream-throttle.ps1 can compile it standalone.
 *
 * Combined with candidate E this gives the pause two modes:
 *  - Boundary near (a pen-up Z0 line within the next few unsent lines):
 *    keep feeding up to the boundary, then simply stop feeding and let grbl
 *    drain to IDLE. The machine halts pen-up at a clean stroke boundary -
 *    no feed-hold, no spring-back re-press, no mid-stroke artifact, zero
 *    missing-ink risk. Typical for writing: a boundary occurs every ~3-5
 *    lines, so the extra delay is ~1-3 strokes.
 *  - No boundary near (one long stroke): fall back to candidate E's
 *    immediate '!', accepting the bounded residual the user approved.
 *
 * Generated writing programs only ever toggle Z between 0 (pen up) and 5
 * (pen down), so any Z0 word marks a lift. The scan only inspects lines not
 * yet sent; a Z0 that is already in flight simply makes the scan feed a few
 * extra lines, which is harmless.
 */
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GrblPlotter.AutoWriter
{
    internal static class StrokeBoundary
    {
        /* How far ahead of the send cursor to look for a boundary. 4 matches
         * the throttle window, so the drain-to-boundary mode never pushes more
         * lines into grbl than the immediate-hold mode would leave behind. */
        public const int DefaultLookAhead = 4;

        /* Z0 / Z0.000 / Z 0 with word boundaries. "Z50" or "Z5" must NOT
         * match (first digit after Z must be 0 and the number must end in
         * zeros only). */
        private static readonly Regex PenUpWord = new Regex("\\bZ\\s*0+(\\.0*)?\\b", RegexOptions.Compiled);

        public static bool IsPenUpLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            return PenUpWord.IsMatch(line.ToUpperInvariant());
        }

        /* upcoming[0] is the next line to send. Returns how many lines must
         * still be fed so that the last fed line is a pen-up boundary
         * (>= 1: even if the next line itself is the lift, it still has to
         * reach grbl), or -1 when no boundary exists within maxAhead. */
        public static int LinesUntilBoundary(IList<string> upcoming, int maxAhead)
        {
            if (upcoming == null)
                return -1;
            int n = Math.Min(upcoming.Count, maxAhead);
            for (int i = 0; i < n; i++)
            {
                if (IsPenUpLine(upcoming[i]))
                    return i + 1;
            }
            return -1;
        }
    }
}
