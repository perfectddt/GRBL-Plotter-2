/* 2026-10-06 stage 2 font-pack loader (字库路线 A, doc/阶段2-字库路线.md).
 *
 * Loads the glyph font pack JSON produced by scripts/generate-stage2-fontpack.py
 * from hanzi-writer-data medians: one polyline per stroke, ordered like a person
 * writes, in millimetres on the shared 12 mm em box (baseline 1 mm), so pack
 * glyphs lay out interchangeably with BuiltinGlyphLibrary's schematic glyphs.
 *
 * Resolution chain: pack -> BuiltinGlyphLibrary -> loud failure. An unknown
 * character is never silently dropped - the operator extends the pack instead.
 *
 * Pure logic, no WinForms / firmware dependencies, so the offline suites can
 * compile this file standalone. JSON parsing uses the project's existing
 * DataContractJsonSerializer convention (see MachineProfileService).
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    [DataContract]
    internal sealed class GlyphFontPackFile
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "source")] public string Source;
        [DataMember(Name = "license")] public string License;
        [DataMember(Name = "emSizeMm")] public double EmSizeMm;
        [DataMember(Name = "bodySizeMm")] public double BodySizeMm;
        [DataMember(Name = "baselineMm")] public double BaselineMm;
        [DataMember(Name = "advanceCjkMm")] public double AdvanceCjkMm;
        [DataMember(Name = "advanceSpaceMm")] public double AdvanceSpaceMm;
        [DataMember(Name = "glyphs")] public List<GlyphFontPackGlyph> Glyphs;
    }

    [DataContract]
    internal sealed class GlyphFontPackGlyph
    {
        /// <summary>One UTF-16 character as a string value. Glyphs travel as an ARRAY of
        /// entries, never as a JSON object keyed by character: the .NET Framework
        /// DataContractJsonSerializer silently returns an EMPTY dictionary for object
        /// members (probed with Dictionary&lt;string,double&gt; too), while string values
        /// and List&lt;T&gt;/nested collections read reliably.</summary>
        [DataMember(Name = "c")] public string Character;
        // Optional in schema 1; omitted entries retain the pack-wide CJK advance.
        [DataMember(Name = "advanceMm", EmitDefaultValue = false)] public double? AdvanceMm;
        /// <summary>Flat [x1,y1,x2,y2,...] polylines in mm on the pack's em box.</summary>
        [DataMember(Name = "strokes")] public List<List<double>> Strokes;
    }

    public sealed class GlyphFontPack
    {
        private readonly Dictionary<char, BuiltinGlyph> glyphs;

        public string Name { get; private set; }
        public string Source { get; private set; }
        public string License { get; private set; }
        public double AdvanceCjkMm { get; private set; }
        public double AdvanceSpaceMm { get; private set; }
        public int GlyphCount { get { return glyphs.Count; } }

        private GlyphFontPack(Dictionary<char, BuiltinGlyph> glyphs, string name,
                              string source, string license, double advanceCjkMm, double advanceSpaceMm)
        {
            this.glyphs = glyphs;
            Name = name;
            Source = source;
            License = license;
            AdvanceCjkMm = advanceCjkMm;
            AdvanceSpaceMm = advanceSpaceMm;
        }

        public static GlyphFontPack LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("字库包路径为空。", "path");
            if (!File.Exists(path))
                throw new FileNotFoundException("字库包文件不存在：" + path, path);

            GlyphFontPackFile file;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GlyphFontPackFile),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 1 << 22 });
                using (var stream = File.OpenRead(path))
                {
                    file = (GlyphFontPackFile)serializer.ReadObject(stream);
                }
            }
            catch (Exception err)
            {
                throw new InvalidOperationException("字库包解析失败：" + path + "（" + err.Message + "）", err);
            }

            if (file == null)
                throw new InvalidOperationException("字库包为空：" + path);
            if (file.SchemaVersion != 1)
                throw new InvalidOperationException(
                    "字库包 schema 版本不支持：" + file.SchemaVersion + "（期望 1，文件：" + path + "）。");
            if (file.Glyphs == null || file.Glyphs.Count == 0)
                throw new InvalidOperationException("字库包没有任何字形：" + path);
            if (file.EmSizeMm != BuiltinGlyphLibrary.EmSizeMm)
                throw new InvalidOperationException(
                    "字库包 em 尺寸 " + file.EmSizeMm + "mm 与内置字形库 " + BuiltinGlyphLibrary.EmSizeMm +
                    "mm 不一致：混排基线会错位，拒绝加载（" + path + "）。");
            if (!ValidAdvance(file.AdvanceCjkMm) || !ValidAdvance(file.AdvanceSpaceMm))
                throw new InvalidOperationException("字库包步进宽非法（" + path + "）。");

            var tolerance = 1.0;    // the generator enforces 0.5; the loader only guards corruption
            var glyphs = new Dictionary<char, BuiltinGlyph>(file.Glyphs.Count);
            foreach (var entry in file.Glyphs)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Character) || entry.Character.Length != 1)
                    throw new InvalidOperationException(
                        "字库包字形条目的 c 字段不是单字符：" + (entry == null ? "null" : entry.Character) + "（" + path + "）。");
                char symbol = entry.Character[0];
                double advance = entry.AdvanceMm ?? file.AdvanceCjkMm;
                if (!ValidAdvance(advance))
                    throw new InvalidOperationException("字库包字形步进宽非法：「" + symbol + "」（" + path + "）。");
                if (glyphs.ContainsKey(symbol))
                    throw new InvalidOperationException("字库包有重复字形「" + symbol + "」（" + path + "）。");
                if (symbol == ' ')
                    throw new InvalidOperationException("字库包不应包含空格字形（空格走内置步进）：" + path);
                if (entry.Strokes == null || entry.Strokes.Count == 0)
                    throw new InvalidOperationException("「" + symbol + "」没有任何笔画（" + path + "）。");

                var strokes = new List<WritingStroke>(entry.Strokes.Count);
                foreach (var flat in entry.Strokes)
                {
                    if (flat == null || flat.Count < 4 || flat.Count % 2 != 0)
                        throw new InvalidOperationException(
                            "「" + symbol + "」有一笔点数非法（需偶数且 ≥4 个数）：" + (flat == null ? "null" : flat.Count.ToString()) + "。");
                    var points = new List<WritingPoint>(flat.Count / 2);
                    for (int i = 0; i < flat.Count; i += 2)
                    {
                        double x = flat[i], y = flat[i + 1];
                        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) ||
                            x < -tolerance || x > file.EmSizeMm + tolerance || y < -tolerance || y > file.EmSizeMm + tolerance)
                            throw new InvalidOperationException(
                                "「" + symbol + "」有坐标超出 em 框（容差 " + tolerance + "mm）：(" +
                                x.ToString("R", CultureInfo.InvariantCulture) + ", " +
                                y.ToString("R", CultureInfo.InvariantCulture) + ")。");
                        points.Add(new WritingPoint(x, y));
                        if (entry.AdvanceMm.HasValue && (x < 0 || x > advance))
                            throw new InvalidOperationException("字形笔画超出自己的步进宽：「" + symbol + "」。");
                    }
                    strokes.Add(new WritingStroke(points));
                }
                glyphs[symbol] = new BuiltinGlyph { Symbol = symbol, AdvanceMm = advance, Strokes = strokes };
            }

            return new GlyphFontPack(glyphs, file.Name, file.Source, file.License,
                                     file.AdvanceCjkMm, file.AdvanceSpaceMm);
        }

        private static bool ValidAdvance(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 && value <= BuiltinGlyphLibrary.EmSizeMm * 4; }

        public bool Contains(char symbol)
        { return glyphs.ContainsKey(symbol); }

        /// <summary>Chain availability for the UI precheck: the pack, the space,
        /// or a built-in schematic glyph can resolve the character.</summary>
        public bool ChainHasGlyph(char symbol)
        { return glyphs.ContainsKey(symbol) || BuiltinGlyphLibrary.Knows(symbol); }

        /// <summary>Pack-only resolution: throws loudly when the pack lacks the character.</summary>
        public BuiltinGlyph Resolve(char symbol)
        {
            BuiltinGlyph glyph;
            if (glyphs.TryGetValue(symbol, out glyph))
                return glyph;
            throw new InvalidOperationException(
                "字库包「" + Name + "」（" + glyphs.Count + " 字）没有「" + symbol + "」这个字。" +
                "不认识的字不会静默跳过——用 scripts/generate-stage2-fontpack.py 扩包后再写。");
        }

        /// <summary>Production chain: pack first, then the built-in schematic glyphs (space included).</summary>
        public BuiltinGlyph ResolveWithFallback(char symbol)
        {
            BuiltinGlyph glyph;
            if (glyphs.TryGetValue(symbol, out glyph))
                return glyph;
            try
            {
                return BuiltinGlyphLibrary.Resolve(symbol);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "字库包「" + Name + "」（" + glyphs.Count + " 字）和内置字形库都没有「" + symbol + "」这个字。" +
                    "不认识的字不会静默跳过——用 scripts/generate-stage2-fontpack.py 扩包后再写。");
            }
        }

        /// <summary>Resolver delegate for TextLayoutService.Layout.</summary>
        public Func<char, BuiltinGlyph> ToResolver()
        { return ResolveWithFallback; }

        /// <summary>
        /// Returns a new pack where the overlay wins per character. The original files are not rewritten,
        /// so an existing project fingerprint stays valid until the operator saves a project that references the overlay.
        /// </summary>
        public GlyphFontPack OverlayWith(GlyphFontPack overlay)
        {
            if (overlay == null || overlay.glyphs.Count == 0) return this;
            var copy = new Dictionary<char, BuiltinGlyph>(glyphs);
            foreach (KeyValuePair<char, BuiltinGlyph> pair in overlay.glyphs) copy[pair.Key] = pair.Value;
            return new GlyphFontPack(copy, Name + " + " + overlay.Name, Source, License, AdvanceCjkMm, AdvanceSpaceMm);
        }
    }

    /// <summary>Discovers glyph font packs for the writing UI font selector.
    /// Scans the Fonts folder next to the executable (deployed layout) and the
    /// dev-tree source location (bin/Debug -> ../../AutoWriter/Fonts). Discovery
    /// must never throw into the dialog constructor - unreadable or missing
    /// folders simply contribute nothing.</summary>
    public static class GlyphFontPackCatalog
    {
        public static IList<string> DiscoverPackPaths()
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(exeDir, "Fonts"),                                  // deployed layout
                Path.Combine(exeDir, "AutoWriter", "Fonts"),                    // csproj path-preserving copy
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "AutoWriter", "Fonts")),      // dev tree
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "artifacts", "stage2")), // dev tree: generated route-B packs (gitignored)
            };
            foreach (string dir in candidates)
            {
                if (!Directory.Exists(dir))
                    continue;
                try
                {
                    var files = new List<string>(Directory.GetFiles(dir, "*.fontpack.json"));
                    files.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
                    foreach (string file in files)
                        if (seen.Add(file))
                            results.Add(file);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return results;
        }
    }
}
