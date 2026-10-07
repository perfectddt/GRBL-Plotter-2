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

        /* R6 21:49: '~' resumes leftover planner XY while the pen is still down.
         * Stock GRBL cannot insert a lift in front of a held block. Flush the
         * controller from HOLD (already stopped) so Case A can lift first. */
        public static bool ShouldFlushHoldLeftover(bool settledOnHold, bool penDown)
        { return settledOnHold && penDown; }

        /* After a leftover flush the tool is still at the pause point, but the
         * interrupted G1 would otherwise be skipped (host confirm cursor is
         * ahead of the tool). Resume the first XY segment that still contains
         * the pause point. */
        public static int FindInProgressLine(IList<string> lines, int confirmedCount,
                                             double startX, double startY,
                                             double pauseX, double pauseY)
        {
            if (lines == null || confirmedCount <= 0)
                return 0;
            int n = Math.Min(confirmedCount, lines.Count);
            double x = startX, y = startY;
            int resume = n;
            for (int i = 0; i < n; i++)
            {
                double tx = x, ty = y;
                if (!TryReadXY(lines[i], ref tx, ref ty))
                    continue;
                if (AlmostEqual(pauseX, tx) && AlmostEqual(pauseY, ty))
                {
                    resume = i + 1;
                    x = tx; y = ty;
                    continue;
                }
                if (PointOnSegment(pauseX, pauseY, x, y, tx, ty))
                    return i;
                x = tx; y = ty;
            }
            return Math.Min(resume, n);
        }

        public const double PositionTolMm = 0.5;

        public static bool AlmostEqual(double a, double b)
        { return Math.Abs(a - b) <= PositionTolMm; }

        private static bool TryReadXY(string line, ref double x, ref double y)
        {
            if (string.IsNullOrEmpty(line))
                return false;
            string u = line.ToUpperInvariant();
            bool got = false;
            double xv, yv;
            if (TryReadAxis(u, 'X', out xv)) { x = xv; got = true; }
            if (TryReadAxis(u, 'Y', out yv)) { y = yv; got = true; }
            return got;
        }

        private static bool TryReadAxis(string line, char axis, out double value)
        {
            value = 0;
            int i = line.IndexOf(axis);
            if (i < 0 || i + 1 >= line.Length)
                return false;
            int start = i + 1;
            while (start < line.Length && line[start] == ' ')
                start++;
            int end = start;
            if (end < line.Length && (line[end] == '-' || line[end] == '+'))
                end++;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
                end++;
            if (end <= start)
                return false;
            return double.TryParse(line.Substring(start, end - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        private static bool PointOnSegment(double px, double py,
                                           double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
                return AlmostEqual(px, x0) && AlmostEqual(py, y0);
            double t = ((px - x0) * dx + (py - y0) * dy) / len2;
            if (t < -0.02 || t > 1.02)
                return false;
            double qx = x0 + t * dx, qy = y0 + t * dy;
            double ddx = px - qx, ddy = py - qy;
            return (ddx * ddx + ddy * ddy) <= PositionTolMm * PositionTolMm;
        }

        /* Always invariant: a decimal comma would corrupt the command on any
         * locale that uses one (the old call sites needed Replace(',', '.') for
         * exactly this reason). */
        private static string Fmt(string format, params object[] args)
        { return string.Format(CultureInfo.InvariantCulture, format, args); }
    }
}
