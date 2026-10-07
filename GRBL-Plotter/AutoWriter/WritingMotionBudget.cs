using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GrblPlotter.AutoWriter
{
    // GRBL Bf is FREE planner blocks, not completed lines. ACK only frees RX bytes.
    internal sealed class WritingMotionBudget
    {
        internal const double SliceSeconds = 0.12;
        internal const int MaxBlocks = 4;
        internal const int MaxExpandedLines = 500000;
        private int capacity, credits;
        private bool queryEligible;
        private double seconds;
        internal bool Fault { get; private set; }
        internal void Begin(int plannerCapacity)
        { capacity = plannerCapacity; credits = 0; seconds = 0; queryEligible = false; Fault = capacity < MaxBlocks || capacity > 1024; }
        internal static bool TryPlannerFree(string report, out int free)
        {
            free = -1;
            Match m = Regex.Match(report, @"(?:^|\|)Bf:(\d+),(\d+)(?:\||>)");
            int rx;
            return m.Success && int.TryParse(m.Groups[1].Value, out free) && int.TryParse(m.Groups[2].Value, out rx)
                && free >= 0 && free <= 1024 && rx > 0 && rx <= 65536;
        }
        internal void QuerySent(bool allAcknowledged)
        { queryEligible = allAcknowledged; credits = 0; seconds = 0; }
        internal bool Report(string raw, bool idle, bool run)
        {
            int free;
            if (!TryPlannerFree(raw, out free) || free > capacity || (!idle && !run))
            { Fault = true; credits = 0; return false; }
            if (queryEligible)
            {
                // Include a currently executing/stepper-prepared block even if absent from Bf.
                int occupied = capacity - free + (run ? 1 : 0);
                credits = Math.Max(0, MaxBlocks - occupied);
                seconds = occupied * SliceSeconds;
            }
            queryEligible = false;
            return !Fault;
        }
        internal bool Reserve(int blocks, double nominalSeconds)
        {
            if (Fault || blocks < 0 || blocks > credits || nominalSeconds < 0 || double.IsNaN(nominalSeconds)
                || double.IsInfinity(nominalSeconds) || seconds + nominalSeconds > MaxBlocks * SliceSeconds + 1e-8) return false;
            credits -= blocks; seconds += nominalSeconds;
            return true;
        }
        internal static bool IsPenDown(string line) { return line.Replace(" ", "").ToUpperInvariant().EndsWith("G1Z5F10000"); }
        internal static bool IsMotion(string line) { return line.Replace(" ", "").ToUpperInvariant().Contains("G1"); }
        internal static string PenDownPacket(string z, string xy, string terminator)
        {
            double x, y, feed;
            if (!IsPenDown(z) || !TryXY(xy, out x, out y, out feed)) throw new ArgumentException("Pen-down needs an adjacent XY slice");
            return z + terminator + xy + terminator;
        }
        internal static bool TryXY(string raw, out double x, out double y, out double feed)
        {
            x = y = feed = 0;
            Match m = Regex.Match(raw.Replace(" ", "").ToUpperInvariant(), @"^G1X([-+]?(?:\d+(?:\.\d*)?|\.\d+))Y([-+]?(?:\d+(?:\.\d*)?|\.\d+))F(\d+(?:\.\d*)?)$");
            return m.Success && Finite(m.Groups[1].Value, out x) && Finite(m.Groups[2].Value, out y)
                && Finite(m.Groups[3].Value, out feed) && feed > 0;
        }
        private static bool Finite(string s, out double value)
        { return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value); }
        internal static List<string> Split(string line, ref double previousX, ref double previousY, int available)
        {
            double x, y, feed;
            var result = new List<string>();
            if (available < 1) throw new ArgumentException("Writing motion expansion exceeds safe limit");
            if (!TryXY(line, out x, out y, out feed))
            {
                if (line.Replace(" ", "").StartsWith("G1X", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid XY/feed value");
                result.Add(line); return result;
            }
            double dx = x - previousX, dy = y - previousY;
            double duration = Math.Sqrt(dx * dx + dy * dy) * 60 / feed;
            double requested = Math.Max(1, Math.Ceiling(duration / SliceSeconds));
            if (double.IsInfinity(requested) || double.IsNaN(requested) || requested > available || requested > MaxExpandedLines)
                throw new ArgumentException("Writing motion expansion exceeds safe limit");
            int count = (int)requested;
            for (int i = 1; i <= count; i++)
                result.Add(i == count ? line : string.Format(CultureInfo.InvariantCulture, "G1X{0:0.#########}Y{1:0.#########}F{2}",
                    previousX + dx * i / count, previousY + dy * i / count, feed.ToString("R", CultureInfo.InvariantCulture)));
            previousX = x; previousY = y;
            return result;
        }
    }
}
