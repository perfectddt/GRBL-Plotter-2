/* 2026-10-06 candidate K (R6 rerun with pen loaded)
 *
 * Owns the G-code sequence sent when a paused stream is resumed. Pure functions,
 * no WinForms / firmware dependencies, so scripts/test-stage1-resume-restore.ps1
 * can compile this file standalone and assert every branch.
 *
 * The bug this fixes: a software pause always outlasts the pen's spring-back
 * (~0.5 s, machine rule: a standalone Z5 rebounds). The physical pen is then UP
 * while grbl keeps Z=5 on its books. Re-pressing with "G0 Z5" is a zero-length
 * move that grbl drops, so the servo never fires and the first stroke after
 * resume draws air (R6 rerun: top horizontal of 工4 missing right at the resume
 * point; every later stroke was fine because its own Z0->Z5 transition re-fired
 * the servo). The first R6 run only escaped this by luck: its resume happened to
 * land on a Z0 line, whose real transition re-armed the pen.
 *
 * Rules encoded here:
 *  - Pen down at pause (this machine: Z0=up, Z5=down) => force a REAL Z0->Z5
 *    transition so the servo re-fires. Must happen even when XY never moved -
 *    that is the common resume case and it was silently broken before.
 *  - Explicit G90 on the first line of each group: the operator may have left
 *    G91 active by jogging manually during the pause.
 *  - Z moves use "G1 .. F10000", the exact convention of generated programs,
 *    because that is the only Z-motion pattern proven to drive the pen servo.
 *  - Pen up at pause and Z unmoved => emit nothing (a zero-length "G0 Z0" would
 *    be dropped by grbl anyway).
 */
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GrblPlotter.AutoWriter
{
    internal static class ResumeRestore
    {
        /* Machine convention (project memory): pen up = Z0, pen down = Z5.
         * Generated programs only ever use those two values, so any positive
         * pause Z means the pen was down when the stream paused. */
        public static bool IsPenDown(double pauseZ)
        { return pauseZ > 0; }

        /* Build the restore lines in send order.
         * xyMoved / zMoved are computed by the caller with Gcode.IsEqual so the
         * tolerance matches the rest of the streaming code exactly.
         *
         * 2026-10-06 candidate B: the XY restore used to be a G0 rapid issued
         * BEFORE the pen was lifted - with the pen down that dragged ink across
         * the paper (the exact risk R6 was designed to probe). The order is now
         * lift -> travel -> settle -> re-press, and the travel itself is a G1
         * with the machine's proven travel feed (F1800) instead of a G0 rapid,
         * so no pen state ever combines with a rapid move. */
        public static string[] BuildLines(bool xyMoved, bool zMoved,
                                          double pauseX, double pauseY, double pauseZ)
        {
            var lines = new List<string>(5);
            if (IsPenDown(pauseZ) && xyMoved)
            {
                lines.Add("G90 G1 Z0 F10000");              // lift FIRST (real transition: book Z>0)
                lines.Add(Fmt("G90 G1 X{0:0.000} Y{1:0.000} F1800", pauseX, pauseY));   // travel feed
                lines.Add("G4 P0.5");                       // settle before the pen touches paper
                lines.Add(Fmt("G1 Z{0:0.000} F10000", pauseZ));   // re-press LAST (0->Z, real)
            }
            else if (IsPenDown(pauseZ))
            {
                // Spring-back guard: real transitions only, zero-length moves are dropped by grbl.
                lines.Add("G90 G1 Z0 F10000");
                lines.Add(Fmt("G1 Z{0:0.000} F10000", pauseZ));
            }
            else
            {
                if (xyMoved)
                {
                    lines.Add(Fmt("G90 G1 X{0:0.000} Y{1:0.000} F1800", pauseX, pauseY));
                    lines.Add("G4 P0.5");                   // settle, matches upstream behaviour
                }
                else if (zMoved)
                {
                    lines.Add(Fmt("G1 Z{0:0.000} F10000", pauseZ));
                }
            }
            return lines.ToArray();
        }

        /* Always invariant: a decimal comma would corrupt the command on any
         * locale that uses one (the old call sites needed Replace(',', '.') for
         * exactly this reason). */
        private static string Fmt(string format, params object[] args)
        { return string.Format(CultureInfo.InvariantCulture, format, args); }
    }
}
