/* 2026-10-06 stage 2 (§11.2-1 multi-character layout): the built-in glyph library.
 *
 * Not a font - a small, explicit set of verified glyphs authored on a shared
 * 12 mm em box (stroke body inside 1..11, Y up, baseline shared by every
 * glyph, so laid-out characters are baseline-aligned by construction). An
 * unknown character is rejected loudly; the project rule "never silently
 * drop" applies to text the same way it applies to single-point strokes.
 *
 * Pure data + a resolver, free of WinForms / firmware dependencies, so the
 * offline suites can compile this file standalone.
 */
using System;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    public sealed class BuiltinGlyph
    {
        public char Symbol { get; set; }
        /// <summary>Horizontal cursor advance in mm, including the glyph's own right margin.</summary>
        public double AdvanceMm { get; set; }
        /// <summary>Pen-down strokes in em-box paper coordinates (origin at the em's lower left, Y up).</summary>
        public IList<WritingStroke> Strokes { get; set; }
    }

    public static class BuiltinGlyphLibrary
    {
        /// <summary>Em box edge in mm; every glyph's body is drawn inside 1..11 on both axes.</summary>
        public const double EmSizeMm = 12.0;
        /// <summary>Glyph body height in mm (Y 1..11). The UI's font size is this value times the
        /// layout's glyph scale, so "size 5" means a 5 mm tall character body.</summary>
        public const double BodySizeMm = 10.0;
        /// <summary>Baseline-to-baseline distance for a new line before the operator's line spacing.</summary>
        public const double LineHeightMm = 14.0;
        /// <summary>Space carries no strokes, only an advance.</summary>
        public const double SpaceAdvanceMm = 6.0;

        public static string AvailableSymbols
        { get { return "工 王 一（空格=空移）"; } }

        /// <summary>True when the built-in library can resolve the character itself
        /// (space included). Used by the font-selector chain precheck.</summary>
        public static bool Knows(char symbol)
        {
            switch (symbol)
            {
                case '工':
                case '王':
                case '一':
                case ' ':
                    return true;
                default:
                    return false;
            }
        }

        public static BuiltinGlyph Resolve(char symbol)
        {
            switch (symbol)
            {
                case '工': return Gong();
                case '王': return Wang();
                case '一': return Yi();
                case ' ':
                    return new BuiltinGlyph { Symbol = ' ', AdvanceMm = SpaceAdvanceMm, Strokes = new List<WritingStroke>() };
                default:
                    throw new InvalidOperationException(
                        "内置字形库没有「" + symbol + "」这个字；目前可用：" + AvailableSymbols +
                        "。不认识的字不会静默跳过——请先只输入可用字。");
            }
        }

        private static BuiltinGlyph Gong()
        {
            return new BuiltinGlyph
            {
                Symbol = '工',
                AdvanceMm = EmSizeMm,
                Strokes = new List<WritingStroke>
                {
                    Stroke(1, 11, 11, 11),      // top bar
                    Stroke(6, 1, 6, 11),        // vertical
                    Stroke(1, 1, 11, 1)         // bottom bar
                }
            };
        }

        private static BuiltinGlyph Wang()
        {
            return new BuiltinGlyph
            {
                Symbol = '王',
                AdvanceMm = EmSizeMm,
                Strokes = new List<WritingStroke>
                {
                    Stroke(1, 11, 11, 11),      // top bar
                    Stroke(3, 6, 9, 6),         // middle bar (shorter)
                    Stroke(1, 1, 11, 1),        // bottom bar
                    Stroke(6, 1, 6, 11)         // vertical
                }
            };
        }

        private static BuiltinGlyph Yi()
        {
            return new BuiltinGlyph
            {
                Symbol = '一',
                AdvanceMm = EmSizeMm,
                Strokes = new List<WritingStroke>
                {
                    Stroke(1, 6, 11, 6)         // single bar on the mid line
                }
            };
        }

        private static WritingStroke Stroke(double x1, double y1, double x2, double y2)
        {
            return new WritingStroke(new[] { new WritingPoint(x1, y1), new WritingPoint(x2, y2) });
        }
    }
}
