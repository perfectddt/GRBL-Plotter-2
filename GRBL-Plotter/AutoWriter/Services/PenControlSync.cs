using System;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.UserControls;

namespace GrblPlotter.AutoWriter.Services
{
    // Immutable per-import overrides. Never changes device settings or the tool table.
    public static class PenControlSync
    {
        internal sealed class ImportContext
        {
            internal readonly float Up, Down, ZFeed, DrawFeed;
            internal ImportContext(float up, float down, float zFeed, float drawFeed)
            { Up = up; Down = down; ZFeed = zFeed; DrawFeed = drawFeed; }
        }
        [ThreadStatic] private static ImportContext current;
        internal static ImportContext Current { get { return current; } }

        public static IDisposable Begin(MachineProfile profile, WritingProgramOptions options)
        {
            if (profile == null || !string.Equals(profile.PenControlMode, "Z", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("图形导入需要已确认的 Z 轴笔控配置，请先打开机器配置。");
            return Enter(new ImportContext(
                Value(options == null ? null : options.PenUpValue, profile.PenUpValue, "抬笔值", false),
                Value(options == null ? null : options.PenDownValue, profile.PenDownValue, "落笔值", false),
                Value(options == null ? null : options.ZMmPerMin, profile.Speeds == null ? null : profile.Speeds.ZMmPerMin, "Z速度", true),
                Value(options == null ? null : options.DrawingMmPerMin, profile.Speeds == null ? null : profile.Speeds.DrawingMmPerMin, "绘图速度", true)));
        }
        internal static IDisposable Enter(ImportContext context) { return new Scope(context); }
        private sealed class Scope : IDisposable
        {
            private readonly ImportContext previous;
            private bool disposed;
            internal Scope(ImportContext context) { previous = current; current = context; }
            public void Dispose() { if (!disposed) { current = previous; disposed = true; } }
        }
        private static float Value(double? explicitValue, double? configuredValue, string name, bool positive)
        {
            double? value = explicitValue ?? configuredValue;
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
                Math.Abs(value.Value) > float.MaxValue || (positive && value.Value <= 0))
                throw new InvalidOperationException(name + "未知或无效，请在机器配置或显式覆盖中设置。");
            return (float)value.Value;
        }
        internal static void ApplyToGeneratedCode()
        {
            ImportContext context = current;
            if (context == null) return;
            Gcode.OptionZAxis.Enable = true;
            Gcode.OptionZAxis.Up = context.Up;
            Gcode.OptionZAxis.Down = context.Down;
            Gcode.OptionZAxis.Feed = context.ZFeed;
            Gcode.GcodeXYFeed = context.DrawFeed;
        }
    }
}
