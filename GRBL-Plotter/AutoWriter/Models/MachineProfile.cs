using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;

namespace GrblPlotter.AutoWriter.Models
{
    public static class MachineProfileSchema
    {
        public const int CurrentVersion = 1;
    }

    [DataContract]
    public sealed class MachineProfile
    {
        [DataMember(Name = "schemaVersion", Order = 1)]
        [DisplayName("配置格式版本")] public int SchemaVersion { get; set; }

        [DataMember(Name = "profileId", Order = 2)]
        [DisplayName("配置编号")] public string ProfileId { get; set; }

        [DataMember(Name = "displayName", Order = 3)]
        [DisplayName("机器名称")] public string DisplayName { get; set; }

        [DataMember(Name = "firmwareIdentity", Order = 4, EmitDefaultValue = false)]
        [DisplayName("固件原始标识")] public string FirmwareIdentity { get; set; }

        [DataMember(Name = "protocolCandidate", Order = 5, EmitDefaultValue = false)]
        [DisplayName("候选协议")] public string ProtocolCandidate { get; set; }

        [DataMember(Name = "baudRateCandidate", Order = 6, EmitDefaultValue = false)]
        [DisplayName("候选波特率")] public int? BaudRateCandidate { get; set; }

        [DataMember(Name = "lastPort", Order = 7, EmitDefaultValue = false)]
        [DisplayName("最近端口")] public string LastPort { get; set; }

        [DataMember(Name = "penControlMode", Order = 8, EmitDefaultValue = false)]
        [DisplayName("笔控方式（Z/PWM）")] public string PenControlMode { get; set; }

        [DataMember(Name = "penUpValue", Order = 9, EmitDefaultValue = false)]
        [DisplayName("抬笔值（Z用mm）")] public double? PenUpValue { get; set; }

        [DataMember(Name = "penDownValue", Order = 10, EmitDefaultValue = false)]
        [DisplayName("落笔值（Z用mm）")] public double? PenDownValue { get; set; }

        [DataMember(Name = "axisMapping", Order = 11, EmitDefaultValue = false)]
        [DisplayName("坐标映射")] public CoordinateMapping AxisMapping { get; set; }

        [DataMember(Name = "usableBoundsWorkMm", Order = 12, EmitDefaultValue = false)]
        [DisplayName("可用工作范围（mm）")] public CoordinateBounds UsableBoundsWorkMm { get; set; }

        [DataMember(Name = "speeds", Order = 13, EmitDefaultValue = false)]
        [DisplayName("速度（mm/min）")] public MachineSpeeds Speeds { get; set; }

        [DataMember(Name = "delays", Order = 14, EmitDefaultValue = false)]
        [DisplayName("抬落笔延迟（ms）")] public MachineDelays Delays { get; set; }

        [DataMember(Name = "capabilities", Order = 15, EmitDefaultValue = false)]
        [DisplayName("已验证控制能力")] public MachineCapabilities Capabilities { get; set; }

        [DataMember(Name = "verificationStatus", Order = 16)]
        [DisplayName("验证状态（Unverified/Verified）")] public string VerificationStatus { get; set; }

        [DataMember(Name = "verifiedAtUtc", Order = 17, EmitDefaultValue = false)]
        [DisplayName("验证时间（UTC）")] public DateTime? VerifiedAtUtc { get; set; }

        [DataMember(Name = "verificationEvidence", Order = 18, EmitDefaultValue = false)]
        [DisplayName("验证证据")] public List<string> VerificationEvidence { get; set; }

        public MachineProfile()
        {
            SchemaVersion = MachineProfileSchema.CurrentVersion;
            VerificationStatus = "Unverified";
        }

        public void Validate()
        {
            if (SchemaVersion != MachineProfileSchema.CurrentVersion)
                throw new MachineProfileValidationException("Unsupported machine profile schema version: " + SchemaVersion + ".");
            RequireText(ProfileId, "profileId", 128);
            RequireText(DisplayName, "displayName", 256);
            OptionalText(FirmwareIdentity, "firmwareIdentity", 512);
            OptionalText(ProtocolCandidate, "protocolCandidate", 64);
            OptionalText(LastPort, "lastPort", 256);
            OptionalText(PenControlMode, "penControlMode", 64);
            OptionalPositiveInt(BaudRateCandidate, "baudRateCandidate");
            OptionalFinite(PenUpValue, "penUpValue");
            OptionalFinite(PenDownValue, "penDownValue");

            if (AxisMapping != null) AxisMapping.Validate();
            if (UsableBoundsWorkMm != null) UsableBoundsWorkMm.Validate();
            if (Speeds != null) Speeds.Validate();
            if (Delays != null) Delays.Validate();
            if (Capabilities != null) Capabilities.Validate();

            OptionalText(VerificationStatus, "verificationStatus", 64);
            if (VerifiedAtUtc.HasValue && (VerifiedAtUtc.Value < DateTime.MinValue || VerifiedAtUtc.Value > DateTime.MaxValue))
                throw new MachineProfileValidationException("verifiedAtUtc is outside the supported date range.");
            if (VerificationEvidence != null)
            {
                foreach (string evidence in VerificationEvidence)
                    RequireText(evidence, "verificationEvidence item", 2048);
            }
        }

        internal static void RequireText(string value, string name, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new MachineProfileValidationException(name + " is required.");
            if (value.Length > maxLength)
                throw new MachineProfileValidationException(name + " exceeds " + maxLength + " characters.");
        }

        internal static void OptionalText(string value, string name, int maxLength)
        {
            if (value != null)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new MachineProfileValidationException(name + " must be null or non-empty.");
                if (value.Length > maxLength)
                    throw new MachineProfileValidationException(name + " exceeds " + maxLength + " characters.");
            }
        }

        internal static void OptionalFinite(double? value, string name)
        {
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
                throw new MachineProfileValidationException(name + " must be finite.");
        }

        internal static void OptionalPositiveInt(int? value, string name)
        {
            if (value.HasValue && value.Value <= 0)
                throw new MachineProfileValidationException(name + " must be positive.");
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class CoordinateMapping
    {
        [DataMember(Name = "xAxis", Order = 1, EmitDefaultValue = false)]
        [DisplayName("纸面X对应机器轴")] public string XAxis { get; set; }

        [DataMember(Name = "yAxis", Order = 2, EmitDefaultValue = false)]
        [DisplayName("纸面Y对应机器轴")] public string YAxis { get; set; }

        [DataMember(Name = "zAxis", Order = 3, EmitDefaultValue = false)]
        [DisplayName("笔控对应机器轴")] public string ZAxis { get; set; }

        [DataMember(Name = "xDirection", Order = 4, EmitDefaultValue = false)]
        [DisplayName("X方向（1/-1/空）")] public int? XDirection { get; set; }

        [DataMember(Name = "yDirection", Order = 5, EmitDefaultValue = false)]
        [DisplayName("Y方向（1/-1/空）")] public int? YDirection { get; set; }

        [DataMember(Name = "zDirection", Order = 6, EmitDefaultValue = false)]
        [DisplayName("Z方向（1/-1/空）")] public int? ZDirection { get; set; }

        [DataMember(Name = "workOrigin", Order = 7, EmitDefaultValue = false)]
        [DisplayName("已验证工作坐标系（G54-G59）")] public string WorkOrigin { get; set; }

        internal void Validate()
        {
            MachineProfile.OptionalText(XAxis, "axisMapping.xAxis", 16);
            MachineProfile.OptionalText(YAxis, "axisMapping.yAxis", 16);
            MachineProfile.OptionalText(ZAxis, "axisMapping.zAxis", 16);
            ValidateDirection(XDirection, "axisMapping.xDirection");
            ValidateDirection(YDirection, "axisMapping.yDirection");
            ValidateDirection(ZDirection, "axisMapping.zDirection");
            MachineProfile.OptionalText(WorkOrigin, "axisMapping.workOrigin", 64);
        }

        /// <summary>Friendly summary for the PropertyGrid value column.</summary>
        public override string ToString()
        {
            string axis = string.Format("{0}→{1}  {2}→{3}  {4}→{5}",
                XAxis ?? "纸面X", XAxis ?? "X", YAxis ?? "纸面Y", YAxis ?? "Y", "笔控", ZAxis ?? "Z");
            string dirs = string.Format("方向 X{0} Y{1} Z{2}",
                XDirection.HasValue ? (XDirection.Value > 0 ? "+" : "−") : "?",
                YDirection.HasValue ? (YDirection.Value > 0 ? "+" : "−") : "?",
                ZDirection.HasValue ? (ZDirection.Value > 0 ? "+" : "−") : "?");
            return string.Format("{0} · {1}{2}", axis, dirs, string.IsNullOrEmpty(WorkOrigin) ? "" : " · " + WorkOrigin);
        }

        private static void ValidateDirection(int? direction, string name)
        {
            if (direction.HasValue && direction.Value != -1 && direction.Value != 1)
                throw new MachineProfileValidationException(name + " must be -1, 1, or null.");
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class CoordinateBounds
    {
        [DataMember(Name = "minX", Order = 1, EmitDefaultValue = false)] [DisplayName("X最小值")] public double? MinX { get; set; }
        [DataMember(Name = "maxX", Order = 2, EmitDefaultValue = false)] [DisplayName("X最大值")] public double? MaxX { get; set; }
        [DataMember(Name = "minY", Order = 3, EmitDefaultValue = false)] [DisplayName("Y最小值")] public double? MinY { get; set; }
        [DataMember(Name = "maxY", Order = 4, EmitDefaultValue = false)] [DisplayName("Y最大值")] public double? MaxY { get; set; }
        [DataMember(Name = "minZ", Order = 5, EmitDefaultValue = false)] [DisplayName("Z最小值")] public double? MinZ { get; set; }
        [DataMember(Name = "maxZ", Order = 6, EmitDefaultValue = false)] [DisplayName("Z最大值")] public double? MaxZ { get; set; }

        internal void Validate()
        {
            ValidateAxis(MinX, MaxX, "X");
            ValidateAxis(MinY, MaxY, "Y");
            ValidateAxis(MinZ, MaxZ, "Z");
        }

        private static void ValidateAxis(double? min, double? max, string axis)
        {
            MachineProfile.OptionalFinite(min, "usableBoundsWorkMm.min" + axis);
            MachineProfile.OptionalFinite(max, "usableBoundsWorkMm.max" + axis);
            if (min.HasValue != max.HasValue)
                throw new MachineProfileValidationException("usableBoundsWorkMm " + axis + " bounds must both be set or null.");
            if (min.HasValue && max.Value <= min.Value)
                throw new MachineProfileValidationException("usableBoundsWorkMm " + axis + " max must be greater than min.");
        }

        /// <summary>Friendly summary for the PropertyGrid value column.</summary>
        public override string ToString()
        {
            string range(double? min, double? max)
            { return min.HasValue && max.HasValue ? string.Format("{0}–{1}", min.Value, max.Value) : "未设置"; }
            return string.Format("X {0} · Y {1} · Z {2} (mm)", range(MinX, MaxX), range(MinY, MaxY), range(MinZ, MaxZ));
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class MachineSpeeds
    {
        [DataMember(Name = "drawingMmPerMin", Order = 1, EmitDefaultValue = false)] [DisplayName("绘图速度")] public double? DrawingMmPerMin { get; set; }
        [DataMember(Name = "travelMmPerMin", Order = 2, EmitDefaultValue = false)] [DisplayName("空移速度")] public double? TravelMmPerMin { get; set; }
        [DataMember(Name = "zMmPerMin", Order = 3, EmitDefaultValue = false)] [DisplayName("Z轴速度")] public double? ZMmPerMin { get; set; }

        internal void Validate()
        {
            ValidateSpeed(DrawingMmPerMin, "speeds.drawingMmPerMin");
            ValidateSpeed(TravelMmPerMin, "speeds.travelMmPerMin");
            ValidateSpeed(ZMmPerMin, "speeds.zMmPerMin");
        }

        /// <summary>Friendly summary for the PropertyGrid value column.</summary>
        public override string ToString()
        {
            return string.Format("绘图 F{0} · 空移 F{1} · Z F{2} (mm/min)",
                DrawingMmPerMin.HasValue ? DrawingMmPerMin.Value.ToString() : "?",
                TravelMmPerMin.HasValue ? TravelMmPerMin.Value.ToString() : "?",
                ZMmPerMin.HasValue ? ZMmPerMin.Value.ToString() : "?");
        }

        private static void ValidateSpeed(double? value, string name)
        {
            MachineProfile.OptionalFinite(value, name);
            if (value.HasValue && value.Value <= 0)
                throw new MachineProfileValidationException(name + " must be positive.");
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class MachineDelays
    {
        [DataMember(Name = "penUpMs", Order = 1, EmitDefaultValue = false)] [DisplayName("抬笔后等待")] public int? PenUpMs { get; set; }
        [DataMember(Name = "penDownMs", Order = 2, EmitDefaultValue = false)] [DisplayName("落笔后等待")] public int? PenDownMs { get; set; }

        internal void Validate()
        {
            ValidateDelay(PenUpMs, "delays.penUpMs");
            ValidateDelay(PenDownMs, "delays.penDownMs");
        }

        /// <summary>Friendly summary for the PropertyGrid value column.</summary>
        public override string ToString()
        {
            return string.Format("抬笔 {0} ms · 落笔 {1} ms",
                PenUpMs.HasValue ? PenUpMs.Value.ToString() : "?",
                PenDownMs.HasValue ? PenDownMs.Value.ToString() : "?");
        }

        private static void ValidateDelay(int? value, string name)
        {
            if (value.HasValue && (value.Value < 0 || value.Value > 3600000))
                throw new MachineProfileValidationException(name + " must be between 0 and 3600000 ms.");
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class MachineCapabilities
    {
        [DataMember(Name = "canHome", Order = 1, EmitDefaultValue = false)] [DisplayName("支持回机械原点")] public bool? CanHome { get; set; }
        [DataMember(Name = "canReset", Order = 2, EmitDefaultValue = false)] [DisplayName("支持复位")] public bool? CanReset { get; set; }
        [DataMember(Name = "canPause", Order = 3, EmitDefaultValue = false)] [DisplayName("支持暂停")] public bool? CanPause { get; set; }
        [DataMember(Name = "canCancel", Order = 4, EmitDefaultValue = false)] [DisplayName("支持取消")] public bool? CanCancel { get; set; }
        [DataMember(Name = "canVerifyPositionAfterReset", Order = 5, EmitDefaultValue = false)] [DisplayName("复位后可核对位置")] public bool? CanVerifyPositionAfterReset { get; set; }
        [DataMember(Name = "hasReliablePositionFeedback", Order = 6, EmitDefaultValue = false)] [DisplayName("位置反馈可靠")] public bool? HasReliablePositionFeedback { get; set; }
        [DataMember(Name = "requiresContinuousPenMotion", Order = 7, EmitDefaultValue = false)] [DisplayName("落笔保持需要连续运动")] public bool? RequiresContinuousPenMotion { get; set; }

        internal void Validate()
        {
            // Nullable flags intentionally have no defaults: null means unverified.
        }

        /// <summary>Friendly summary for the PropertyGrid value column.</summary>
        public override string ToString()
        {
            int known = 0;
            if (CanHome.HasValue) known++;
            if (CanReset.HasValue) known++;
            if (CanPause.HasValue) known++;
            if (CanCancel.HasValue) known++;
            if (CanVerifyPositionAfterReset.HasValue) known++;
            if (HasReliablePositionFeedback.HasValue) known++;
            if (RequiresContinuousPenMotion.HasValue) known++;
            return string.Format("已确认 {0}/7 项（展开逐项查看）", known);
        }
    }

    public sealed class MachineProfileValidationException : IOException
    {
        public MachineProfileValidationException(string message) : base(message) { }
    }
}
