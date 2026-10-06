/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.

    Copyright (C) 2015-2026 Sven Hasemann contact: svenhb@web.de

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
/* ViewTransform
 * Display-only orientation of the 2D view: horizontal mirror, vertical mirror and quarter turns.
 *
 * The operator uses this to line the on-screen picture up with the sheet lying in front of the
 * machine. It must never change what is written: the G-code text, the sent program and every
 * stored coordinate stay exactly as they are, only the picture moves. That is why this class is
 * deliberately kept apart from VisuGCode and from the G-code transform menu, which really does
 * rewrite the code.
 *
 * The transform is applied around the centre of the drawing bounds, so mirroring or turning the
 * view does not push the graphic out of the window.
 *
 * Deliberately free of WinForms so the matrix maths can be verified offline - see
 * scripts/test-stage1-viewtransform.ps1, which drives the real GDI+ Matrix and checks which way
 * each operation actually turns the picture on screen.
*/

using System.Drawing;
using System.Drawing.Drawing2D;

namespace GrblPlotter
{
    public sealed class ViewTransform
    {
        /// <summary>Mirror about the vertical centre line (left and right swap).</summary>
        public bool MirrorX { get; private set; }

        /// <summary>Mirror about the horizontal centre line (top and bottom swap).</summary>
        public bool MirrorY { get; private set; }

        /// <summary>Quarter turns, 1 = one clockwise turn as seen on screen. Range 0..3.</summary>
        public int QuarterTurns { get; private set; }

        /// <summary>True when the view is shown exactly as the G-code describes it.</summary>
        public bool IsIdentity
        {
            get { return !MirrorX && !MirrorY && (QuarterTurns % 4) == 0; }
        }

        /// <summary>
        /// True for the odd quarter turns, where the visible X and Y extents swap. The view
        /// scaling has to follow, otherwise a turned graphic is scaled for the wrong aspect.
        /// </summary>
        public bool SwapsAxes
        {
            get { return (QuarterTurns % 4) == 1 || (QuarterTurns % 4) == 3; }
        }

        public void ToggleMirrorX() { MirrorX = !MirrorX; }

        public void ToggleMirrorY() { MirrorY = !MirrorY; }

        public void RotateClockwise() { QuarterTurns = (QuarterTurns + 1) % 4; }

        public void RotateCounterClockwise() { QuarterTurns = (QuarterTurns + 3) % 4; }

        public void Reset()
        {
            MirrorX = false;
            MirrorY = false;
            QuarterTurns = 0;
        }

        /// <summary>Short human readable state, for menus and the info box.</summary>
        public string Describe()
        {
            if (IsIdentity) return "normal";
            string text = "";
            if (QuarterTurns % 4 != 0) text += "rotate " + (QuarterTurns * 90) + " deg CW";
            if (MirrorX) text += (text.Length > 0 ? ", " : "") + "mirror X";
            if (MirrorY) text += (text.Length > 0 ? ", " : "") + "mirror Y";
            return text;
        }

        /// <summary>
        /// Builds the display matrix for one paint pass, turning/mirroring world coordinates about
        /// the given centre. The world of this class is the picture box coordinate system before
        /// the view applies its own scaling: X to the right and Y upwards.
        ///
        /// Sign convention, verifiable in the offline test: inside a Y-up world GDI+ Rotate(+90)
        /// swings "right" towards "up", which the view's own Y flip then shows as a
        /// counter-clockwise turn, so a clockwise turn passes a negative angle. The mirror factors
        /// are plain axis factors and carry no such twist.
        ///
        /// An identity state returns an untouched matrix, so the caller can apply it unconditionally
        /// without changing any existing behaviour.
        /// </summary>
        public Matrix BuildMatrix(float centerX, float centerY)
        {
            Matrix matrix = new Matrix();
            if (IsIdentity) return matrix;

            matrix.Translate(-centerX, -centerY);
            if (QuarterTurns % 4 != 0)
                matrix.Rotate(-QuarterTurns * 90f, MatrixOrder.Append);
            if (MirrorX || MirrorY)
                matrix.Scale(MirrorX ? -1f : 1f, MirrorY ? -1f : 1f, MatrixOrder.Append);
            matrix.Translate(centerX, centerY, MatrixOrder.Append);
            return matrix;
        }
    }
}
