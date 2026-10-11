using System;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    public sealed class GlyphCoverageHit
    {
        public int Index;
        public int Line;
        public int Column;
        public char Symbol;
        public int RegionNumber;
    }

    public static class GlyphCoverageService
    {
        public static List<GlyphCoverageHit> Scan(string text, Func<char, BuiltinGlyph> resolve, int regionNumber)
        {
            var hits = new List<GlyphCoverageHit>();
            if (string.IsNullOrEmpty(text) || resolve == null) return hits;
            int line = 1, column = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char symbol = text[i];
                if (symbol == '\n') { line++; column = 1; continue; }
                if (symbol == '\r') continue;
                bool missing = false;
                try
                {
                    BuiltinGlyph glyph = resolve(symbol);
                    missing = glyph == null;
                }
                catch (Exception) { missing = true; }
                if (missing)
                    hits.Add(new GlyphCoverageHit { Index = i + 1, Line = line, Column = column, Symbol = symbol, RegionNumber = regionNumber });
                column++;
            }
            return hits;
        }

        public static string Describe(IList<GlyphCoverageHit> hits)
        {
            if (hits == null || hits.Count == 0) return "没有缺字。";
            var parts = new List<string>();
            int shown = Math.Min(hits.Count, 30);
            for (int i = 0; i < shown; i++)
            {
                GlyphCoverageHit hit = hits[i];
                string where = hit.RegionNumber > 0 ? "区域 " + hit.RegionNumber + " " : "";
                parts.Add(where + "第 " + hit.Line + " 行第 " + hit.Column + " 列「" + hit.Symbol + "」（第 " + hit.Index + " 个字符）");
            }
            return "缺 " + hits.Count + " 处，尚未排成页：" + string.Join("；", parts.ToArray()) + (hits.Count > shown ? " …" : "") + "。缺字不会静默跳过。";
        }
    }

    public static class WritingVariation
    {
        public const double MaxAmountMm = 0.35;

        public static IList<WritingStroke> Apply(IList<WritingStroke> strokes, int seed, double amountMm, PageLayoutOptions sheet)
        {
            if (strokes == null) throw new ArgumentNullException("strokes");
            if (sheet == null) throw new ArgumentNullException("sheet");
            if (amountMm == 0) return strokes;
            if (amountMm < 0 || amountMm > MaxAmountMm || double.IsNaN(amountMm) || double.IsInfinity(amountMm))
                throw new ArgumentOutOfRangeException("amountMm", "手写变化幅度必须在 0 到 " + MaxAmountMm.ToString("0.##") + " mm。");
            sheet.Validate();
            var output = new List<WritingStroke>();
            int pointIndex = 0;
            foreach (WritingStroke stroke in strokes)
            {
                var points = new List<WritingPoint>();
                if (stroke != null && stroke.Points != null)
                {
                    foreach (WritingPoint point in stroke.Points)
                    {
                        double dx = Unit(seed, pointIndex) * amountMm;
                        double dy = Unit(seed, pointIndex + 104729) * amountMm;
                        pointIndex++;
                        double x = point.X + dx;
                        double y = point.Y + dy;
                        if (x < -1e-9 || x > sheet.WidthMm + 1e-9 || y > 1e-9 || y < -sheet.HeightMm - 1e-9)
                            throw new InvalidOperationException("手写变化超出纸张，已拒绝。请减小幅度或增大边距。未裁剪路径。");
                        points.Add(new WritingPoint(x, y));
                    }
                }
                output.Add(new WritingStroke(points));
            }
            return output;
        }

        public static IList<WritingLayoutPage> ApplyPages(IList<WritingLayoutPage> pages, int seed, double amountMm, PageLayoutOptions sheet)
        {
            if (amountMm == 0 || pages == null) return pages;
            var output = new List<WritingLayoutPage>();
            for (int i = 0; i < pages.Count; i++)
            {
                WritingLayoutPage page = pages[i];
                output.Add(new WritingLayoutPage
                {
                    PageNumber = page.PageNumber,
                    LineCount = page.LineCount,
                    Strokes = Apply(page.Strokes, seed + i * 1009, amountMm, sheet)
                });
            }
            return output;
        }

        private static double Unit(int seed, int index)
        {
            unchecked
            {
                uint value = (uint)seed * 16777619u ^ (uint)index * 2166136261u;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                return (value % 2001) / 1000.0 - 1.0;
            }
        }
    }

    public static class WritingGridService
    {
        public static List<WritingRegion> Cells(PageLayoutOptions sheet, int rows, int columns)
        {
            Require(sheet, rows, columns);
            double left = sheet.LeftMm, top = sheet.TopMm;
            double width = sheet.WidthMm - sheet.LeftMm - sheet.RightMm;
            double height = sheet.HeightMm - sheet.TopMm - sheet.BottomMm;
            double cellW = width / columns, cellH = height / rows;
            var cells = new List<WritingRegion>();
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    var region = new WritingRegion
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Text = "",
                        X = left + column * cellW,
                        Y = top + row * cellH,
                        Width = cellW,
                        Height = cellH,
                        Left = Math.Min(1, cellW / 4),
                        Right = Math.Min(1, cellW / 4),
                        Top = Math.Min(1, cellH / 4),
                        Bottom = Math.Min(1, cellH / 4),
                        FontSize = 6,
                        LetterGap = 1,
                        LineGap = 1
                    };
                    region.Validate(sheet);
                    cells.Add(region);
                }
            return cells;
        }

        public static List<WritingStroke> Lines(PageLayoutOptions sheet, int rows, int columns)
        {
            Require(sheet, rows, columns);
            double left = sheet.LeftMm, right = sheet.WidthMm - sheet.RightMm;
            double top = sheet.TopMm, bottom = sheet.HeightMm - sheet.BottomMm;
            var lines = new List<WritingStroke>();
            for (int column = 0; column <= columns; column++)
            {
                double x = left + (right - left) * column / columns;
                lines.Add(new WritingStroke(new[] { new WritingPoint(x, -top), new WritingPoint(x, -bottom) }));
            }
            for (int row = 0; row <= rows; row++)
            {
                double y = top + (bottom - top) * row / rows;
                lines.Add(new WritingStroke(new[] { new WritingPoint(left, -y), new WritingPoint(right, -y) }));
            }
            return lines;
        }

        public static IList<WritingLayoutPage> WithLines(IList<WritingLayoutPage> pages, IList<WritingStroke> lines)
        {
            if (pages == null || lines == null || lines.Count == 0) return pages;
            var output = new List<WritingLayoutPage>();
            foreach (WritingLayoutPage page in pages)
            {
                var strokes = new List<WritingStroke>(page.Strokes ?? new WritingStroke[0]);
                strokes.AddRange(lines);
                output.Add(new WritingLayoutPage { PageNumber = page.PageNumber, LineCount = page.LineCount, Strokes = strokes });
            }
            return output;
        }

        private static void Require(PageLayoutOptions sheet, int rows, int columns)
        {
            if (sheet == null) throw new ArgumentNullException("sheet");
            sheet.Validate();
            if (rows < 1 || columns < 1 || rows > 40 || columns > 40) throw new ArgumentOutOfRangeException("rows", "表格行数和列数必须是 1 到 40。");
            if (sheet.WidthMm - sheet.LeftMm - sheet.RightMm < columns * 3 || sheet.HeightMm - sheet.TopMm - sheet.BottomMm < rows * 3)
                throw new InvalidOperationException("格子小于 3 mm，没有生成表格。");
        }
    }

    public static class WritingCommandGuard
    {
        public static bool Blocks(bool taskBusy, string command, bool jogging)
        {
            if (!taskBusy) return false;
            if (jogging) return true;
            if (string.IsNullOrWhiteSpace(command)) return false;
            string upper = command.ToUpperInvariant();
            if (upper.Contains("#STOP")) return false;
            if (upper.StartsWith("?")) return false;
            if (upper.Contains("$J")) return true;
            for (int i = 0; i < upper.Length; i++)
            {
                if (upper[i] != 'G') continue;
                if (i > 0 && char.IsLetterOrDigit(upper[i - 1])) continue;
                int j = i + 1, value = 0;
                bool any = false;
                while (j < upper.Length && upper[j] >= '0' && upper[j] <= '9')
                {
                    any = true;
                    value = value * 10 + (upper[j] - '0');
                    j++;
                    if (value > 100) break;
                }
                if (any && (value == 0 || value == 1 || value == 2 || value == 3 || value == 38)) return true;
            }
            return false;
        }
    }

    public static class WritingPreviewBinding
    {
        public static bool BlocksStart(bool stale, string editorText, string loadedProgram)
        {
            if (!stale || string.IsNullOrEmpty(loadedProgram)) return false;
            string editor = PageTaskService.Canon(editorText);
            string loaded = PageTaskService.Canon(loadedProgram);
            return loaded.Length > 0 && (editor == loaded || editor.Contains(loaded));
        }
    }
}
