/* 2026-10-06 stage 2 (§11.2-1 multi-character layout): string -> one stroke set.
 *
 * Lays a text string out as a single ordered stroke list that the existing
 * WritingProgramService can turn into one continuous program - the operator
 * no longer generates character by character. Pure logic, no WinForms /
 * firmware dependencies, so the offline suites compile it standalone.
 *
 * Rules:
 *  - Characters run left to right; the cursor advances by the glyph's own
 *    advance plus the operator's letter spacing.
 *  - "\n" starts a new line: the cursor returns to X0 and drops by the
 *    em line height plus the operator's line spacing ("\r" is swallowed).
 *  - Baseline alignment comes free from the shared em box: every glyph is
 *    authored on the same box, so translating by whole advances keeps all
 *    baselines on one line.
 *  - A space is a stroke-less advance, not an error.
 *  - Unknown characters, empty text, stroke-less text (spaces/newlines
 *    only) and negative spacings are rejected loudly - nothing is dropped
 *    or clipped silently.
 */
using System;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    [System.Runtime.Serialization.DataContract]
    public sealed class PageLayoutOptions
    {
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double WidthMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double HeightMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double LeftMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double RightMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double TopMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public double BottomMm { get; set; }
        [System.Runtime.Serialization.DataMember(IsRequired = true)] public bool AutoWrap { get; set; }
        // 0 left, 1 center, 2 right. Optional for compatibility with existing projects.
        [System.Runtime.Serialization.DataMember(IsRequired = false)] public int Alignment { get; set; }
        public void Validate()
        {
            double[] values = { WidthMm, HeightMm, LeftMm, RightMm, TopMm, BottomMm };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentException("纸张和边距必须是非负有限值。");
            if (WidthMm <= LeftMm + RightMm || HeightMm <= TopMm + BottomMm)
                throw new ArgumentException("纸张尺寸必须大于两侧边距之和。");
            if (Alignment < 0 || Alignment > 2) throw new ArgumentException("对齐方式无效。");
        }
    }

    public sealed class WritingLayoutPage
    {
        public IList<WritingStroke> Strokes { get; internal set; }
        public int PageNumber { get; internal set; }
        public int LineCount { get; internal set; }
    }

    public static class TextLayoutService
    {
        public static IList<WritingStroke> Layout(string text, Func<char, BuiltinGlyph> resolve,
                                                  double letterSpacingMm, double lineSpacingMm,
                                                  double lineHeightMm)
        {
            return Layout(text, resolve, letterSpacingMm, lineSpacingMm, lineHeightMm, 1.0);
        }

        /* 2026-10-06 stage 2 (§11.2-2 font size): glyphScale shrinks/grows every glyph about its
         * own em-box origin AND scales the advance and the line height with it, so a multi-line,
         * multi-character block stays proportionally spaced at any size. The operator's letter /
         * line spacing stay absolute millimetres - they are the fine-tuning knobs, not part of
         * the glyph geometry. scale = requested body height / BuiltinGlyphLibrary.BodySizeMm. */
        public static IList<WritingStroke> Layout(string text, Func<char, BuiltinGlyph> resolve,
                                                  double letterSpacingMm, double lineSpacingMm,
                                                  double lineHeightMm, double glyphScale)
        {
            if (string.IsNullOrEmpty(text))
                throw new InvalidOperationException("写字文本不能为空。");
            if (resolve == null)
                throw new ArgumentNullException("resolve");
            RequireNonNegativeFinite(letterSpacingMm, "字距");
            RequireNonNegativeFinite(lineSpacingMm, "行距");
            if (double.IsNaN(lineHeightMm) || double.IsInfinity(lineHeightMm) || lineHeightMm <= 0)
                throw new ArgumentOutOfRangeException("lineHeightMm", lineHeightMm, "行高必须是大于 0 的有限值。");
            if (double.IsNaN(glyphScale) || double.IsInfinity(glyphScale) || glyphScale <= 0)
                throw new ArgumentOutOfRangeException("glyphScale", glyphScale, "字号缩放必须是大于 0 的有限值。");

            var output = new List<WritingStroke>();
            double cursorX = 0;
            double cursorY = 0;
            foreach (char symbol in text)
            {
                if (symbol == '\r')
                    continue;
                if (symbol == '\n')
                {
                    cursorX = 0;
                    cursorY -= (lineHeightMm * glyphScale + lineSpacingMm);
                    continue;
                }

                BuiltinGlyph glyph = resolve(symbol);       // throws on an unknown character
                if (glyph == null)
                    throw new InvalidOperationException("字形解析返回了空值：" + symbol);
                if (glyph.Strokes != null)
                {
                    foreach (WritingStroke stroke in glyph.Strokes)
                    {
                        if (stroke == null || stroke.Points == null)
                            continue;                       // a defective glyph is the library's own bug; skip keeps the layout aligned
                        var points = new List<WritingPoint>(stroke.Points.Count);
                        foreach (WritingPoint point in stroke.Points)
                            points.Add(new WritingPoint(point.X * glyphScale + cursorX, point.Y * glyphScale + cursorY));
                        output.Add(new WritingStroke(points));
                    }
                }
                cursorX += glyph.AdvanceMm * glyphScale + letterSpacingMm;
            }

            if (output.Count == 0)
                throw new InvalidOperationException("这段文本没有产生任何笔画（可能全是空格或换行）。");
            return output;
        }

        // Paper origin is the top-left corner: down the sheet is negative paper Y.
        // This API does not change the legacy unbounded Layout overloads.
        public static IList<WritingStroke> LayoutPage(string text, Func<char, BuiltinGlyph> resolve,
            double letterSpacingMm, double lineSpacingMm, double lineHeightMm, double glyphScale,
            PageLayoutOptions page)
        {
            return LayoutPagesCore(text, resolve, letterSpacingMm, lineSpacingMm, lineHeightMm, glyphScale, page, false)[0].Strokes;
        }

        public static IList<WritingLayoutPage> LayoutPages(string text, Func<char, BuiltinGlyph> resolve,
            double letterSpacingMm, double lineSpacingMm, double lineHeightMm, double glyphScale, PageLayoutOptions page)
        {
            return LayoutPagesCore(text, resolve, letterSpacingMm, lineSpacingMm, lineHeightMm, glyphScale, page, true);
        }

        private static IList<WritingLayoutPage> LayoutPagesCore(string text, Func<char, BuiltinGlyph> resolve,
            double letterSpacingMm, double lineSpacingMm, double lineHeightMm, double glyphScale, PageLayoutOptions page, bool paginate)
        {
            if (page == null) throw new ArgumentNullException("page");
            page.Validate();
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("写字文本不能为空。");
            if (resolve == null) throw new ArgumentNullException("resolve");
            RequireNonNegativeFinite(letterSpacingMm, "字距");
            RequireNonNegativeFinite(lineSpacingMm, "行距");
            if (!FinitePositive(lineHeightMm) || !FinitePositive(glyphScale))
                throw new ArgumentException("行高与字号缩放必须是正有限值。");
            double width = page.WidthMm - page.LeftMm - page.RightMm;
            double height = page.HeightMm - page.TopMm - page.BottomMm;
            text = text.Replace("\r\r\n", "\n").Replace("\r\n", "\n").Replace('\r', '\n');
            var rows = BuildRows(text, resolve, letterSpacingMm, glyphScale, width, page.AutoWrap);
            var pages = new List<WritingLayoutPage>();
            var output = new List<WritingStroke>();
            var currentPage = new WritingLayoutPage { PageNumber = 1, Strokes = output };
            pages.Add(currentPage);
            double top = 0;
            foreach (PageRow current in rows)
            {
                // Empty/space-only rows reserve a nominal body box, so intentional blank lines survive.
                double inkHeight = current.Strokes.Count == 0 ? BuiltinGlyphLibrary.BodySizeMm * glyphScale : current.MaxY - current.MinY;
                if (inkHeight > height + 1e-9) throw Overflow(current.LastIndex);
                if (top + inkHeight > height + 1e-9)
                {
                    if (!paginate) throw Overflow(current.LastIndex);
                    output = new List<WritingStroke>();
                    currentPage = new WritingLayoutPage { PageNumber = pages.Count + 1, Strokes = output };
                    pages.Add(currentPage);
                    top = 0;
                }
                currentPage.LineCount++;
                double offsetX = page.Alignment == 1 ? (width - current.Width) / 2 : page.Alignment == 2 ? width - current.Width : 0;
                foreach (WritingStroke stroke in current.Strokes)
                {
                    var points = new List<WritingPoint>();
                    foreach (WritingPoint point in stroke.Points)
                        points.Add(new WritingPoint(page.LeftMm + offsetX + point.X, -page.TopMm - top + point.Y - current.MaxY));
                    output.Add(new WritingStroke(points));
                }
                top += inkHeight + lineSpacingMm;
            }
            int count = 0;
            foreach (WritingLayoutPage item in pages) count += item.Strokes.Count;
            if (count == 0) throw new InvalidOperationException("这段文本没有产生任何笔画。");
            return pages;
        }

        private sealed class PlacedGlyph
        {
            public int Index;
            public BuiltinGlyph Glyph;
            public double Start;
            public double Advance;
        }

        private sealed class PageRow
        {
            public readonly List<WritingStroke> Strokes = new List<WritingStroke>();
            public readonly List<PlacedGlyph> Glyphs = new List<PlacedGlyph>();
            public double Width, MinY = double.PositiveInfinity, MaxY = double.NegativeInfinity;
            public bool Occupied;
            public int LastIndex;
        }

        private static List<PageRow> BuildRows(string text, Func<char, BuiltinGlyph> resolve, double letterSpacingMm, double glyphScale, double width, bool autoWrap)
        {
            var rows = new List<PageRow>();
            var row = new PageRow();
            rows.Add(row);
            var pending = new Queue<int>();
            int cursor = 0;
            int steps = 0;
            while (cursor < text.Length || pending.Count > 0)
            {
                if (++steps > text.Length * 8) throw new InvalidOperationException("换行没有进展。");
                int index = pending.Count > 0 ? pending.Dequeue() : cursor++;
                char symbol = text[index];
                if (symbol == '\n')
                {
                    if (pending.Count > 0) throw new InvalidOperationException("换行不能插进尚未排完的一行。");
                    row = new PageRow(); rows.Add(row); continue;
                }
                if (char.IsControl(symbol) || char.IsSurrogate(symbol))
                    throw new InvalidOperationException("不支持的控制字符或扩展字符，位置 " + (index + 1) + "（制表符请改用空格）。");
                BuiltinGlyph glyph = resolve(symbol);
                if (glyph == null || !FinitePositive(glyph.AdvanceMm)) throw new InvalidOperationException("字形前进距离无效：" + symbol);
                if (glyph.Strokes == null) throw new InvalidOperationException("字形笔画无效：" + symbol);
                double advance = glyph.AdvanceMm * glyphScale;
                double start = row.Occupied ? row.Width + letterSpacingMm : 0;
                if (start + advance > width + 1e-9 && row.Occupied && autoWrap)
                {
                    var indexes = new List<int>();
                    foreach (PlacedGlyph placed in row.Glyphs) indexes.Add(placed.Index);
                    int keep = WritingLineBreak.KeepOnLine(text, indexes, index);
                    if (keep < row.Glyphs.Count)
                    {
                        var moved = new List<int>();
                        for (int k = row.Glyphs.Count - 1; k >= keep; k--) moved.Add(row.Glyphs[k].Index);
                        row.Glyphs.RemoveRange(keep, row.Glyphs.Count - keep);
                        if (row.Glyphs.Count == 0) { row.Width = 0; row.Occupied = false; }
                        else
                        {
                            PlacedGlyph last = row.Glyphs[row.Glyphs.Count - 1];
                            row.Width = last.Start + last.Advance; row.Occupied = true; row.LastIndex = last.Index;
                        }
                        row = new PageRow(); rows.Add(row);
                        moved.Reverse();
                        var again = new Queue<int>();
                        foreach (int movedIndex in moved) again.Enqueue(movedIndex);
                        again.Enqueue(index);
                        while (pending.Count > 0) again.Enqueue(pending.Dequeue());
                        pending = again;
                        continue;
                    }
                    if (WritingLineBreak.CannotStartLine(symbol)) throw Overflow(index);
                    row = new PageRow(); rows.Add(row); start = 0;
                }
                if (start + advance > width + 1e-9) throw Overflow(index);
                row.Glyphs.Add(new PlacedGlyph { Index = index, Glyph = glyph, Start = start, Advance = advance });
                row.Width = start + advance; row.Occupied = true; row.LastIndex = index;
            }
            foreach (PageRow current in rows)
            {
                foreach (PlacedGlyph placed in current.Glyphs)
                {
                    foreach (WritingStroke stroke in placed.Glyph.Strokes)
                    {
                        if (stroke == null || stroke.Points == null || stroke.Points.Count < 2)
                            throw new InvalidOperationException("字形存在无效笔画：" + text[placed.Index]);
                        var points = new List<WritingPoint>();
                        foreach (WritingPoint point in stroke.Points)
                        {
                            double px = placed.Start + point.X * glyphScale;
                            double py = point.Y * glyphScale;
                            if (double.IsNaN(px) || double.IsInfinity(px) || double.IsNaN(py) || double.IsInfinity(py) ||
                                px < -1e-9 || px > width + 1e-9) throw Overflow(placed.Index);
                            current.MinY = Math.Min(current.MinY, py); current.MaxY = Math.Max(current.MaxY, py);
                            points.Add(new WritingPoint(px, py));
                        }
                        current.Strokes.Add(new WritingStroke(points));
                    }
                }
            }
            return rows;
        }

        private static Exception Overflow(int index)
        { return new InvalidOperationException("单页排版越界：第 " + (index + 1) + " 个字符无法放入可写区域。未截断文本，请调整纸张、边距或字号。"); }
        private static bool FinitePositive(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0; }

        private static void RequireNonNegativeFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, value, name + "必须是大于等于 0 的有限值（小数点使用 . ）。");
        }
    }

    /// <summary>
    /// Line-break policy for wrapped writing. A sentence-ending mark is not left alone at the
    /// start of the next line, an opening mark is not left alone at the end of a line, and an
    /// ASCII word moves together when the remainder of the line can hold it. A word wider than
    /// the line still breaks by character, and nothing is clipped.
    /// </summary>
    public static class WritingLineBreak
    {
        public static bool IsWordChar(char symbol)
        {
            return (symbol >= 'A' && symbol <= 'Z') || (symbol >= 'a' && symbol <= 'z') || (symbol >= '0' && symbol <= '9');
        }

        public static bool CannotStartLine(char symbol)
        {
            return "，。！？、；：,.!?;:）】》」』\"'…·—".IndexOf(symbol) >= 0;
        }

        public static bool CannotEndLine(char symbol)
        {
            return "（【《「『\"'(".IndexOf(symbol) >= 0;
        }

        public static int KeepOnLine(string text, IList<int> indexes, int nextIndex)
        {
            int count = indexes == null ? 0 : indexes.Count;
            if (count <= 1 || text == null || nextIndex < 0 || nextIndex >= text.Length) return count;
            if (CannotEndLine(text[indexes[count - 1]])) count--;
            if (count > 1 && CannotStartLine(text[nextIndex])) count--;
            if (count > 0 && IsWordChar(text[nextIndex]) && IsWordChar(text[indexes[count - 1]]))
            {
                int word = count;
                while (word > 0 && IsWordChar(text[indexes[word - 1]])) word--;
                if (word > 0 && word < count) count = word;
            }
            return count < 1 ? indexes.Count : count;
        }
    }
}
