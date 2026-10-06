using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    /// <summary>
    /// Options that may override machine-profile values for a single writing program.
    /// An override is an explicit operator decision; it is never applied silently.
    /// </summary>
    public sealed class WritingProgramOptions
    {
        /// <summary>
        /// When true, the generator requires a fully verified profile (status, date, evidence,
        /// work frame, bounds and speeds). When false the program is produced as a preview and
        /// is prefixed with a "PREVIEW ONLY" warning comment.
        /// </summary>
        public bool RequireVerifiedRun { get; set; }

        public double? DrawingMmPerMin { get; set; }
        public double? TravelMmPerMin { get; set; }
        public double? ZMmPerMin { get; set; }
        public double? PenUpValue { get; set; }
        public double? PenDownValue { get; set; }

        /// <summary>
        /// When set, the program ends by raising the pen and returning to this point, so the tool
        /// parks at a known place instead of stopping wherever the last stroke finished. The point
        /// is given in the same paper coordinates as the strokes and is transformed by the
        /// profile's axis directions; (0, 0) therefore parks at the work origin the operator set.
        /// Null keeps the historical behaviour (stop at the end of the last stroke).
        /// </summary>
        public WritingPoint? ParkPosition { get; set; }

        /// <summary>
        /// When true, the whole stroke set is translated so that the first pen-down point of the
        /// program sits exactly on the work origin (X0 Y0). The operator then decides where the
        /// character lands by placing the sheet, instead of having to remember a built-in margin
        /// that the author baked into the geometry. Only the set as a whole moves: the shape,
        /// the stroke order and the spacing between strokes are untouched.
        ///
        /// The translation is applied in paper coordinates before the axis directions, so the
        /// first touch is the origin for any xDirection / yDirection. With a mirrored axis the
        /// shape then extends into negative machine coordinates, which is legal and stays subject
        /// to the same work-bounds check as every other point.
        ///
        /// Defaults to false so that the pure transform stays measurable by the offline tests;
        /// every operator-facing entry point (setup dialog, generation scripts) turns it on, and
        /// the setup dialog ships with it checked.
        /// </summary>
        public bool AlignStartToOrigin { get; set; }
    }

    /// <summary>
    /// Generates an ordered writing program from explicit pen-down strokes, using the values
    /// that a machine profile has already verified. This is the formal generation path for
    /// AutoWriter writing tasks.
    ///
    /// Continuous pen motion: when the profile declares
    /// <see cref="MachineCapabilities.RequiresContinuousPenMotion"/>, the generator
    /// - rejects any positive pen dwell,
    /// - never emits a synchronising G4 block,
    /// - writes a pen-down block whose very next code line is the XY drawing move,
    /// so the pen-down and the drawing motion can be queued without an Idle wait.
    ///
    /// Axis direction: stroke points are authored with +X to the right and +Y "up" on the paper.
    /// The profile's <see cref="CoordinateMapping.XDirection"/> and
    /// <see cref="CoordinateMapping.YDirection"/> are applied while the G-code is emitted, so a
    /// machine whose +Y moves opposite to the paper's "up" declares yDirection = -1 and receives
    /// mirrored Y coordinates, while yDirection = +1 passes them through unchanged. A direction
    /// that is null is treated as +1 (no transform) and is surfaced as an undeclared-direction
    /// program comment.
    ///
    /// Start point: when <see cref="WritingProgramOptions.AlignStartToOrigin"/> is set, the stroke
    /// set is translated as a whole so that the program's first pen-down point is the work origin
    /// (X0 Y0). Combined with the end park at the same origin, the program both starts and finishes
    /// at the point the operator defined, which is what makes the start position predictable.
    ///
    /// End park: when <see cref="WritingProgramOptions.ParkPosition"/> is set, the program ends by
    /// raising the pen and returning to that work point (the work origin when it is 0,0), so the
    /// tool parks where the next sheet can be aligned instead of stopping at the last stroke.
    ///
    /// The returned text is never sent to a device by this class.
    /// </summary>
    public static class WritingProgramService
    {
        /// <summary>
        /// Builds the writing program text. Throws when a required value is unknown or when a
        /// value contradicts the verified pen-control behaviour. Unknown values are never
        /// replaced by an assumed default.
        /// </summary>
        public static string Generate(MachineProfile profile, IList<WritingStroke> strokes, WritingProgramOptions options)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (strokes == null) throw new ArgumentNullException("strokes");
            if (options == null) options = new WritingProgramOptions();
            profile.Validate();

            Resolved resolved = Resolve(profile, options);
            // Operator start-point rule: the first pen-down point is the work origin, so the sheet
            // can be positioned by hand instead of the operator guessing where the pen will land.
            // This runs in paper coordinates, before the axis transform, and moves the set as a
            // whole; only then are the declared axis directions applied.
            IList<WritingStroke> preparedStrokes = options.AlignStartToOrigin ? MoveStartToOrigin(strokes) : strokes;
            // Apply the declared paper-to-machine axis directions once; the bounds checks and the
            // emitted code below both work in machine work coordinates.
            IList<WritingStroke> machineStrokes = MapAxes(preparedStrokes, resolved);
            ValidateStrokes(profile, machineStrokes, resolved);
            // The optional end park is expressed in the same paper coordinates as the strokes, so it
            // goes through the same axis transform and the same bounds check.
            WritingPoint? endPark = MapPoint(resolved, options.ParkPosition);
            if (endPark.HasValue)
            {
                RequireFinite(endPark.Value.X, "park X");
                RequireFinite(endPark.Value.Y, "park Y");
                if (resolved.BoundsKnown)
                    ValidateWithinBounds(profile.UsableBoundsWorkMm, endPark.Value, "End park");
            }

            StringBuilder output = new StringBuilder();
            output.AppendLine("G21");
            output.AppendLine("G90");
            output.AppendLine("G94");
            if (resolved.WorkFrame != null)
                output.AppendLine(resolved.WorkFrame);
            // Review inherited G92/TLO state before running. This program deliberately does not cancel offsets.
            output.AppendLine("; Review inherited G92/TLO state before run; offsets are not cancelled by this program.");
            if (resolved.WorkFrame == null)
                output.AppendLine("; Work frame unknown; select and verify G54-G59 before any run.");
            if (resolved.BoundsKnown == false)
                output.AppendLine("; Work bounds unknown; verify XY/Z limits before any run.");
            if (!resolved.DirectionsFullyDeclared)
                output.AppendLine("; Axis direction is not fully declared (xDirection/yDirection); undeclared axes are emitted without a transform.");
            if (resolved.ContinuousPenMotion)
                output.AppendLine("; Continuous pen motion required: keep pen-down and drawing queued; do not wait for Idle between blocks.");
            if (!resolved.VerifiedRun)
                output.AppendLine("; PREVIEW ONLY: machine profile is not verified for production; do not run without on-site validation.");
            if (options.AlignStartToOrigin)
                output.AppendLine("; Start point: the first pen-down point of this program is the work origin (X0 Y0).");
            if (endPark.HasValue)
                output.AppendLine("; End park: the pen is raised and the tool returns to work X" + Number(endPark.Value.X) + " Y" + Number(endPark.Value.Y) + ".");

            ProgramWriter writer = new ProgramWriter(output, resolved);
            foreach (WritingStroke stroke in machineStrokes)
            {
                writer.RaisePen();                                  // lift before every reposition; no-op when already up
                writer.MoveTo(stroke.Points[0]);
                writer.DrawStroke(stroke);
            }
            writer.Finish();
            if (endPark.HasValue)
                writer.ParkAt(endPark.Value);
            return output.ToString();
        }

        /// <summary>
        /// Offline check of the continuous-queue invariant: a program that relies on continuous
        /// pen motion must contain no dwell/stop block, and every pen-down block must be followed
        /// immediately by an XY drawing move. Returns an empty list when the text is clean.
        /// </summary>
        public static IList<string> FindContinuousViolations(string program, double penDownValue)
        {
            List<string> violations = new List<string>();
            if (string.IsNullOrEmpty(program)) return violations;

            string penDownToken = "G1 Z" + Number(penDownValue) + " F";
            string[] lines = program.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> code = new List<string>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("(")) continue;
                code.Add(line);
            }

            for (int i = 0; i < code.Count; i++)
            {
                string line = code[i];
                if (line.StartsWith("G4", StringComparison.Ordinal))
                    violations.Add("Dwell/synchronising block present: " + line);
                if (line.StartsWith("M0", StringComparison.Ordinal))
                    violations.Add("Program stop present: " + line);
                if (line.StartsWith(penDownToken, StringComparison.Ordinal))
                {
                    if (i + 1 >= code.Count || !IsDrawingMove(code[i + 1]))
                        violations.Add("Pen-down block is not immediately followed by an XY drawing move: " + line);
                }
            }
            return violations;
        }

        private static bool IsDrawingMove(string line)
        {
            return line.StartsWith("G1 X", StringComparison.Ordinal) &&
                   line.IndexOf(" Y", StringComparison.Ordinal) >= 0 &&
                   line.IndexOf('Z') < 0;
        }

        private static void ValidateStrokes(MachineProfile profile, IList<WritingStroke> strokes, Resolved resolved)
        {
            if (strokes.Count == 0)
                throw new InvalidOperationException("A writing program requires at least one stroke.");

            int index = 0;
            foreach (WritingStroke stroke in strokes)
            {
                index++;
                if (stroke == null || stroke.Points == null || stroke.Points.Count < 2)
                    throw new InvalidOperationException("Stroke " + index + " has fewer than two points. Single-point strokes are not supported by this build and must not be dropped silently.");
                foreach (WritingPoint point in stroke.Points)
                {
                    RequireFinite(point.X, "stroke " + index + " X");
                    RequireFinite(point.Y, "stroke " + index + " Y");
                    if (resolved.BoundsKnown)
                        ValidateWithinBounds(profile.UsableBoundsWorkMm, point, "Stroke " + index);
                }
            }
        }

        private static void ValidateWithinBounds(CoordinateBounds bounds, WritingPoint point, string what)
        {
            if (point.X < bounds.MinX.Value || point.X > bounds.MaxX.Value ||
                point.Y < bounds.MinY.Value || point.Y > bounds.MaxY.Value)
                throw new ArgumentOutOfRangeException("strokes", what + " leaves the verified XY work bounds.");
        }

        /// <summary>
        /// Applies the profile's declared axis directions to a single point. A null input passes
        /// through, so "no end park requested" stays distinct from "park at the origin".
        /// </summary>
        private static WritingPoint? MapPoint(Resolved resolved, WritingPoint? point)
        {
            if (!point.HasValue) return null;
            return new WritingPoint(point.Value.X * resolved.XDirection, point.Value.Y * resolved.YDirection);
        }

        /// <summary>
        /// Translates the whole stroke set so that the program's first pen-down point becomes the
        /// work origin. The first point of the first stroke is the pen's first touch of the sheet,
        /// so subtracting it puts that touch on (0, 0) and leaves every other point at the same
        /// offset from it: the shape, the stroke order and the inter-stroke spacing do not change.
        ///
        /// Invalid or non-finite input is returned unchanged so that ValidateStrokes reports the
        /// offending stroke by name; a NaN offset would otherwise be spread over every point and
        /// hide which stroke was actually wrong.
        /// </summary>
        private static IList<WritingStroke> MoveStartToOrigin(IList<WritingStroke> strokes)
        {
            if (strokes == null || strokes.Count == 0) return strokes;
            WritingStroke first = strokes[0];
            if (first == null || first.Points == null || first.Points.Count == 0) return strokes;

            double dx = first.Points[0].X;
            double dy = first.Points[0].Y;
            if (double.IsNaN(dx) || double.IsInfinity(dx) || double.IsNaN(dy) || double.IsInfinity(dy))
                return strokes;
            if (dx == 0 && dy == 0) return strokes;      // already on the origin: nothing to move

            List<WritingStroke> moved = new List<WritingStroke>(strokes.Count);
            foreach (WritingStroke stroke in strokes)
            {
                if (stroke == null || stroke.Points == null)
                {
                    moved.Add(stroke);    // preserved so ValidateStrokes reports the defect
                    continue;
                }
                List<WritingPoint> points = new List<WritingPoint>(stroke.Points.Count);
                foreach (WritingPoint point in stroke.Points)
                    points.Add(new WritingPoint(point.X - dx, point.Y - dy));
                moved.Add(new WritingStroke(points));
            }
            return moved;
        }

        /// <summary>
        /// Applies the profile's declared axis directions so the emitted code is expressed in
        /// machine work coordinates. A direction of -1 mirrors that axis about the work origin;
        /// a direction of 1 (or an undeclared/null value, already normalised by Resolve) leaves it
        /// unchanged. This is the single place the paper-to-machine mapping is applied.
        /// </summary>
        private static IList<WritingStroke> MapAxes(IList<WritingStroke> strokes, Resolved resolved)
        {
            if (resolved.XDirection == 1 && resolved.YDirection == 1)
                return strokes;            // no transform: return the caller's list unchanged

            List<WritingStroke> mapped = new List<WritingStroke>(strokes.Count);
            foreach (WritingStroke stroke in strokes)
            {
                if (stroke == null || stroke.Points == null)
                {
                    mapped.Add(stroke);    // preserved so ValidateStrokes reports the defect
                    continue;
                }
                List<WritingPoint> points = new List<WritingPoint>(stroke.Points.Count);
                foreach (WritingPoint point in stroke.Points)
                    points.Add(MapPoint(resolved, point).Value);
                mapped.Add(new WritingStroke(points));
            }
            return mapped;
        }

        /// <summary>
        /// Normalises a declared axis direction. Null means the profile has not declared it; the
        /// generator then emits the axis unchanged and records that fact as a program comment.
        /// </summary>
        private static int AxisDirection(int? declared, string axis)
        {
            if (!declared.HasValue)
                return 1;
            if (declared.Value != 1 && declared.Value != -1)
                throw new InvalidOperationException("axisMapping." + axis + "Direction must be 1, -1, or null.");
            return declared.Value;
        }

        private static Resolved Resolve(MachineProfile profile, WritingProgramOptions options)
        {
            if (!String.Equals(profile.PenControlMode, "Z", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Writing requires a verified Z pen control mode; profile reports '" + (profile.PenControlMode ?? "unknown") + "'.");

            double penUp = ResolveValue(profile.PenUpValue, options.PenUpValue, "pen-up Z");
            double penDown = ResolveValue(profile.PenDownValue, options.PenDownValue, "pen-down Z");
            RequireFinite(penUp, "penUpValue");
            RequireFinite(penDown, "penDownValue");
            if (penUp == penDown)
                throw new InvalidOperationException("Pen-up and pen-down Z values must differ.");

            double drawFeed = ResolveSpeed(profile.Speeds == null ? null : profile.Speeds.DrawingMmPerMin, options.DrawingMmPerMin, "drawing speed");
            double travelFeed = ResolveSpeed(profile.Speeds == null ? null : profile.Speeds.TravelMmPerMin, options.TravelMmPerMin, "travel speed");
            double zFeed = ResolveSpeed(profile.Speeds == null ? null : profile.Speeds.ZMmPerMin, options.ZMmPerMin, "Z speed");

            int penUpDelay = profile.Delays != null && profile.Delays.PenUpMs.HasValue ? profile.Delays.PenUpMs.Value : 0;
            int penDownDelay = profile.Delays != null && profile.Delays.PenDownMs.HasValue ? profile.Delays.PenDownMs.Value : 0;
            bool continuous = profile.Capabilities != null && profile.Capabilities.RequiresContinuousPenMotion == true;
            if (continuous && (penUpDelay != 0 || penDownDelay != 0))
                throw new InvalidOperationException("Continuous pen motion requires zero pen dwell; queue pen-down and drawing without waiting for Idle.");

            string workFrame = profile.AxisMapping == null ? null : profile.AxisMapping.WorkOrigin;
            if (workFrame != null && !IsWorkFrame(workFrame))
                throw new InvalidOperationException("Work frame must be G54 through G59 or null.");

            bool boundsKnown = BoundsAreComplete(profile.UsableBoundsWorkMm);

            CoordinateMapping mapping = profile.AxisMapping;
            int? declaredX = mapping == null ? null : mapping.XDirection;
            int? declaredY = mapping == null ? null : mapping.YDirection;
            int xDirection = AxisDirection(declaredX, "x");
            int yDirection = AxisDirection(declaredY, "y");
            bool directionsFullyDeclared = declaredX.HasValue && declaredY.HasValue;

            if (options.RequireVerifiedRun)
            {
                if (!String.Equals(profile.VerificationStatus, "Verified", StringComparison.Ordinal))
                    throw new InvalidOperationException("A verified machine profile is required before starting a writing run.");
                if (!profile.VerifiedAtUtc.HasValue)
                    throw new InvalidOperationException("A machine verification date is required before starting a writing run.");
                if (profile.VerificationEvidence == null || profile.VerificationEvidence.Count == 0)
                    throw new InvalidOperationException("Verification evidence is required before starting a writing run.");
                if (workFrame == null)
                    throw new InvalidOperationException("A verified work frame G54 through G59 is required before starting a writing run.");
                if (!boundsKnown)
                    throw new InvalidOperationException("Verified XY and Z work bounds are required before starting a writing run.");
            }

            return new Resolved
            {
                PenUp = penUp,
                PenDown = penDown,
                DrawFeed = drawFeed,
                TravelFeed = travelFeed,
                ZFeed = zFeed,
                PenUpDelayMs = penUpDelay,
                PenDownDelayMs = penDownDelay,
                ContinuousPenMotion = continuous,
                WorkFrame = workFrame,
                BoundsKnown = boundsKnown,
                VerifiedRun = options.RequireVerifiedRun,
                XDirection = xDirection,
                YDirection = yDirection,
                DirectionsFullyDeclared = directionsFullyDeclared
            };
        }

        private static double ResolveValue(double? profileValue, double? overrideValue, string name)
        {
            if (overrideValue.HasValue) return overrideValue.Value;   // an explicit override is an operator decision
            if (profileValue.HasValue) return profileValue.Value;
            throw new InvalidOperationException(name + " must be known (set it in the machine profile or provide an explicit override).");
        }

        private static double ResolveSpeed(double? profileValue, double? overrideValue, string name)
        {
            double value = ResolveValue(profileValue, overrideValue, name);
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, value, name + " must be finite and greater than zero.");
            return value;
        }

        private static bool BoundsAreComplete(CoordinateBounds bounds)
        {
            return bounds != null &&
                   bounds.MinX.HasValue && bounds.MaxX.HasValue &&
                   bounds.MinY.HasValue && bounds.MaxY.HasValue &&
                   bounds.MinZ.HasValue && bounds.MaxZ.HasValue;
        }

        private static bool IsWorkFrame(string workOrigin)
        {
            return String.Equals(workOrigin, "G54", StringComparison.Ordinal) ||
                   String.Equals(workOrigin, "G55", StringComparison.Ordinal) ||
                   String.Equals(workOrigin, "G56", StringComparison.Ordinal) ||
                   String.Equals(workOrigin, "G57", StringComparison.Ordinal) ||
                   String.Equals(workOrigin, "G58", StringComparison.Ordinal) ||
                   String.Equals(workOrigin, "G59", StringComparison.Ordinal);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, name + " must be finite.");
        }

        private static string Number(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Seconds(int milliseconds)
        {
            return (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private sealed class Resolved
        {
            public double PenUp;
            public double PenDown;
            public double DrawFeed;
            public double TravelFeed;
            public double ZFeed;
            public int PenUpDelayMs;
            public int PenDownDelayMs;
            public bool ContinuousPenMotion;
            public string WorkFrame;
            public bool BoundsKnown;
            public bool VerifiedRun;
            public int XDirection;
            public int YDirection;
            public bool DirectionsFullyDeclared;
        }

        private sealed class ProgramWriter
        {
            private readonly StringBuilder output;
            private readonly Resolved resolved;
            // The pen may already be down before the program starts; emit a defined raise first.
            private bool penIsDown = true;

            public ProgramWriter(StringBuilder output, Resolved resolved)
            {
                this.output = output;
                this.resolved = resolved;
            }

            public void MoveTo(WritingPoint point)
            {
                output.Append("G1 X").Append(Number(point.X)).Append(" Y").Append(Number(point.Y)).Append(" F").Append(Number(resolved.TravelFeed)).AppendLine();
            }

            public void DrawStroke(WritingStroke stroke)
            {
                LowerPen();
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    output.Append("G1 X").Append(Number(stroke.Points[i].X)).Append(" Y").Append(Number(stroke.Points[i].Y)).Append(" F").Append(Number(resolved.DrawFeed)).AppendLine();
                }
            }

            public void RaisePen()
            {
                if (!penIsDown) return;      // already up: never repeat synchronising raises
                output.Append("G1 Z").Append(Number(resolved.PenUp)).Append(" F").Append(Number(resolved.ZFeed)).AppendLine();
                if (resolved.PenUpDelayMs > 0)
                    output.Append("G4 P").Append(Seconds(resolved.PenUpDelayMs)).AppendLine();
                penIsDown = false;
            }

            private void LowerPen()
            {
                output.Append("G1 Z").Append(Number(resolved.PenDown)).Append(" F").Append(Number(resolved.ZFeed)).AppendLine();
                if (resolved.PenDownDelayMs > 0)
                    output.Append("G4 P").Append(Seconds(resolved.PenDownDelayMs)).AppendLine();
                penIsDown = true;
            }

            public void Finish()
            {
                if (penIsDown) RaisePen();
            }

            /// <summary>
            /// Raises the pen if it is still down and returns to the end-park point at the travel
            /// feed. The pen is never moved in XY while down, so the park cannot draw a stray line.
            /// </summary>
            public void ParkAt(WritingPoint point)
            {
                RaisePen();      // no-op when Finish() already raised it
                output.Append("G1 X").Append(Number(point.X)).Append(" Y").Append(Number(point.Y)).Append(" F").Append(Number(resolved.TravelFeed)).AppendLine();
            }
        }
    }
}
