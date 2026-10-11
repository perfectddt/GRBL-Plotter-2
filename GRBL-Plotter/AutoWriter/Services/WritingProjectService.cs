using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace GrblPlotter.AutoWriter.Services
{
    [DataContract]
    public sealed class WritingProject
    {
        [DataMember(IsRequired = false)] public bool RegionsEnabled { get; set; }
        [DataMember(IsRequired = false)] public System.Collections.Generic.List<WritingRegion> Regions { get; set; }
        [DataMember(IsRequired = false)] public int SelectedPage { get; set; }
        [DataMember(IsRequired = true)] public int Version { get; set; }
        [DataMember(IsRequired = true)] public string Text { get; set; }
        [DataMember(IsRequired = true)] public string FontPath { get; set; }
        [DataMember(IsRequired = true)] public string FontSha256 { get; set; }
        [DataMember(IsRequired = true)] public bool PageEnabled { get; set; }
        [DataMember(IsRequired = true)] public PageLayoutOptions Page { get; set; }
        [DataMember(IsRequired = true)] public double FontSizeMm { get; set; }
        [DataMember(IsRequired = true)] public double LetterSpacingMm { get; set; }
        [DataMember(IsRequired = true)] public double LineSpacingMm { get; set; }
        [DataMember(IsRequired = true)] public bool AlignStartToOrigin { get; set; }
        [DataMember(IsRequired = true)] public bool ParkEnabled { get; set; }
        [DataMember(IsRequired = true)] public double ParkX { get; set; }
        [DataMember(IsRequired = true)] public double ParkY { get; set; }
        [DataMember(IsRequired = true)] public double? PenUpValue { get; set; }
        [DataMember(IsRequired = true)] public double? PenDownValue { get; set; }
        [DataMember(IsRequired = true)] public double? ZMmPerMin { get; set; }
        [DataMember(IsRequired = true)] public double? DrawingMmPerMin { get; set; }
        [DataMember(IsRequired = true)] public double? TravelMmPerMin { get; set; }
        // Optional on version 1. Older files omit them. A supplement overlays glyphs and is never written back into the base pack.
        [DataMember(IsRequired = false)] public string SupplementPath { get; set; }
        [DataMember(IsRequired = false)] public string SupplementSha256 { get; set; }
        [DataMember(IsRequired = false)] public int VariationSeed { get; set; }
        [DataMember(IsRequired = false)] public double VariationMm { get; set; }
        [DataMember(IsRequired = false)] public bool DrawGridLines { get; set; }
        [DataMember(IsRequired = false)] public int GridRows { get; set; }
        [DataMember(IsRequired = false)] public int GridColumns { get; set; }
        public void Validate()
        {
            if (SelectedPage < 0) throw new InvalidDataException("页码无效。");
            if (Version != 1) throw new InvalidDataException("不支持的写字项目版本。");
            if (Text == null || FontPath == null || FontSha256 == null || Page == null) throw new InvalidDataException("项目字段不完整。");
            if (SupplementPath == null) SupplementPath = "";
            if (SupplementSha256 == null) SupplementSha256 = "";
            Page.Validate();
            if (Regions != null) foreach (var region in Regions)
            {
                if (region == null) throw new InvalidDataException("区域为空。");
                if (region.FontPath == null) region.FontPath = "";
                if (region.FontSha256 == null) region.FontSha256 = "";
                region.Validate(RegionsEnabled ? Page : new PageLayoutOptions { WidthMm = double.MaxValue, HeightMm = double.MaxValue });
                if (region.FontPath.Length > 0 && region.FontSha256.Length != 64) throw new InvalidDataException("区域字体校验值无效。");
            }
            if (RegionsEnabled && (!PageEnabled || AlignStartToOrigin)) throw new InvalidDataException("多区域需要单页排版并保留纸张原点。");
            Check(FontSizeMm, true); Check(LetterSpacingMm, false); Check(LineSpacingMm, false);
            CheckFinite(ParkX); CheckFinite(ParkY);
            foreach (double? value in new[] { PenUpValue, PenDownValue, ZMmPerMin, DrawingMmPerMin, TravelMmPerMin })
                if (value.HasValue) CheckFinite(value.Value);
            foreach (double? value in new[] { ZMmPerMin, DrawingMmPerMin, TravelMmPerMin })
                if (value.HasValue && value.Value <= 0) throw new InvalidDataException("速度必须为正数。");
            if (PageEnabled && AlignStartToOrigin) throw new InvalidDataException("单页模式不能将起笔点移动到原点，否则边距会失效。");
            if (FontPath.Length == 0 && FontSha256.Length != 0) throw new InvalidDataException("内置字体标识无效。");
            if (FontPath.Length != 0 && FontSha256.Length != 64) throw new InvalidDataException("字体校验值无效。");
            if (SupplementPath.Length == 0 && SupplementSha256.Length != 0) throw new InvalidDataException("补充字库标识无效。");
            if (SupplementPath.Length != 0 && SupplementSha256.Length != 64) throw new InvalidDataException("补充字库校验值无效。");
            if (VariationMm < 0 || VariationMm > 0.35 || double.IsNaN(VariationMm) || double.IsInfinity(VariationMm)) throw new InvalidDataException("手写变化幅度必须在 0 到 0.35 mm。");
            if (GridRows < 0 || GridColumns < 0 || GridRows > 40 || GridColumns > 40) throw new InvalidDataException("表格行列无效。");
            if (DrawGridLines && (GridRows < 1 || GridColumns < 1)) throw new InvalidDataException("要画出的表格缺少行列。");
        }
        private static void Check(double value, bool positive)
        { CheckFinite(value); if (positive ? value <= 0 : value < 0) throw new InvalidDataException("字号或间距无效。"); }
        private static void CheckFinite(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("项目数值必须有限。"); }
    }

    public static class WritingProjectService
    {
        public static string Fingerprint(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        public static void ValidateFont(WritingProject project)
        {
            CheckFile(project.FontPath, project.FontSha256, "项目字体");
            CheckFile(project.SupplementPath, project.SupplementSha256, "补充字库");
            if (project.Regions == null) return;
            foreach (WritingRegion region in project.Regions)
                if (region != null) CheckFile(region.FontPath ?? "", region.FontSha256 ?? "", "区域字体");
        }
        private static void CheckFile(string path, string fingerprint, string label)
        {
            if (path == null || path.Length == 0) return;
            if (!File.Exists(path)) throw new FileNotFoundException(label + "缺失，请恢复原字库后再打开。不会改写其他字库。", path);
            if (!string.Equals(Fingerprint(path), fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(label + "内容已改变，请恢复原字库后再打开。");
        }
        public static void Save(string path, WritingProject project)
        {
            project.Validate(); ValidateFont(project);
            string full = Path.GetFullPath(path);
            string temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(WritingProject)).WriteObject(stream, project); stream.Flush(true); }
            if (File.Exists(full)) File.Replace(temporary, full, full + ".bak-" + Guid.NewGuid().ToString("N"), true);
            else File.Move(temporary, full);
        }
        public static WritingProject Load(string path)
        {
            try
            {
                WritingProject project;
                using (var stream = File.OpenRead(path)) project = (WritingProject)new DataContractJsonSerializer(typeof(WritingProject)).ReadObject(stream);
                if (project == null) throw new InvalidDataException("项目为空。");
                project.Validate(); ValidateFont(project); return project;
            }
            catch (Exception error) { throw new InvalidDataException("写字项目无法读取：" + error.Message, error); }
        }
    }
}
