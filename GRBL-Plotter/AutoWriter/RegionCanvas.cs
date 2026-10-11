using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    internal sealed class RegionHistory
    {
        private readonly List<List<WritingRegion>> undo = new List<List<WritingRegion>>();
        private readonly List<List<WritingRegion>> redo = new List<List<WritingRegion>>();
        internal int UndoCount { get { return undo.Count; } }
        internal int RedoCount { get { return redo.Count; } }
        internal void Remember(IList<WritingRegion> before)
        {
            undo.Add(Clone(before));
            if (undo.Count > 50) undo.RemoveAt(0);
            redo.Clear();
        }
        internal IList<WritingRegion> Undo(IList<WritingRegion> current)
        {
            if (undo.Count == 0) return null;
            redo.Add(Clone(current));
            List<WritingRegion> previous = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            return previous;
        }
        internal IList<WritingRegion> Redo(IList<WritingRegion> current)
        {
            if (redo.Count == 0) return null;
            undo.Add(Clone(current));
            List<WritingRegion> next = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            return next;
        }
        private static List<WritingRegion> Clone(IList<WritingRegion> source)
        {
            var copy = new List<WritingRegion>();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++) copy.Add(source[i] == null ? null : source[i].Clone());
            return copy;
        }
    }

    internal sealed class RegionCanvas : Control
    {
        private enum DragMode { None, Create, Move, Resize }
        internal IList<WritingRegion> Regions = new List<WritingRegion>();
        internal IList<WritingStroke> Strokes;
        internal PageLayoutOptions Sheet;
        internal int Selected = -1;
        internal Action<WritingRegion> RegionCreated;
        internal Action<int> EditRequested;
        internal Action GestureStarted;
        internal Action<bool> GestureFinished;
        internal bool DrawEnabled;
        internal double Zoom = 1;
        internal PointF Pan;
        internal bool SnapToGrid;
        internal bool ShowGrid = true;
        internal int[] Warnings = new int[0];
        // Pen-start picking: while PenStartPickMode is on, a left click reports the page
        // millimetre point (and shows it as a black marker); right click / Esc reports null.
        internal bool PenStartPickMode;
        internal PointF? PenStartMm;
        internal Action<PointF?> PenStartPicked;
        private PointF? dragStart;
        private PointF dragEnd;
        private DragMode dragMode;
        private int resizeHandle = -1;
        private WritingRegion dragOrigin;
        private PointF grabMm;
        private bool panning;
        private Point panStart;
        private PointF panOrigin;
        internal RegionCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(225, 228, 232); Dock = DockStyle.Fill; MinimumSize = new Size(160, 160); }
        private float FitScale()
        {
            return (float)Math.Min(Math.Max(1, ClientSize.Width - 48) / Sheet.WidthMm, Math.Max(1, ClientSize.Height - 48) / Sheet.HeightMm);
        }
        internal RectangleF PageRectangle()
        {
            if (Sheet == null || Sheet.WidthMm <= 0 || Sheet.HeightMm <= 0) return RectangleF.Empty;
            float scale = FitScale() * (float)Zoom;
            float w = (float)Sheet.WidthMm * scale, h = (float)Sheet.HeightMm * scale;
            return new RectangleF((ClientSize.Width - w) / 2f + Pan.X, (ClientSize.Height - h) / 2f + Pan.Y, w, h);
        }
        internal PointF ToPage(PointF pixel, bool clamp)
        {
            var page = PageRectangle(); if (page.IsEmpty) throw new InvalidOperationException("请先设置有效纸张。");
            double x = (pixel.X - page.Left) * Sheet.WidthMm / page.Width, y = (pixel.Y - page.Top) * Sheet.HeightMm / page.Height;
            if (clamp) { x = Math.Max(0, Math.Min(Sheet.WidthMm, x)); y = Math.Max(0, Math.Min(Sheet.HeightMm, y)); }
            return new PointF((float)x, (float)y);
        }
        internal PointF ToPixel(double x, double y) { var page = PageRectangle(); return new PointF(page.Left + (float)(x / Sheet.WidthMm) * page.Width, page.Top + (float)(y / Sheet.HeightMm) * page.Height); }
        private RectangleF BoundsOf(WritingRegion region) { var a = ToPixel(region.X, region.Y); var b = ToPixel(region.X + region.Width, region.Y + region.Height); return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y); }
        internal int HitTest(Point pixel) { if (Sheet == null) return -1; for (int i = Regions.Count - 1; i >= 0; i--) if (BoundsOf(Regions[i]).Contains(pixel)) return i; return -1; }
        private int HitHandle(Point pixel)
        {
            if (Selected < 0 || Selected >= Regions.Count) return -1;
            RectangleF bounds = BoundsOf(Regions[Selected]);
            PointF[] handles = HandlePoints(bounds);
            for (int i = 0; i < handles.Length; i++)
                if (Math.Abs(pixel.X - handles[i].X) <= 6 && Math.Abs(pixel.Y - handles[i].Y) <= 6) return i;
            return -1;
        }
        private static PointF[] HandlePoints(RectangleF bounds)
        {
            float cx = (bounds.Left + bounds.Right) / 2f, cy = (bounds.Top + bounds.Bottom) / 2f;
            return new[] {
                new PointF(bounds.Left, bounds.Top), new PointF(cx, bounds.Top), new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.Right, cy), new PointF(bounds.Right, bounds.Bottom), new PointF(cx, bounds.Bottom),
                new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Left, cy)
            };
        }
        private double Snap(double value)
        {
            if (!SnapToGrid) return value;
            return Math.Round(value);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (PenStartPickMode)
            {
                Focus();
                if (Sheet == null || PageRectangle().IsEmpty)
                {
                    PenStartPickMode = false; Cursor = Cursors.Default;
                    if (PenStartPicked != null) PenStartPicked(null);
                    return;
                }
                if (e.Button == MouseButtons.Left)
                {
                    PointF mm = ToPage(e.Location, true);
                    PenStartMm = mm; PenStartPickMode = false; Cursor = Cursors.Default; Invalidate();
                    if (PenStartPicked != null) PenStartPicked(mm);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    PenStartPickMode = false; Cursor = Cursors.Default; Invalidate();
                    if (PenStartPicked != null) PenStartPicked(null);
                }
                return;
            }
            if (!DrawEnabled || Sheet == null) return;
            Focus();
            if (e.Button == MouseButtons.Right)
            {
                panning = true; panStart = e.Location; panOrigin = Pan; Capture = true; return;
            }
            if (e.Button != MouseButtons.Left) return;
            int handle = HitHandle(e.Location);
            int hit = HitTest(e.Location);
            if (handle >= 0)
            {
                dragMode = DragMode.Resize; resizeHandle = handle; dragOrigin = Regions[Selected].Clone();
                if (GestureStarted != null) GestureStarted();
                Capture = true;
            }
            else if (hit >= 0)
            {
                Selected = hit;
                dragMode = DragMode.Move; dragOrigin = Regions[hit].Clone(); grabMm = ToPage(e.Location, false);
                if (GestureStarted != null) GestureStarted();
                Capture = true;
            }
            else if (PageRectangle().Contains(e.Location))
            {
                Selected = -1; dragMode = DragMode.Create; dragStart = ToPage(e.Location, true); dragEnd = dragStart.Value; Capture = true;
            }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (PenStartPickMode) { Cursor = Cursors.Cross; return; }
            if (panning) { Pan = new PointF(panOrigin.X + e.X - panStart.X, panOrigin.Y + e.Y - panStart.Y); Invalidate(); return; }
            if (dragMode == DragMode.Create && dragStart.HasValue) { dragEnd = ToPage(e.Location, true); Invalidate(); return; }
            if (dragMode == DragMode.Move && Selected >= 0) MoveSelected(ToPage(e.Location, false));
            else if (dragMode == DragMode.Resize && Selected >= 0) ResizeSelected(ToPage(e.Location, false));
            else Cursor = HitHandle(e.Location) >= 0 ? Cursors.SizeAll : HitTest(e.Location) >= 0 ? Cursors.SizeAll : Cursors.Default;
        }
        private void MoveSelected(PointF now)
        {
            double x = Snap(dragOrigin.X + now.X - grabMm.X);
            double y = Snap(dragOrigin.Y + now.Y - grabMm.Y);
            x = Math.Max(0, Math.Min(Sheet.WidthMm - dragOrigin.Width, x));
            y = Math.Max(0, Math.Min(Sheet.HeightMm - dragOrigin.Height, y));
            Regions[Selected].X = x; Regions[Selected].Y = y; Invalidate();
        }
        private void ResizeSelected(PointF now)
        {
            double left = dragOrigin.X, top = dragOrigin.Y, right = dragOrigin.X + dragOrigin.Width, bottom = dragOrigin.Y + dragOrigin.Height;
            double x = Snap(now.X), y = Snap(now.Y);
            bool west = resizeHandle == 0 || resizeHandle == 6 || resizeHandle == 7;
            bool east = resizeHandle == 2 || resizeHandle == 3 || resizeHandle == 4;
            bool north = resizeHandle == 0 || resizeHandle == 1 || resizeHandle == 2;
            bool south = resizeHandle == 4 || resizeHandle == 5 || resizeHandle == 6;
            if (west) left = x; if (east) right = x; if (north) top = y; if (south) bottom = y;
            double minW = Math.Max(3, dragOrigin.Left + dragOrigin.Right + 0.5);
            double minH = Math.Max(3, dragOrigin.Top + dragOrigin.Bottom + 0.5);
            if (right - left < minW) { if (west) left = right - minW; else right = left + minW; }
            if (bottom - top < minH) { if (north) top = bottom - minH; else bottom = top + minH; }
            left = Math.Max(0, left); top = Math.Max(0, top);
            right = Math.Min(Sheet.WidthMm, right); bottom = Math.Min(Sheet.HeightMm, bottom);
            if (right - left < minW) { if (west) left = Math.Max(0, right - minW); else right = Math.Min(Sheet.WidthMm, left + minW); }
            if (bottom - top < minH) { if (north) top = Math.Max(0, bottom - minH); else bottom = Math.Min(Sheet.HeightMm, top + minH); }
            Regions[Selected].X = left; Regions[Selected].Y = top;
            Regions[Selected].Width = Math.Max(minW, right - left); Regions[Selected].Height = Math.Max(minH, bottom - top);
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right && panning) { panning = false; Capture = false; return; }
            if (e.Button != MouseButtons.Left) return;
            if (dragMode == DragMode.Create && dragStart.HasValue)
            {
                var start = dragStart.Value; var end = ToPage(e.Location, true); dragStart = null; dragMode = DragMode.None; Capture = false;
                double x = Math.Min(start.X, end.X), y = Math.Min(start.Y, end.Y), w = Math.Abs(start.X - end.X), h = Math.Abs(start.Y - end.Y);
                if (SnapToGrid) { x = Snap(x); y = Snap(y); w = Math.Max(3, Snap(w)); h = Math.Max(3, Snap(h)); }
                if (w >= 3 && h >= 3 && RegionCreated != null) RegionCreated(new WritingRegion { X = x, Y = y, Width = w, Height = h, Text = "", FontSize = 6, LetterGap = 1, LineGap = 1 });
                Invalidate(); return;
            }
            if (dragMode == DragMode.Move || dragMode == DragMode.Resize)
            {
                bool changed = Selected >= 0 && dragOrigin != null && (Math.Abs(Regions[Selected].X - dragOrigin.X) > 0.05 || Math.Abs(Regions[Selected].Y - dragOrigin.Y) > 0.05 || Math.Abs(Regions[Selected].Width - dragOrigin.Width) > 0.05 || Math.Abs(Regions[Selected].Height - dragOrigin.Height) > 0.05);
                if (!changed && Selected >= 0 && dragOrigin != null) { Regions[Selected].X = dragOrigin.X; Regions[Selected].Y = dragOrigin.Y; Regions[Selected].Width = dragOrigin.Width; Regions[Selected].Height = dragOrigin.Height; }
                dragMode = DragMode.None; Capture = false;
                if (GestureFinished != null) GestureFinished(changed);
            }
            Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); Selected = HitTest(e.Location); if (DrawEnabled && Selected >= 0 && EditRequested != null) EditRequested(Selected); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Sheet == null || PageRectangle().IsEmpty) return;
            PointF before = ToPage(e.Location, false);
            Zoom = Math.Max(0.25, Math.Min(8, Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)));
            float scale = FitScale() * (float)Zoom;
            float w = (float)Sheet.WidthMm * scale, h = (float)Sheet.HeightMm * scale;
            float left = e.X - (float)(before.X / Sheet.WidthMm) * w;
            float top = e.Y - (float)(before.Y / Sheet.HeightMm) * h;
            Pan = new PointF(left - (ClientSize.Width - w) / 2f, top - (ClientSize.Height - h) / 2f);
            Invalidate();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (PenStartPickMode && e.KeyCode == Keys.Escape)
            {
                PenStartPickMode = false; Cursor = Cursors.Default; Invalidate();
                if (PenStartPicked != null) PenStartPicked(null);
                e.Handled = true; return;
            }
            if (!e.Control) return;
            if (e.KeyCode == Keys.Z && UndoRequested != null) { UndoRequested(); e.Handled = true; }
            if (e.KeyCode == Keys.Y && RedoRequested != null) { RedoRequested(); e.Handled = true; }
        }
        internal Action UndoRequested;
        internal Action RedoRequested;
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var page = PageRectangle(); if (page.IsEmpty) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillRectangle(Brushes.White, page); e.Graphics.DrawRectangle(Pens.Gray, page.X, page.Y, page.Width, page.Height);
            DrawGrid(e.Graphics);
            DrawRuler(e.Graphics, page);
            using (var margin = new Pen(Color.LightGray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            {
                var a = ToPixel(Sheet.LeftMm, Sheet.TopMm); var b = ToPixel(Sheet.WidthMm - Sheet.RightMm, Sheet.HeightMm - Sheet.BottomMm);
                e.Graphics.DrawRectangle(margin, a.X, a.Y, Math.Max(0, b.X - a.X), Math.Max(0, b.Y - a.Y));
            }
            if (Strokes != null) using (var pen = new Pen(Color.DarkRed, 1)) foreach (var stroke in Strokes) for (int i = 1; i < stroke.Points.Count; i++) e.Graphics.DrawLine(pen, ToPixel(stroke.Points[i - 1].X, -stroke.Points[i - 1].Y), ToPixel(stroke.Points[i].X, -stroke.Points[i].Y));
            for (int i = 0; i < Regions.Count; i++)
            {
                var bounds = BoundsOf(Regions[i]);
                bool warn = Array.IndexOf(Warnings, i) >= 0;
                using (var pen = new Pen(warn ? Color.Firebrick : i == Selected ? Color.OrangeRed : Color.RoyalBlue, i == Selected || warn ? 2 : 1))
                { if (warn) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
                string label = (i + 1).ToString(); var size = e.Graphics.MeasureString(label, Font); float labelY = bounds.Y - size.Height;
                e.Graphics.FillRectangle(Brushes.White, bounds.X, labelY, size.Width, size.Height); e.Graphics.DrawString(label, Font, Brushes.RoyalBlue, bounds.X, labelY);
                if (i == Selected) foreach (PointF handle in HandlePoints(bounds)) e.Graphics.FillRectangle(Brushes.OrangeRed, handle.X - 3, handle.Y - 3, 6, 6);
            }
            e.Graphics.DrawString("蓝框不写  红线是本页笔迹", Font, Brushes.DimGray, page.Left + 4, page.Bottom - Font.Height - 4);
            if (dragStart.HasValue) { var a = ToPixel(dragStart.Value.X, dragStart.Value.Y); var b = ToPixel(dragEnd.X, dragEnd.Y); e.Graphics.DrawRectangle(Pens.OrangeRed, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)); }
            if (PenStartMm.HasValue)
            {
                PointF m = ToPixel(PenStartMm.Value.X, PenStartMm.Value.Y);
                e.Graphics.DrawLine(Pens.Black, m.X - 7, m.Y, m.X + 7, m.Y);
                e.Graphics.DrawLine(Pens.Black, m.X, m.Y - 7, m.X, m.Y + 7);
                e.Graphics.FillEllipse(Brushes.Black, m.X - 3, m.Y - 3, 6, 6);
                e.Graphics.DrawString("笔起点", Font, Brushes.Black, m.X + 7, m.Y + 3);
            }
        }
        private void DrawGrid(Graphics graphics)
        {
            if (!ShowGrid) return;
            double step = Sheet.WidthMm > 400 ? 50 : 10;
            using (var pen = new Pen(Color.Gainsboro))
            {
                for (double mm = 0; mm <= Sheet.WidthMm + 0.01; mm += step) { var a = ToPixel(mm, 0); var b = ToPixel(mm, Sheet.HeightMm); graphics.DrawLine(pen, a, b); }
                for (double mm = 0; mm <= Sheet.HeightMm + 0.01; mm += step) { var a = ToPixel(0, mm); var b = ToPixel(Sheet.WidthMm, mm); graphics.DrawLine(pen, a, b); }
            }
        }
        private void DrawRuler(Graphics graphics, RectangleF page)
        {
            double step = Sheet.WidthMm > 400 ? 50 : 10;
            using (var pen = new Pen(Color.Gray))
            {
                for (double mm = 0; mm <= Sheet.WidthMm + 0.01; mm += step)
                {
                    var point = ToPixel(mm, 0);
                    graphics.DrawLine(pen, point.X, page.Top - 10, point.X, page.Top);
                    graphics.DrawString(mm.ToString("0"), Font, Brushes.Gray, point.X + 1, page.Top - 22);
                }
                for (double mm = 0; mm <= Sheet.HeightMm + 0.01; mm += step)
                {
                    var point = ToPixel(0, mm);
                    graphics.DrawLine(pen, page.Left - 10, point.Y, page.Left, point.Y);
                    graphics.DrawString(mm.ToString("0"), Font, Brushes.Gray, page.Left - 28, point.Y + 1);
                }
            }
        }
    }
    internal sealed class RegionEditorForm : Form
    {
        internal WritingRegion Value;
        private readonly Dictionary<string, NumericUpDown> numbers = new Dictionary<string, NumericUpDown>();
        private readonly TextBox text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private readonly ComboBox pageBehavior = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly ComboBox alignment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        internal RegionEditorForm(WritingRegion source, PageLayoutOptions sheet, int number)
        {
            Value = source.Clone(); Text = "区域 " + number + "（左上角坐标 mm）"; Font = new Font("Microsoft YaHei UI", 9); Size = new Size(600, 620); MinimumSize = new Size(500, 500); StartPosition = FormStartPosition.CenterParent;
            text.Text = WritingEditorText.Normalize(source.Text ?? "");
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(6) };
            string[] labels = { "X", "Y", "宽", "高", "内左", "内右", "内上", "内下", "字号", "字距", "行间空隙" };
            double[] values = { source.X, source.Y, source.Width, source.Height, source.Left, source.Right, source.Top, source.Bottom, source.FontSize, source.LetterGap, source.LineGap };
            for (int i = 0; i < labels.Length; i++) { int row = i / 2, col = (i % 2) * 2; grid.Controls.Add(new Label { Text = labels[i] + " mm", AutoSize = true, Anchor = AnchorStyles.Left }, col, row); var box = new NumericUpDown { DecimalPlaces = 2, Maximum = 100000, Width = 100, Value = (decimal)Math.Min(100000, Math.Max(0, values[i])) }; numbers.Add(labels[i], box); grid.Controls.Add(box, col + 1, row); }
            alignment.Items.AddRange(new object[] { "左对齐", "居中", "右对齐" }); alignment.SelectedIndex = Math.Max(0, Math.Min(2, source.Alignment)); grid.Controls.Add(new Label { Text = "每行对齐", AutoSize = true }, 2, 5); grid.Controls.Add(alignment, 3, 5);
            pageBehavior.Items.AddRange(new object[] { "自动续到下一页", "每页重复", "仅首页", "仅末页" }); pageBehavior.SelectedIndex = source.PageBehavior; grid.Controls.Add(new Label { Text = "分页方式", AutoSize = true }, 0, 6); grid.Controls.Add(pageBehavior, 1, 6); grid.SetColumnSpan(pageBehavior, 3);
            var fontPath = new TextBox { Text = source.FontPath ?? "", Width = 280 };
            var browse = new Button { Text = "选择区域字体", AutoSize = true };
            browse.Click += (sender, args) => { using (var dialog = new OpenFileDialog { Filter = "字库包 (*.fontpack.json)|*.fontpack.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) fontPath.Text = dialog.FileName; };
            grid.Controls.Add(new Label { Text = "区域字体（留空=当前字体）", AutoSize = true }, 0, 7);
            grid.Controls.Add(fontPath, 1, 7); grid.SetColumnSpan(fontPath, 2); grid.Controls.Add(browse, 3, 7);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "确定", AutoSize = true }; var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true }; buttons.Controls.Add(cancel); buttons.Controls.Add(ok); CancelButton = cancel;
            ok.Click += (sender, args) =>
            {
                var region = source.Clone(); region.Text = WritingEditorText.Normalize(text.Text); region.X = N("X"); region.Y = N("Y"); region.Width = N("宽"); region.Height = N("高"); region.Left = N("内左"); region.Right = N("内右"); region.Top = N("内上"); region.Bottom = N("内下"); region.FontSize = N("字号"); region.LetterGap = N("字距"); region.LineGap = N("行间空隙"); region.Alignment = alignment.SelectedIndex; region.PageBehavior = pageBehavior.SelectedIndex; region.FontPath = fontPath.Text.Trim(); if (region.FontPath.Length > 0 && !System.IO.File.Exists(region.FontPath)) { MessageBox.Show(this, "区域字体文件不存在。留空则使用当前字体。", "区域字体"); return; }
                try { region.Validate(sheet); Value = region; DialogResult = DialogResult.OK; Close(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "区域参数"); }
            };
            Controls.Add(text); Controls.Add(grid); Controls.Add(buttons);
        }
        private double N(string key) { return (double)numbers[key].Value; }
    }
}
