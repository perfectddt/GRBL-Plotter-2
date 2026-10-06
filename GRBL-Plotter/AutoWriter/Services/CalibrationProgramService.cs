using System;
using System.Globalization;
using System.Text;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    /// <summary>
    /// Creates an offline calibration program. The returned text is never sent to a device.
    /// </summary>
    public static class CalibrationProgramService
    {
        public static string GenerateShortLine(MachineProfile profile, double startX, double startY, double lengthMm)
        {
            RequireFinitePositive(lengthMm, "lengthMm");
            ValidateProfile(profile);
            Point start = new Point(startX, startY);
            Point end = new Point(startX + lengthMm, startY);
            ValidatePoint(profile, start, "short line start");
            ValidatePoint(profile, end, "short line end");

            ProgramBuilder program = new ProgramBuilder(profile);
            program.MoveTo(start);
            program.DrawTo(end);
            program.Finish();
            return program.ToString();
        }

        public static string GenerateCross(MachineProfile profile, double centerX, double centerY, double armLengthMm)
        {
            RequireFinitePositive(armLengthMm, "armLengthMm");
            ValidateProfile(profile);
            Point horizontalStart = new Point(centerX - armLengthMm, centerY);
            Point horizontalEnd = new Point(centerX + armLengthMm, centerY);
            Point verticalStart = new Point(centerX, centerY - armLengthMm);
            Point verticalEnd = new Point(centerX, centerY + armLengthMm);
            ValidatePoint(profile, horizontalStart, "cross horizontal start");
            ValidatePoint(profile, horizontalEnd, "cross horizontal end");
            ValidatePoint(profile, verticalStart, "cross vertical start");
            ValidatePoint(profile, verticalEnd, "cross vertical end");

            ProgramBuilder program = new ProgramBuilder(profile);
            program.MoveTo(horizontalStart);
            program.DrawTo(horizontalEnd);
            program.RaisePen();
            program.MoveTo(verticalStart);
            program.DrawTo(verticalEnd);
            program.Finish();
            return program.ToString();
        }

        public static string GenerateSquare10Mm(MachineProfile profile, double minX, double minY)
        {
            return GenerateSquare(profile, minX, minY, 10.0);
        }

        public static string GenerateSquare(MachineProfile profile, double minX, double minY, double sizeMm)
        {
            RequireFinitePositive(sizeMm, "sizeMm");
            ValidateProfile(profile);
            Point lowerLeft = new Point(minX, minY);
            Point lowerRight = new Point(minX + sizeMm, minY);
            Point upperRight = new Point(minX + sizeMm, minY + sizeMm);
            Point upperLeft = new Point(minX, minY + sizeMm);
            ValidatePoint(profile, lowerLeft, "square lower-left");
            ValidatePoint(profile, lowerRight, "square lower-right");
            ValidatePoint(profile, upperRight, "square upper-right");
            ValidatePoint(profile, upperLeft, "square upper-left");

            ProgramBuilder program = new ProgramBuilder(profile);
            program.MoveTo(lowerLeft);
            program.DrawTo(lowerRight);
            program.DrawTo(upperRight);
            program.DrawTo(upperLeft);
            program.DrawTo(lowerLeft);
            program.Finish();
            return program.ToString();
        }

        private static void ValidateProfile(MachineProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");
            profile.Validate();
            if (!String.Equals(profile.VerificationStatus, "Verified", StringComparison.Ordinal))
                throw new InvalidOperationException("A verified machine profile is required before generating calibration G-code.");
            if (!profile.VerifiedAtUtc.HasValue)
                throw new InvalidOperationException("A machine verification date is required before generating calibration G-code.");
            if (profile.VerificationEvidence == null || profile.VerificationEvidence.Count == 0)
                throw new InvalidOperationException("Verification evidence is required before generating calibration G-code.");
            if (profile.AxisMapping == null || !IsWorkFrame(profile.AxisMapping.WorkOrigin))
                throw new InvalidOperationException("A verified work frame G54 through G59 is required before generating calibration G-code.");
            if (!String.Equals(profile.PenControlMode, "Z", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Calibration programs require Z pen control mode.");
            if (!profile.PenUpValue.HasValue || !profile.PenDownValue.HasValue)
                throw new InvalidOperationException("Both Z pen-up and pen-down values must be verified.");
            RequireFinite(profile.PenUpValue.Value, "penUpValue");
            RequireFinite(profile.PenDownValue.Value, "penDownValue");
            if (profile.PenUpValue.Value == profile.PenDownValue.Value)
                throw new InvalidOperationException("Pen-up and pen-down Z values must differ.");

            if (profile.UsableBoundsWorkMm == null)
                throw new InvalidOperationException("Verified XY and Z work bounds are required.");
            RequireBounds(profile.UsableBoundsWorkMm);
            if (profile.Speeds == null || !profile.Speeds.DrawingMmPerMin.HasValue || !profile.Speeds.TravelMmPerMin.HasValue || !profile.Speeds.ZMmPerMin.HasValue)
                throw new InvalidOperationException("Drawing, travel, and Z speeds must all be verified.");
            RequireFinitePositive(profile.Speeds.DrawingMmPerMin.Value, "drawingMmPerMin");
            RequireFinitePositive(profile.Speeds.TravelMmPerMin.Value, "travelMmPerMin");
            RequireFinitePositive(profile.Speeds.ZMmPerMin.Value, "zMmPerMin");
            if (profile.Delays == null || !profile.Delays.PenUpMs.HasValue || !profile.Delays.PenDownMs.HasValue)
                throw new InvalidOperationException("Pen-up and pen-down delays must both be verified.");
            RequireDelay(profile.Delays.PenUpMs.Value, "penUpMs");
            RequireDelay(profile.Delays.PenDownMs.Value, "penDownMs");
            if (profile.Capabilities != null && profile.Capabilities.RequiresContinuousPenMotion == true && profile.Delays.PenDownMs.Value != 0)
                throw new InvalidOperationException("Continuous pen motion requires zero pen-down dwell; queue pen-down and drawing without waiting for Idle.");
            ValidateZ(profile, profile.PenUpValue.Value, "pen-up Z");
            ValidateZ(profile, profile.PenDownValue.Value, "pen-down Z");
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

        private static void RequireBounds(CoordinateBounds bounds)
        {
            RequireFinite(bounds.MinX, "minX");
            RequireFinite(bounds.MaxX, "maxX");
            RequireFinite(bounds.MinY, "minY");
            RequireFinite(bounds.MaxY, "maxY");
            RequireFinite(bounds.MinZ, "minZ");
            RequireFinite(bounds.MaxZ, "maxZ");
            if (!bounds.MinX.HasValue || !bounds.MaxX.HasValue || !bounds.MinY.HasValue || !bounds.MaxY.HasValue || !bounds.MinZ.HasValue || !bounds.MaxZ.HasValue)
                throw new InvalidOperationException("All XY and Z work bounds must be verified.");
            if (bounds.MaxX.Value <= bounds.MinX.Value || bounds.MaxY.Value <= bounds.MinY.Value || bounds.MaxZ.Value <= bounds.MinZ.Value)
                throw new InvalidOperationException("XY and Z work bounds must have positive ranges.");
        }

        private static void ValidatePoint(MachineProfile profile, Point point, string name)
        {
            RequireFinite(point.X, name + " X");
            RequireFinite(point.Y, name + " Y");
            CoordinateBounds bounds = profile.UsableBoundsWorkMm;
            if (point.X < bounds.MinX.Value || point.X > bounds.MaxX.Value || point.Y < bounds.MinY.Value || point.Y > bounds.MaxY.Value)
                throw new ArgumentOutOfRangeException(name, "The calibration path is outside the verified XY bounds.");
        }

        private static void ValidateZ(MachineProfile profile, double z, string name)
        {
            CoordinateBounds bounds = profile.UsableBoundsWorkMm;
            if (z < bounds.MinZ.Value || z > bounds.MaxZ.Value)
                throw new ArgumentOutOfRangeException(name, z, "The pen Z value is outside the verified Z bounds.");
        }

        private static void RequireFinite(double? value, string name)
        {
            if (!value.HasValue) throw new InvalidOperationException(name + " must be known.");
            RequireFinite(value.Value, name);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, name + " must be finite.");
        }

        private static void RequireFinitePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0) throw new ArgumentOutOfRangeException(name, value, name + " must be greater than zero.");
        }

        private static void RequireDelay(int value, string name)
        {
            if (value < 0 || value > 3600000)
                throw new ArgumentOutOfRangeException(name, value, name + " must be between 0 and 3600000 ms.");
        }

        private struct Point
        {
            public readonly double X;
            public readonly double Y;

            public Point(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        private sealed class ProgramBuilder
        {
            private readonly MachineProfile profile;
            private readonly StringBuilder output = new StringBuilder();
            private bool penIsUp;

            public ProgramBuilder(MachineProfile profile)
            {
                this.profile = profile;
                output.AppendLine("G21");
                output.AppendLine("G90");
                output.AppendLine("G94");
                output.AppendLine(profile.AxisMapping.WorkOrigin);
                // Review inherited G92/TLO state before running. This offline program deliberately does not cancel offsets.
                output.AppendLine("; Review inherited G92/TLO state before run; offsets are not cancelled by this program.");
                if (profile.Capabilities != null && profile.Capabilities.RequiresContinuousPenMotion == true)
                    output.AppendLine("; Keep pen-down and drawing queued; do not wait for Idle between blocks.");
                RaisePen();
            }

            public void MoveTo(Point point)
            {
                output.Append("G1 X").Append(Number(point.X)).Append(" Y").Append(Number(point.Y)).Append(" F").Append(Number(profile.Speeds.TravelMmPerMin.Value)).AppendLine();
            }

            public void DrawTo(Point point)
            {
                if (penIsUp) LowerPen();
                output.Append("G1 X").Append(Number(point.X)).Append(" Y").Append(Number(point.Y)).Append(" F").Append(Number(profile.Speeds.DrawingMmPerMin.Value)).AppendLine();
            }

            public void RaisePen()
            {
                output.Append("G1 Z").Append(Number(profile.PenUpValue.Value)).Append(" F").Append(Number(profile.Speeds.ZMmPerMin.Value)).AppendLine();
                if (profile.Delays.PenUpMs.Value > 0)
                    output.Append("G4 P").Append(Seconds(profile.Delays.PenUpMs.Value)).AppendLine();
                penIsUp = true;
            }

            private void LowerPen()
            {
                output.Append("G1 Z").Append(Number(profile.PenDownValue.Value)).Append(" F").Append(Number(profile.Speeds.ZMmPerMin.Value)).AppendLine();
                if (profile.Delays.PenDownMs.Value > 0)
                    output.Append("G4 P").Append(Seconds(profile.Delays.PenDownMs.Value)).AppendLine();
                penIsUp = false;
            }

            public void Finish()
            {
                if (!penIsUp) RaisePen();
            }

            public override string ToString()
            {
                return output.ToString();
            }

            private static string Number(double value)
            {
                return value.ToString("0.######", CultureInfo.InvariantCulture);
            }

            private static string Seconds(int milliseconds)
            {
                return (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }
}
