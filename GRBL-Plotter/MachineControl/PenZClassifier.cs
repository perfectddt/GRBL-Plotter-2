using System;

namespace GrblPlotter
{
    /// <summary>
    /// Pen up / down detection from Z moves for machines that press the pen down at a *positive*
    /// Z value. The AutoWriter profile writes Z0 to lift the pen and Z5 to press it down, and the
    /// original machine software writes the same values with G1 for every move.
    ///
    /// Why this exists: GRBL-Plotter's built-in rule (MachineControl/GCode2DViewpaths.cs) only
    /// recognises pen-down as "Z below zero", plus the G0/G1 motion change and the "(PU"/"(PD"
    /// markers. A program that is all G1 and uses Z0/Z5 matches none of them, so the whole file -
    /// travels included - was drawn as pen-down and the "Pen-Up path" toggle had nothing to show.
    ///
    /// The rule is deliberately transition based: pen down exactly when Z steps from the pen-up
    /// value to the pen-down value, and up on the way back. A file that never touches Z (a plain
    /// 2D job) or keeps Z at a single value is therefore left exactly as it was before.
    /// </summary>
    public static class PenZClassifier
    {
        // Z values arrive as text parsed to double and are also stored in JSON, so exact equality
        // is not reliable; a tolerance is used instead.
        private const double Tolerance = 1e-6;

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= Tolerance;
        }

        /// <summary>True when this move is the pen going down (pen-up Z to pen-down Z).</summary>
        public static bool IsPenDownMove(double oldZ, double newZ, bool enabled, double penUpZ, double penDownZ)
        {
            if (!enabled) return false;
            if (Near(penUpZ, penDownZ)) return false;
            return Near(oldZ, penUpZ) && Near(newZ, penDownZ);
        }

        /// <summary>True when this move is the pen coming up (pen-down Z to pen-up Z).</summary>
        public static bool IsPenUpMove(double oldZ, double newZ, bool enabled, double penUpZ, double penDownZ)
        {
            if (!enabled) return false;
            if (Near(penUpZ, penDownZ)) return false;
            return Near(oldZ, penDownZ) && Near(newZ, penUpZ);
        }
    }
}
