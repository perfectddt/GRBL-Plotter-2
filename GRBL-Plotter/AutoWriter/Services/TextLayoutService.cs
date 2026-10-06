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

        private static void RequireNonNegativeFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, value, name + "必须是大于等于 0 的有限值（小数点使用 . ）。");
        }
    }
}
