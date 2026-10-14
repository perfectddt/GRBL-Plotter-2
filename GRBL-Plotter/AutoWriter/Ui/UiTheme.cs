/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.

    AutoWriter modern shell (2026-10-12): central color / font constants and small styling
    helpers shared by the modern main-window layout and the dialog skin. iOS-like light
    palette. Purely cosmetic - no machine, streaming or coordinate logic lives here.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
*/
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace GrblPlotter.AutoWriter.Ui
{
    internal enum UiButtonKind { Normal, Primary, Success, Warning, Danger }

    /// <summary>iOS-like grouped surface: rounded white card with a hairline border.</summary>
    internal sealed class RoundedPanel : Panel
    {
        private readonly int radius;

        public RoundedPanel(int radius)
        {
            this.radius = radius;
            BackColor = UiTheme.Card;
            BorderStyle = BorderStyle.None;
            Resize += (s, a) => ApplyRegion();
            Paint += PaintBorder;
        }

        private GraphicsPath BuildPath()
        {
            var path = new GraphicsPath();
            Rectangle rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (rect.Width < 2 || rect.Height < 2) { path.AddRectangle(new Rectangle(0, 0, 1, 1)); return path; }
            int r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyRegion()
        {
            try { using (GraphicsPath path = BuildPath()) Region = new Region(path); }
            catch (Exception) { Region = null; }
        }

        private void PaintBorder(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = BuildPath())
                using (Pen pen = new Pen(UiTheme.CardBorder))
                    e.Graphics.DrawPath(pen, path);
            }
            catch (Exception) { /* cosmetic only */ }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRegion();
        }
    }

    /// <summary>
    /// Visual constants for the AutoWriter modern UI (iOS-like light theme). Reference layout:
    /// doc/写字功能图片 with the three-column arrangement from doc/UI重设计-规划-20261011.md.
    /// </summary>
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(0xF2, 0xF2, 0xF7);   // systemGroupedBackground
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(0xE5, 0xE5, 0xEA);   // separator
        public static readonly Color Text = Color.FromArgb(0x1C, 0x1C, 0x1E);         // label
        public static readonly Color SubText = Color.FromArgb(0x8E, 0x8E, 0x93);      // secondaryLabel
        public static readonly Color Accent = Color.FromArgb(0x00, 0x7A, 0xFF);       // systemBlue
        public static readonly Color AccentDark = Color.FromArgb(0x00, 0x62, 0xCC);
        public static readonly Color AccentSoft = Color.FromArgb(0xE8, 0xF2, 0xFF);
        public static readonly Color Green = Color.FromArgb(0x34, 0xC7, 0x59);        // systemGreen
        public static readonly Color GreenDark = Color.FromArgb(0x28, 0xA4, 0x45);
        public static readonly Color YellowSoft = Color.FromArgb(0xFF, 0xF3, 0xE0);
        public static readonly Color YellowText = Color.FromArgb(0x9A, 0x64, 0x00);
        public static readonly Color YellowBorder = Color.FromArgb(0xFF, 0xD5, 0x8A);
        public static readonly Color Red = Color.FromArgb(0xFF, 0x3B, 0x30);          // systemRed
        public static readonly Color RedSoft = Color.FromArgb(0xFF, 0xEB, 0xEA);
        public static readonly Color RedText = Color.FromArgb(0xC0, 0x27, 0x1F);
        public static readonly Color RedBorder = Color.FromArgb(0xFF, 0xC5, 0xC2);
        public static readonly Color FillGray = Color.FromArgb(0xE9, 0xE9, 0xEB);     // iOS tertiary fill (secondary buttons)

        // ---- shared metrics -------------------------------------------------------------
        // Every control inside a card row uses the same height, and UiGroup centres each one
        // inside a row of fixed height. That is what keeps labels, inputs and buttons on one
        // baseline; tuning per-control margins by hand is what made the old band look crooked.
        public const int ControlHeight = 26;   // buttons, text boxes, combo boxes, numeric fields
        public const int RowHeight = 28;       // one line inside a card
        public const int RowGap = 4;           // vertical gap between rows
        public const int FieldGap = 8;         // horizontal gap between two controls
        public const int LabelGap = 4;         // gap between a label and the field it belongs to
        public const int CardPad = 10;         // inner padding of a card body
        public const int CardGap = 10;         // gap between two cards

        /// <summary>Microsoft YaHei UI with graceful fallback when the font is missing.</summary>
        public static Font SafeFont(float sizePt, FontStyle style = FontStyle.Regular)
        {
            string[] families = { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑" };
            foreach (string family in families)
            {
                try { return new Font(family, sizePt, style, GraphicsUnit.Point); }
                catch (ArgumentException) { /* try next family */ }
            }
            return new Font(SystemFonts.DefaultFont.FontFamily, sizePt, style, GraphicsUnit.Point);
        }

        public static void StyleFlatButton(Button button, UiButtonKind kind = UiButtonKind.Normal)
        {
            if (button == null) return;
            button.Tag = kind;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.ForeColor = Text;
            button.BackColor = FillGray;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xDC, 0xDC, 0xE0);
            switch (kind)
            {
                case UiButtonKind.Primary:
                    button.BackColor = Accent; button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = AccentDark; break;
                case UiButtonKind.Success:
                    button.BackColor = Green; button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = GreenDark; break;
                case UiButtonKind.Warning:
                    button.BackColor = YellowSoft; button.ForeColor = YellowText; break;
                case UiButtonKind.Danger:
                    button.BackColor = RedSoft; button.ForeColor = RedText; break;
            }
            EventHandler resizer = (s, a) => ApplyRoundedRegion(button, 8);
            button.Resize += resizer;
            button.HandleCreated += resizer;
            ApplyRoundedRegion(button, 8);
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            try
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                int r = Math.Max(0, Math.Min(radius, Math.Min(control.Width, control.Height) / 2));
                var path = new GraphicsPath();
                Rectangle rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
                path.AddArc(rect.X, rect.Y, r, r, 180, 90);
                path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
                path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
                path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
                path.CloseFigure();
                control.Region = new Region(path);
            }
            catch (Exception) { /* cosmetic only */ }
        }

        /// <summary>
        /// iOS grouped card: a small gray caption above a rounded white surface. The returned
        /// container docks to the top of its column; the caption comes back as
        /// <paramref name="header"/> so collapsible cards can hook clicks onto it.
        /// </summary>
        public static Panel MakeCard(string title, out Panel body)
        {
            Label header;
            return MakeCard(title, out body, out header);
        }

        public static Panel MakeCard(string title, out Panel body, out Label header)
        {
            Panel wrapper = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 10)
            };
            Label caption = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "  " + title,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SubText,
                Font = SafeFont(8.5f),
                BackColor = Color.Transparent
            };
            RoundedPanel card = new RoundedPanel(10)
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            body = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Card,
                Padding = new Padding(10, 10, 10, 10)
            };
            card.Controls.Add(body);
            wrapper.Controls.Add(card);
            wrapper.Controls.Add(caption);
            // caption keeps the higher child index so it docks FIRST at the top edge;
            // the card then fills below it. (Lower index docks last in WinForms.)
            header = caption;
            return wrapper;
        }

        /// <summary>Stack a control at the bottom of a card body (Dock=Top, appended below previous).</summary>
        public static void StackBody(Panel body, Control control, int topMargin = 0)
        {
            if (control == null) return;
            control.Dock = DockStyle.Top;
            if (topMargin > 0) control.Margin = new Padding(0, topMargin, 0, 0);
            body.Controls.Add(control);
            body.Controls.SetChildIndex(control, 0);  // lowest index docks last -> newest ends up at the bottom
        }

        /// <summary>
        /// Caption in front of a field. AutoSize is deliberately OFF and the text is
        /// middle-aligned: with AutoSize the label box is shorter than the input beside it, so
        /// centring the box leaves the text a few pixels too high. Same height + MiddleLeft makes
        /// the caption and the input share one baseline.
        /// </summary>
        public static Label FieldLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = false,
                ForeColor = Text,
                Font = SafeFont(9f),
                Margin = new Padding(0),
                Padding = new Padding(0),
                TextAlign = ContentAlignment.MiddleLeft,
                Height = ControlHeight
            };
            using (Graphics graphics = label.CreateGraphics())
                label.Width = (int)Math.Ceiling(graphics.MeasureString(text, label.Font).Width) + 2;
            return label;
        }

        /// <summary>Compact single-line input; UiGroup centres it vertically.</summary>
        public static TextBox Field(string text, int width)
        {
            return new TextBox { Text = text, Font = SafeFont(9f), Width = width, Height = ControlHeight, Margin = new Padding(0, 0, 0, 0) };
        }

        /// <summary>
        /// iOS-like push button. It is only created here; UiGroup places it so that every control
        /// of a row shares one vertical centre.
        /// </summary>
        public static Button MakeButton(string text, Action action, UiButtonKind kind = UiButtonKind.Normal, int height = ControlHeight)
        {
            var button = new Button { Text = text, Font = SafeFont(9f), AutoSize = true, Height = height, Margin = new Padding(0, 0, 0, 0) };
            button.Click += (sender, args) => action();
            StyleFlatButton(button, kind);
            return button;
        }

        /// <summary>Create a styled button and append it to a plain toolbar panel.</summary>
        public static Button AddButton(Panel panel, string text, Action action,
                                       UiButtonKind kind = UiButtonKind.Normal, int height = ControlHeight)
        {
            Button button = MakeButton(text, action, kind, height);
            panel.Controls.Add(button);
            return button;
        }

        /// <summary>Checkbox sized like the other row controls so its text lands on the same baseline.</summary>
        public static CheckBox MakeCheck(string text, bool checkedState, EventHandler changed = null)
        {
            var box = new CheckBox
            {
                Text = text,
                Checked = checkedState,
                AutoSize = false,
                ForeColor = Text,
                Margin = new Padding(0, 0, 0, 0),
                CheckAlign = ContentAlignment.MiddleLeft,
                Font = SafeFont(9f),
                TextAlign = ContentAlignment.MiddleLeft
            };
            using (Graphics graphics = box.CreateGraphics())
            {
                SizeF size = graphics.MeasureString(text, box.Font);
                box.Width = (int)Math.Ceiling(size.Width) + 24;   // 24 = check glyph + inner padding
            }
            box.Height = ControlHeight;
            if (changed != null) box.CheckedChanged += changed;
            return box;
        }

        /// <summary>Small gray hint line at the bottom of a card.</summary>
        public static Label AddNote(Panel body, string text)
        {
            var note = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SubText,
                Font = SafeFont(8.5f),
                BackColor = Card,
                Margin = new Padding(10, 2, 4, 0),
                Dock = DockStyle.Top
            };
            body.Controls.Add(note);
            body.SizeChanged += (sender, args) =>
                note.MaximumSize = new Size(Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal), 0);
            body.Controls.SetChildIndex(note, 0);  // append at the bottom
            return note;
        }

        /// <summary>Enable double buffering on controls that expose the property (Panel and friends).</summary>
        public static void EnableDoubleBuffer(Control control)
        {
            if (control == null) return;
            try
            {
                PropertyInfo property = control.GetType().GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null && property.PropertyType == typeof(bool))
                    property.SetValue(control, true, null);
            }
            catch (Exception) { /* cosmetic only */ }
        }

        /// <summary>
        /// Gentle dialog skin: form background + flat buttons + friendlier PropertyGrid colors.
        /// Font replacement is opt-in because dense localized designer forms may clip when the
        /// face changes. Cosmetic only.
        /// </summary>
        public static void ApplyDialogSkin(Form form, bool changeFont)
        {
            if (form == null) return;
            try
            {
                form.BackColor = Background;
                if (changeFont) form.Font = SafeFont(9f);
                foreach (Control control in AllControls(form))
                {
                    if (control is Button button && !(button.Tag is UiButtonKind))
                        StyleFlatButton(button, UiButtonKind.Normal);
                    else if (control is PropertyGrid grid)
                        StylePropertyGrid(grid);
                }
            }
            catch (Exception) { /* skinning must never break a dialog */ }
        }

        public static Color StatusFill(Color color)
        {
            if (!MainForm.ModernUiConfig.ModernEnabled) return color;
            if (color.R > 180 && color.B > 180 && color.G < 120) return AccentSoft;
            if (color.G > 160 && color.R < 160 && color.B < 160) return Color.FromArgb(225, 245, 231);
            if (color.R > 180 && color.G < 150 && color.B < 150) return Color.FromArgb(255, 230, 230);
            if (color.R > 180 && color.G > 160 && color.B < 170) return Color.FromArgb(255, 245, 216);
            return color;
        }

        public static Color JogFill(int level, int levels)
        {
            return GrblPlotter.Helper.Colors.ColorFromHSV(240, (double)(levels - level) / levels, 1);
        }

        public static void ApplyControlSkin(Control root)
        {
            root.BackColor = Card;
            foreach (Control control in AllControls(root))
            {
                if (control is Button button && !(button.Tag is UiButtonKind))
                {
                    StyleFlatButton(button);
                    button.Font = SafeFont(8.5f);
                    button.AutoEllipsis = true;
                }
                else if (control is GroupBox group)
                {
                    group.BackColor = Card;
                    group.ForeColor = SubText;
                }
                else if (control is TextBoxBase text)
                {
                    text.BackColor = Card;
                    text.ForeColor = Text;
                }
                else if (control is Label label)
                {
                    label.ForeColor = Text;
                    label.BackColor = StatusFill(label.BackColor);
                    label.BackColorChanged += (sender, args) =>
                    {
                        Color fill = StatusFill(label.BackColor);
                        if (fill != label.BackColor) label.BackColor = fill;
                    };
                }
            }
        }

        private static void StylePropertyGrid(PropertyGrid grid)
        {
            try
            {
                grid.ViewBackColor = Card;
                grid.ViewForeColor = Text;
                grid.LineColor = CardBorder;
                grid.CategorySplitterColor = CardBorder;
                grid.BackColor = Background;
                grid.ForeColor = Text;
                EnableDoubleBuffer(grid);
            }
            catch (Exception) { /* cosmetic only */ }
        }

        public static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in AllControls(child))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// One line of controls inside a card. Controls are placed left to right and wrapped when the
    /// card is too narrow; each control is centred on the line, which is what makes the whole card
    /// line up - the layout is computed here instead of being left to per-control margins.
    /// </summary>
    internal sealed class UiRow : Panel
    {
        private readonly List<int> gaps = new List<int>();
        private readonly int lineHeight;
        private bool arranging;
        public int EqualColumns { get; set; }

        public UiRow(int height)
        {
            lineHeight = height;
            Dock = DockStyle.Top;
            Height = height;
            BackColor = UiTheme.Card;
            Margin = new Padding(0);
            Padding = new Padding(0);          // the card body supplies the left inset
        }

        /// <summary>Add a control; <paramref name="gapBefore"/> is the gap to the previous one.</summary>
        public void Place(Control control, int gapBefore)
        {
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(control);
            gaps.Add(gapBefore);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (arranging) return;
            arranging = true;
            try
            {
                base.OnLayout(levent);
                var items = new List<Control>();
                bool buttonsOnly = true, checksOnly = true;
                foreach (Control child in Controls)
                {
                    if (child.Dock != DockStyle.None) continue;
                    items.Add(child);
                    buttonsOnly &= child is Button;
                    checksOnly &= child is CheckBox;
                }
                int width = ClientSize.Width;
                if (Controls.Count == 0) { Height = 0; return; }
                if (items.Count > 0 && width > 0 && (buttonsOnly || checksOnly || EqualColumns > 0))
                {
                    // All action rows use the same two-column grid, including wrapped rows.
                    int columns = EqualColumns > 0 ? EqualColumns : Math.Min(2, items.Count);
                    int minimum = 0;
                    foreach (Control child in items)
                        minimum = Math.Max(minimum, TextRenderer.MeasureText(child.Text, child.Font).Width + 24);
                    if (EqualColumns == 0 && width < columns * minimum + (columns - 1) * UiTheme.FieldGap) columns = 1;
                    int cellWidth = (width - (columns - 1) * UiTheme.FieldGap) / columns;
                    for (int i = 0; i < items.Count; i++)
                    {
                        Control child = items[i];
                        child.AutoSize = false;
                        child.Height = UiTheme.ControlHeight;
                        int column = i % columns;
                        int left = column * (cellWidth + UiTheme.FieldGap);
                        child.SetBounds(left, (i / columns) * (lineHeight + UiTheme.RowGap)
                            + (lineHeight - child.Height) / 2,
                            column == columns - 1 ? width - left : cellWidth, child.Height);
                    }
                    int rows = (items.Count + columns - 1) / columns;
                    Height = rows * lineHeight + (rows - 1) * UiTheme.RowGap;
                    return;
                }
            // Child coordinates are relative to DisplayRectangle, so the left inset comes from
            // the parent card - start at 0 and only use the explicit gaps between controls.
            int x = 0;
            int y = 0;
            int available = ClientSize.Width;
            for (int i = 0; i < Controls.Count; i++)
            {
                Control child = Controls[i];
                if (child.Dock != DockStyle.None) continue;   // AddWide fills the whole row
                int gap = i < gaps.Count ? gaps[i] : UiTheme.FieldGap;
                if (i > 0)
                {
                    x += gap;
                    if (available > 0 && x + child.Width > available)
                    {
                        x = 0;
                        y += lineHeight + 2;
                    }
                }
                child.Location = new Point(x, y + (lineHeight - child.Height) / 2);
                x += child.Width;
            }
            int wanted = y + lineHeight + Padding.Vertical;
            if (Height != wanted) Height = wanted;   // one extra layout pass settles it
            }
            finally { arranging = false; }
        }
    }

    /// <summary>
    /// A titled card that stacks rows of controls. Introduced (2026-10-12) to re-group the dense
    /// AutoWriter setup tabs into operator-ordered sections: the same controls with the same
    /// handlers, arranged by workflow step instead of one flat wrapping band.
    /// </summary>
    internal sealed class UiGroup
    {
        private readonly Panel body;
        private UiRow row;

        public Control Card { get; private set; }
        public Label Header { get; private set; }
        /// <summary>The row new controls are appended to; call <see cref="NewRow"/> to start another line.</summary>
        public UiRow Row { get { return row; } }

        public UiGroup(string title)
        {
            Card = UiTheme.MakeCard(title, out body, out Label header);
            Header = header;
            NewRow();
        }

        /// <summary>Start another row below the current one and make it current.</summary>
        public UiRow NewRow(int height = UiTheme.RowHeight, int topMargin = UiTheme.RowGap)
        {
            row = new UiRow(height) { Margin = new Padding(0, topMargin, 0, 0) };
            UiTheme.StackBody(body, row);
            return row;
        }

        /// <summary>Place a control on the current row, vertically centred.</summary>
        public T Add<T>(T control, int gapBefore = UiTheme.FieldGap) where T : Control
        {
            row.Place(control, gapBefore);
            return control;
        }

        /// <summary>Caption for the field that follows it - starts a new group on the row.</summary>
        public Label AddLabel(string text)
        {
            return Add(UiTheme.FieldLabel(text), 0);
        }

        /// <summary>label + input pair; returns the input.</summary>
        public TextBox AddField(string label, string text, int width)
        {
            var input = UiTheme.Field(text, width);
            var caption = UiTheme.FieldLabel(label);
            var cell = new Panel { Height = UiTheme.ControlHeight, Width = caption.Width + width + UiTheme.LabelGap };
            caption.Dock = DockStyle.Left;
            input.Dock = DockStyle.Fill;
            cell.Controls.Add(input);
            cell.Controls.Add(caption);
            Add(cell);
            row.EqualColumns = 2;
            return input;
        }

        public Button AddButton(string text, Action action, UiButtonKind kind = UiButtonKind.Normal)
        {
            return Add(UiTheme.MakeButton(text, action, kind));
        }

        public CheckBox AddCheck(string text, bool checkedState, EventHandler changed = null)
        {
            return Add(UiTheme.MakeCheck(text, checkedState, changed));
        }

        /// <summary>A row of its own whose single control spans the card width.</summary>
        public T AddWide<T>(T control, int height) where T : Control
        {
            NewRow(height, UiTheme.RowGap);
            control.Dock = DockStyle.Fill;
            row.Controls.Add(control);
            return control;
        }

        public Label Note(string text)
        {
            return UiTheme.AddNote(body, text);
        }
    }
}
