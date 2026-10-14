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
using System.Text;
using System.Text.RegularExpressions;
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

        /* 2026-10-12 click-stall fix: one full pack is ~8 MB of JSON / ~9.6k glyphs and
         * costs ~0.5 s to parse; the setup dialog used to parse EVERY discovered pack
         * (10 in the dev tree, ~4 s) on the UI thread just to fill the font selector.
         * Loads are now cached by path+size+mtime, and the selector uses Peek(). */
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, GlyphFontPack> Cache =
            new Dictionary<string, GlyphFontPack>(StringComparer.OrdinalIgnoreCase);

        private static string CacheKey(string path)
        {
            var info = new FileInfo(path);
            return path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        }

        public static GlyphFontPack LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("字库包路径为空。", "path");
            if (!File.Exists(path))
                throw new FileNotFoundException("字库包文件不存在：" + path, path);
            string key = CacheKey(path);
            lock (CacheLock)
            {
                GlyphFontPack cached;
                if (Cache.TryGetValue(key, out cached))
                    return cached;
            }
            GlyphFontPack pack = ParseFromFile(path);
            lock (CacheLock) { Cache[key] = pack; }
            return pack;
        }

        /// <summary>Full parse + validation; use LoadFromFile unless the cache must be bypassed.</summary>
        private static GlyphFontPack ParseFromFile(string path)
        {

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

        /// <summary>
        /// Cheap header read for the font selector: pack name + glyph count WITHOUT
        /// deserializing the pack (~8 MB / 0.5 s each). The generator emits compact
        /// one-line JSON with glyphs as [{"c":"X","strokes":[...]}, ...] first and the
        /// metadata fields (name, source, ...) at the END (Python sort_keys), so we
        /// count the "c":" byte pattern while streaming and regex the tail chunk for
        /// the name. The pattern has no leading brace on purpose: entries carrying a
        /// per-glyph advance serialize as {"advanceMm":...,"c":"X",...} (alphabetical
        /// keys). ASCII pattern matching on raw bytes is UTF-8 safe; stroke arrays are
        /// pure numbers so the pattern can only appear as a glyph key. The count is a
        /// display label only - LoadFromFile revalidates everything on real use.
        /// </summary>
        public static GlyphFontPackInfo Peek(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("字库包路径为空。", "path");
            if (!File.Exists(path))
                throw new FileNotFoundException("字库包文件不存在：" + path, path);

            byte[] pattern = Encoding.ASCII.GetBytes("\"c\":\"");
            int count = 0;
            // Sliding window [carry | new chunk]. Every match fully inside the window
            // is counted exactly once: a match starting in the carried overlap always
            // extends past the previous chunk's end (carry is shorter than the pattern),
            // so it could not have been counted before; matches starting in the new
            // bytes are new by definition.
            byte[] window = new byte[1 << 16];
            int carryLen = 0;
            var tail = new MemoryStream();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                int read;
                while ((read = stream.Read(window, carryLen, window.Length - carryLen)) > 0)
                {
                    int total = carryLen + read;
                    count += CountOccurrences(window, 0, total, pattern);

                    // keep only the trailing 32 KB for the metadata regex (the new
                    // chunk sits at window[carryLen .. carryLen+read] - write it out
                    // BEFORE the overlap copy below clobbers the window head)
                    tail.Write(window, carryLen, read);
                    if (tail.Length > 32768)
                    {
                        byte[] kept = tail.ToArray();
                        tail.SetLength(0);
                        tail.Write(kept, kept.Length - 32768, 32768);
                    }

                    int newCarry = Math.Min(pattern.Length - 1, total);
                    Buffer.BlockCopy(window, total - newCarry, window, 0, newCarry);
                    carryLen = newCarry;
                }
            }
            if (count == 0)
                throw new InvalidOperationException("字库包没有任何字形：" + path);

            string tailText = Encoding.UTF8.GetString(tail.ToArray());
            var match = Regex.Match(tailText, "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success)
                throw new InvalidOperationException("字库包缺少 name 字段：" + path);
            return new GlyphFontPackInfo(UnescapeJson(match.Groups[1].Value), count, path);
        }

        private static int CountOccurrences(byte[] haystack, int offset, int length, byte[] needle)
        {
            int found = 0;
            int limit = offset + length - needle.Length;
            for (int i = offset; i <= limit; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { hit = false; break; }
                if (hit) found++;
            }
            return found;
        }

        private static string UnescapeJson(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[++i];
                    switch (next)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        case 'u':
                            if (i + 4 < value.Length)
                            {
                                builder.Append((char)Convert.ToInt32(value.Substring(i + 1, 4), 16));
                                i += 4;
                            }
                            break;
                        default: builder.Append(next); break;
                    }
                }
                else builder.Append(c);
            }
            return builder.ToString();
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

    /// <summary>Name + glyph count of a pack, read cheaply by GlyphFontPack.Peek.</summary>
    public sealed class GlyphFontPackInfo
    {
        public string Name { get; private set; }
        public int GlyphCount { get; private set; }
        public string Path { get; private set; }

        public GlyphFontPackInfo(string name, int glyphCount, string path)
        {
            Name = name;
            GlyphCount = glyphCount;
            Path = path;
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
            /* 2026-10-12: the deployed copy (bin/AutoWriter/Fonts) and the dev-tree
             * copy (../../AutoWriter/Fonts) are the same files in a dev build - listing
             * both made the selector show every pack twice (and probe-parse each copy).
             * Drop duplicates by file name + size; the first hit wins, which is the
             * deployed layout because it is scanned first. */
            var byIdentity = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<string>();
            foreach (string file in results)
            {
                string identity;
                try
                {
                    var info = new FileInfo(file);
                    identity = info.Name + "|" + info.Length;
                }
                catch (Exception) { identity = file; }
                if (byIdentity.Add(identity)) deduped.Add(file);
            }
            return deduped;
        }
    }
}
