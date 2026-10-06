using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    /// <summary>One line of the apply report: a value that would be applied, and whether it is verified.</summary>
    public sealed class ApplyItem
    {
        public ApplyItem(string group, string label, string value, bool known)
            : this(group, label, value, known, false)
        { }

        public ApplyItem(string group, string label, string value, bool known, bool overridden)
        {
            Group = group;
            Label = label;
            Value = value;
            Known = known;
            Overridden = overridden;
        }

        public string Group { get; private set; }
        public string Label { get; private set; }
        public string Value { get; private set; }
        public bool Known { get; private set; }

        /// <summary>True when this value comes from an explicit operator override rather than the profile.</summary>
        public bool Overridden { get; private set; }
    }

    /// <summary>
    /// A stable, language-neutral reason that prevents preview/start. The <see cref="Code"/> is
    /// ASCII so callers and offline tests can match on it; <see cref="Text"/> is for the UI.
    /// </summary>
    public sealed class ApplyBlocker
    {
        public ApplyBlocker(string code, string text)
        {
            Code = code;
            Text = text;
        }

        public string Code { get; private set; }
        public string Text { get; private set; }
    }

    /// <summary>
    /// Explicit "apply machine profile" summary. It shows the serial, pen-control and coordinate
    /// values that a writing task would use and marks each one verified or unknown. It never
    /// invents a value: an unknown stays unknown and, where it matters, becomes a blocker.
    /// </summary>
    public sealed class MachineApplyReport
    {
        internal MachineApplyReport()
        {
            Items = new List<ApplyItem>();
            Blockers = new List<ApplyBlocker>();
        }

        public string DisplayName { get; internal set; }
        public IList<ApplyItem> Items { get; private set; }
        public IList<ApplyBlocker> Blockers { get; private set; }
        public bool PenValuesKnown { get; internal set; }
        public bool SpeedsKnown { get; internal set; }
        public bool WorkFrameKnown { get; internal set; }
        public bool BoundsKnown { get; internal set; }
        public bool IsVerified { get; internal set; }

        /// <summary>True when a preview program can be produced from the known pen values and speeds.</summary>
        public bool CanGeneratePreview { get; internal set; }

        /// <summary>True when the profile is fully verified for a production writing run.</summary>
        public bool CanStartWriting { get; internal set; }

        /// <summary>True when a blocker with the given stable code is present.</summary>
        public bool HasBlocker(string code)
        {
            foreach (ApplyBlocker blocker in Blockers)
                if (String.Equals(blocker.Code, code, StringComparison.Ordinal)) return true;
            return false;
        }

        public string ToText()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("配置：" + DisplayName);
            string group = null;
            foreach (ApplyItem item in Items)
            {
                if (item.Group != group)
                {
                    group = item.Group;
                    text.AppendLine("【" + group + "】");
                }
                text.AppendLine("  " + item.Label + "：" + item.Value +
                    (item.Known ? (item.Overridden ? "（显式覆盖）" : "") : "（未知）"));
            }
            text.AppendLine();
            text.AppendLine(CanStartWriting
                ? "结论：配置已通过验证，可生成并开始书写（仍需现场环境确认）。"
                : CanGeneratePreview
                    ? "结论：可生成预览程序，但不能开始书写；未知/未验证项见下方阻塞。"
                    : "结论：关键笔控值不齐，暂不能生成写字程序。");
            if (Blockers.Count > 0)
            {
                text.AppendLine("阻塞项：");
                foreach (ApplyBlocker blocker in Blockers) text.AppendLine("  - " + blocker.Text);
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Builds the explicit apply report. Unknown profile values are surfaced as unknown and are
    /// never filled in with assumed or screenshot values.
    /// </summary>
    public static class MachineProfileApplier
    {
        public static MachineApplyReport BuildReport(MachineProfile profile)
        {
            return BuildReport(profile, null);
        }

        /// <summary>
        /// Builds the report using explicit operator overrides where supplied. An override is an
        /// operator decision for the current task and is marked "（显式覆盖）"; it never rewrites the
        /// profile, and a profile value that is unknown stays unknown when no override is given.
        /// </summary>
        public static MachineApplyReport BuildReport(MachineProfile profile, WritingProgramOptions overrides)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            profile.Validate();

            MachineApplyReport report = new MachineApplyReport { DisplayName = profile.DisplayName };
            CoordinateMapping mapping = profile.AxisMapping;
            CoordinateBounds bounds = profile.UsableBoundsWorkMm;
            MachineSpeeds speeds = profile.Speeds;
            MachineDelays delays = profile.Delays;
            MachineCapabilities capabilities = profile.Capabilities;

            // Serial
            Add(report, "串口", "候选协议", profile.ProtocolCandidate);
            Add(report, "串口", "候选波特率", profile.BaudRateCandidate.HasValue ? profile.BaudRateCandidate.Value.ToString(CultureInfo.InvariantCulture) : null);
            Add(report, "串口", "最近端口", profile.LastPort);
            Add(report, "串口", "固件标识", profile.FirmwareIdentity);

            // Pen control
            bool penUpOverridden, penDownOverridden, drawOverridden, travelOverridden, zOverridden;
            double? penUp = Pick(overrides == null ? null : overrides.PenUpValue, profile.PenUpValue, out penUpOverridden);
            double? penDown = Pick(overrides == null ? null : overrides.PenDownValue, profile.PenDownValue, out penDownOverridden);
            double? drawFeed = Pick(overrides == null ? null : overrides.DrawingMmPerMin, speeds == null ? null : speeds.DrawingMmPerMin, out drawOverridden);
            double? travelFeed = Pick(overrides == null ? null : overrides.TravelMmPerMin, speeds == null ? null : speeds.TravelMmPerMin, out travelOverridden);
            double? zFeed = Pick(overrides == null ? null : overrides.ZMmPerMin, speeds == null ? null : speeds.ZMmPerMin, out zOverridden);

            Add(report, "笔控", "笔控方式（Z/PWM）", profile.PenControlMode);
            Add(report, "笔控", "抬笔值（Z mm）", Number(penUp), penUpOverridden);
            Add(report, "笔控", "落笔值（Z mm）", Number(penDown), penDownOverridden);
            Add(report, "笔控", "绘图速度（mm/min）", Number(drawFeed), drawOverridden);
            Add(report, "笔控", "空移速度（mm/min）", Number(travelFeed), travelOverridden);
            Add(report, "笔控", "Z 速度（mm/min）", Number(zFeed), zOverridden);
            Add(report, "笔控", "抬笔后等待（ms）", Number(delays == null ? null : delays.PenUpMs));
            Add(report, "笔控", "落笔后等待（ms）", Number(delays == null ? null : delays.PenDownMs));
            Add(report, "笔控", "落笔保持需连续运动", Flag(capabilities == null ? null : capabilities.RequiresContinuousPenMotion));

            // Coordinates
            Add(report, "坐标", "纸面 X 对应机器轴", mapping == null ? null : mapping.XAxis);
            Add(report, "坐标", "纸面 Y 对应机器轴", mapping == null ? null : mapping.YAxis);
            Add(report, "坐标", "笔控对应机器轴", mapping == null ? null : mapping.ZAxis);
            Add(report, "坐标", "X 方向（1/-1）", mapping == null ? null : Direction(mapping.XDirection));
            Add(report, "坐标", "Y 方向（1/-1）", mapping == null ? null : Direction(mapping.YDirection));
            Add(report, "坐标", "Z 方向（1/-1）", mapping == null ? null : Direction(mapping.ZDirection));
            Add(report, "坐标", "工作坐标系", mapping == null ? null : mapping.WorkOrigin);
            Add(report, "坐标", "可用工作范围 X", BoundsText(bounds, bounds == null ? null : bounds.MinX, bounds == null ? null : bounds.MaxX));
            Add(report, "坐标", "可用工作范围 Y", BoundsText(bounds, bounds == null ? null : bounds.MinY, bounds == null ? null : bounds.MaxY));
            Add(report, "坐标", "可用工作范围 Z", BoundsText(bounds, bounds == null ? null : bounds.MinZ, bounds == null ? null : bounds.MaxZ));

            // Verification
            Add(report, "验证", "验证状态", profile.VerificationStatus);
            Add(report, "验证", "验证时间（UTC）", profile.VerifiedAtUtc.HasValue ? profile.VerifiedAtUtc.Value.ToString("u", CultureInfo.InvariantCulture) : null);
            Add(report, "验证", "证据条数", profile.VerificationEvidence == null ? null : profile.VerificationEvidence.Count.ToString(CultureInfo.InvariantCulture));

            report.PenValuesKnown = String.Equals(profile.PenControlMode, "Z", StringComparison.OrdinalIgnoreCase) &&
                                    penUp.HasValue && penDown.HasValue && penUp.Value != penDown.Value;
            report.SpeedsKnown = IsPositive(drawFeed) && IsPositive(travelFeed) && IsPositive(zFeed);
            report.WorkFrameKnown = mapping != null && IsWorkFrame(mapping.WorkOrigin);
            report.BoundsKnown = BoundsComplete(bounds);
            report.IsVerified = String.Equals(profile.VerificationStatus, "Verified", StringComparison.Ordinal) &&
                                 profile.VerifiedAtUtc.HasValue &&
                                 profile.VerificationEvidence != null && profile.VerificationEvidence.Count > 0;
            report.CanGeneratePreview = report.PenValuesKnown && report.SpeedsKnown;

            bool continuous = capabilities != null && capabilities.RequiresContinuousPenMotion == true;
            int penUpDelay = delays != null && delays.PenUpMs.HasValue ? delays.PenUpMs.Value : 0;
            int penDownDelay = delays != null && delays.PenDownMs.HasValue ? delays.PenDownMs.Value : 0;
            bool dwellOk = !continuous || (penUpDelay == 0 && penDownDelay == 0);

            report.CanStartWriting = report.CanGeneratePreview && report.IsVerified && report.WorkFrameKnown && report.BoundsKnown && dwellOk;

            if (!report.PenValuesKnown) report.Blockers.Add(new ApplyBlocker("pen-unknown", "笔控方式或抬笔/落笔值未知或相同：先实测确认 Z 抬落笔值。"));
            if (!report.SpeedsKnown) report.Blockers.Add(new ApplyBlocker("speeds-unknown", "绘图/空移/Z 速度不齐：读写速度需先确认或显式给定。"));
            if (!report.WorkFrameKnown) report.Blockers.Add(new ApplyBlocker("workframe-unknown", "工作坐标系未知：需确认 G54-G59。"));
            if (!report.BoundsKnown) report.Blockers.Add(new ApplyBlocker("bounds-unknown", "可用工作范围未知：需实测 X/Y/Z 行程。"));
            if (!report.IsVerified) report.Blockers.Add(new ApplyBlocker("unverified", "配置未验证：可生成预览，但不能开始书写。"));
            if (!dwellOk) report.Blockers.Add(new ApplyBlocker("dwell", "连续笔控要求落笔/抬笔等待为 0，当前不为 0。"));

            return report;
        }

        private static void Add(MachineApplyReport report, string group, string label, string value)
        {
            Add(report, group, label, value, false);
        }

        private static void Add(MachineApplyReport report, string group, string label, string value, bool overridden)
        {
            bool known = !string.IsNullOrWhiteSpace(value);
            report.Items.Add(new ApplyItem(group, label, known ? value : "—", known, known && overridden));
        }

        /// <summary>Override wins over the profile value; reports which source was used.</summary>
        private static double? Pick(double? overrideValue, double? profileValue, out bool overridden)
        {
            if (overrideValue.HasValue) { overridden = true; return overrideValue; }
            overridden = false;
            return profileValue;
        }

        private static bool IsPositive(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value > 0;
        }

        private static bool BoundsComplete(CoordinateBounds bounds)
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

        private static string Number(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : null;
        }

        private static string Number(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string Direction(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string Flag(bool? value)
        {
            if (!value.HasValue) return null;
            return value.Value ? "是" : "否";
        }

        private static string BoundsText(CoordinateBounds bounds, double? min, double? max)
        {
            if (bounds == null || !min.HasValue || !max.HasValue) return null;
            return min.Value.ToString("0.###", CultureInfo.InvariantCulture) + " … " + max.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
