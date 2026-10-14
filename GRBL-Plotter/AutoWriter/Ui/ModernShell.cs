/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.

    AutoWriter modern shell (2026-10-12): runtime re-layout of the classic main window into the
    three-column arrangement documented in doc/UI重设计-规划-20261011.md (reference:
    doc/写字功能图片). The designer, the resx files and every event wiring stay untouched -
    existing user controls are only re-parented into new containers after InitializeComponent().
    No machine / streaming / pause-resume / coordinate logic changes; if anything fails during
    the rebuild the classic layout stays active. The mode can be flipped back to classic from
    the AutoWriter menu (restart required) and is persisted in ui-layout.json.

    2026-10-12 later: iOS-like skin (UiTheme), and the machine-setup dialog can be embedded
    into the center column as a page ("预览 / 写字配置" segment) instead of a separate window.
    The status refresh timer only touches cheap properties; the setup summary is reloaded from
    disk only when the setup page is left or the setup window closes - never on window
    activation, which made clicks feel laggy.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
*/
using GrblPlotter.AutoWriter.Services;
using GrblPlotter.AutoWriter.Ui;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GrblPlotter
{
    public partial class MainForm
    {
        /// <summary>Persisted choice between the classic and the modern shell (restart applies).</summary>
        internal static class ModernUiConfig
        {
            public static string ConfigPath
            {
                get
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "GRBL_Plotter", "AutoWriter", "ui-layout.json");
                }
            }
            private static bool? cached;
            public static bool ModernEnabled
            {
                get
                {
                    if (!cached.HasValue) cached = Load();
                    return cached.Value;
                }
            }
            private static bool Load()
            {
                try
                {
                    if (File.Exists(ConfigPath))
                        return !File.ReadAllText(ConfigPath).Contains("\"classic\"");
                }
                catch (Exception) { /* unreadable config -> default modern */ }
                return true;
            }
            public static void SetModern(bool modern)
            {
                cached = modern;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                    File.WriteAllText(ConfigPath, modern ? "{\"mode\":\"modern\"}" : "{\"mode\":\"classic\"}");
                }
                catch (Exception) { /* keep runtime value even when the file cannot be written */ }
            }
        }

        private TableLayoutPanel modernRoot;
        private Panel modernGCodeStrip, modernGCodeBody, modernAdvancedBody;
        private Panel modernCanvasCard, modernConfigHost;
        private Button modernTabPreview, modernTabSetup;
        private Label modernGCodeHeader, modernConnDot, modernConnText, modernFileChip, modernPageSummary;
        private ToolStripStatusLabel modernStatusProfile;
        private Timer modernStatusTimer;
        private bool modernShellActive, modernGCodeOpen, modernAdvancedOpen, modernSetupPage;

        /// <summary>Called once from the constructor after InitializeComponent()/menu setup.</summary>
        private void ModernShellApply()
        {
            if (!ModernUiConfig.ModernEnabled) return;
            try
            {
                SuspendLayout();
                if (BuildModernShell())
                {
                    splitContainer1.Visible = false;   // parked classic container keeps hidden helpers alive
                    DoubleBuffered = true;
                    modernShellActive = true;
                    StartModernStatusTimer();
                    RefreshPageSummary();
                }
            }
            catch (Exception err)
            {
                Logger.Error(err, "ModernShellApply - classic layout remains active");
            }
            finally { ResumeLayout(true); }
        }

        /// <summary>AutoWriter menu entry: flip between modern and classic shell (restart required).</summary>
        private void ModernUiToggle()
        {
            bool nowModern = ModernUiConfig.ModernEnabled;
            ModernUiConfig.SetModern(!nowModern);
            MessageBox.Show(this,
                nowModern ? "已切换为「经典界面」，重启软件后生效。"
                          : "已切换为「现代界面」，重启软件后生效。",
                "AutoWriter 界面", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool BuildModernShell()
        {
            // ---- single top bar: the native menu strip itself, iOS-styled, with quick actions
            // appended. The duplicated custom toolbar was removed on user feedback - keeping both
            // rows showed the same menus twice.
            ConfigureMenuStrip();

            // ---- three-column root ----
            modernRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = UiTheme.Background,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0)
            };
            modernRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
            modernRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            modernRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 292f));
            modernRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(modernRoot);

            // Docking order: lowest child index docks last. Desired result:
            // menuStrip (top) -> writingPageBar above statusStrip (bottom) -> root fills.
            // The parked classic container must NOT stay Dock=Fill or it would swallow the client area.
            splitContainer1.Dock = DockStyle.None;
            Controls.SetChildIndex(statusStrip1, 4);
            Controls.SetChildIndex(writingPageBar, 3);
            Controls.SetChildIndex(menuStrip1, 2);
            Controls.SetChildIndex(modernRoot, 0);

            Panel leftColumn = MakeColumn();
            Panel centerColumn = MakeColumn();
            Panel rightColumn = MakeColumn();
            modernRoot.Controls.Add(leftColumn, 0, 0);
            modernRoot.Controls.Add(centerColumn, 1, 0);
            modernRoot.Controls.Add(rightColumn, 2, 0);
            UiTheme.EnableDoubleBuffer(modernRoot);
            UiTheme.EnableDoubleBuffer(leftColumn);
            UiTheme.EnableDoubleBuffer(centerColumn);
            UiTheme.EnableDoubleBuffer(rightColumn);

            bool ok = true;
            BuildLeftColumn(leftColumn);
            ok &= BuildCenterColumn(centerColumn);
            ok &= BuildRightColumn(rightColumn);
            AppendStatusLabels();
            if (!ok) Logger.Error("ModernShell: not every control could be moved, modern shell disabled");
            return ok;
        }

        private static Panel MakeColumn()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(4, 2, 4, 2)
            };
        }

        private static Panel MakeToolbarButtonRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
        }

        private Button MakeShellButton(string text, UiButtonKind kind, EventHandler onClick, int width = 0)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = width <= 0,
                Width = width > 0 ? width : 0,
                Height = 30,
                Margin = new Padding(0, 0, 8, 4),
                Font = UiTheme.SafeFont(9f)
            };
            UiTheme.StyleFlatButton(button, kind);
            if (onClick != null) button.Click += onClick;
            return button;
        }

        // ---------------------------------------------------------------- top bar
        /// <summary>
        /// Restyles the native menu strip as the single top bar (white, larger face) and appends
        /// the quick actions. All original menus keep working with their correct drop-downs.
        /// </summary>
        private void ConfigureMenuStrip()
        {
            try
            {
                menuStrip1.BackColor = UiTheme.Card;
                menuStrip1.ForeColor = UiTheme.Text;
                menuStrip1.Font = UiTheme.SafeFont(9.75f);
                menuStrip1.Padding = new Padding(10, 4, 10, 4);
                menuStrip1.ShowItemToolTips = true;
                foreach (ToolStripItem item in menuStrip1.Items)
                {
                    item.ForeColor = UiTheme.Text;
                    item.BackColor = UiTheme.Card;
                }
                menuStrip1.Items.Add(new ToolStripSeparator());

                var setupItem = new ToolStripMenuItem("✍ 写字设置")
                {
                    BackColor = UiTheme.Accent,
                    ForeColor = Color.White,
                    Font = UiTheme.SafeFont(9.75f, FontStyle.Bold)
                };
                setupItem.Click += (s, a) => ShowMachineSetup();
                menuStrip1.Items.Add(setupItem);

                var serialItem = new ToolStripMenuItem("COM CNC 窗口")
                {
                    BackColor = UiTheme.Card,
                    ForeColor = UiTheme.Text
                };
                serialItem.Click += (s, a) => ShowSerialForm();
                menuStrip1.Items.Add(serialItem);
            }
            catch (Exception err) { Logger.Error(err, "ConfigureMenuStrip"); }
        }

        private void ShowSerialForm()
        {
            try
            {
                if (_serial_form == null || _serial_form.IsDisposed) return;
                if (_serial_form.WindowState == FormWindowState.Minimized)
                    _serial_form.WindowState = FormWindowState.Normal;
                _serial_form.Show();
                _serial_form.Activate();
            }
            catch (Exception err) { Logger.Error(err, "ShowSerialForm"); }
        }

        // ---------------------------------------------------------------- left column
        private void BuildLeftColumn(Panel column)
        {
            // ---- content card ----
            Panel contentBody;
            Panel contentCard = UiTheme.MakeCard("内容", out contentBody);
            AddModeRow(contentBody, "写字排版", "文字 / 笔记逐页书写", true,
                (s, a) => ShowMachineSetup(2));
            AddModeRow(contentBody, "区域模板", "多区域自动排版续页", false,
                (s, a) => ShowMachineSetup(3));
            AddModeRow(contentBody, "SVG 导入", "矢量图形描边", false,
                (s, a) => BtnOpenFile_Click(this, EventArgs.Empty));
            AddModeRow(contentBody, "图片导入", "位图转笔迹", false,
                (s, a) => { if (imageToolStripMenuItem != null) imageToolStripMenuItem.PerformClick(); });
            column.Controls.Add(contentCard);
            column.Controls.SetChildIndex(contentCard, 2);   // higher index docks first: 内容 must be on top

            // ---- page card ----
            Panel pageBody;
            Panel pageCard = UiTheme.MakeCard("页面 / 机器", out pageBody);
            modernPageSummary = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = UiTheme.SubText,
                Font = UiTheme.SafeFont(8.5f),
                Text = "读取上次配置…",
                Margin = new Padding(0, 0, 0, 8)
            };
            UiTheme.StackBody(pageBody, modernPageSummary);
            UiTheme.StackBody(pageBody,
                MakeShellButton("编辑内容 / 页面设置…", UiButtonKind.Primary, (s, a) => ShowMachineSetup()));
            column.Controls.Add(pageCard);
            column.Controls.SetChildIndex(pageCard, 1);

            // ---- file card ----
            Panel fileBody;
            Panel fileCard = UiTheme.MakeCard("文件", out fileBody);
            Panel row = MakeToolbarButtonRow();
            row.Controls.Add(MakeShellButton("打开", UiButtonKind.Normal, (s, a) => BtnOpenFile_Click(this, EventArgs.Empty)));
            row.Controls.Add(MakeShellButton("保存", UiButtonKind.Normal, (s, a) => BtnSaveFile_Click(this, EventArgs.Empty)));
            row.Controls.Add(MakeShellButton("新建", UiButtonKind.Normal, (s, a) => ModernNewFile()));
            UiTheme.StackBody(fileBody, row);
            column.Controls.Add(fileCard);
            column.Controls.SetChildIndex(fileCard, 0);
        }

        private void AddModeRow(Panel body, string title, string desc, bool selected, EventHandler onClick)
        {
            Panel rowPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = selected ? UiTheme.AccentSoft : UiTheme.Card,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 6)
            };
            Label titleLabel = new Label
            {
                Left = 10, Top = 5, AutoSize = true,
                Text = title,
                Font = UiTheme.SafeFont(9f, FontStyle.Bold),
                ForeColor = selected ? UiTheme.Accent : UiTheme.Text,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            Label descLabel = new Label
            {
                Left = 10, Top = 23, AutoSize = true,
                Text = desc,
                Font = UiTheme.SafeFont(8f),
                ForeColor = UiTheme.SubText,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            rowPanel.Controls.Add(titleLabel);
            rowPanel.Controls.Add(descLabel);
            rowPanel.Click += onClick;
            titleLabel.Click += onClick;
            descLabel.Click += onClick;
            UiTheme.StackBody(body, rowPanel);
        }

        private void ModernNewFile()
        {
            try
            {
                NewCodeStart(false);
                SetFctbCodeText("");
                NewCodeEnd();
            }
            catch (Exception err) { Logger.Error(err, "ModernNewFile"); }
        }

        // ---------------------------------------------------------------- center column
        private bool BuildCenterColumn(Panel column)
        {
            bool ok = true;

            // ---- collapsible G-code strip along the bottom ----
            modernGCodeStrip = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Color.Transparent };
            RoundedPanel gcodeFrame = new RoundedPanel(10) { Dock = DockStyle.Fill, BackColor = UiTheme.Card, Margin = new Padding(0) };
            modernGCodeHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "   ▸ G 代码（点击展开编辑器）",
                ForeColor = UiTheme.SubText,
                Font = UiTheme.SafeFont(9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            modernGCodeHeader.Click += (s, a) => ToggleModernGCode();
            modernGCodeBody = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = UiTheme.Card };
            gcodeFrame.Controls.Add(modernGCodeBody);
            gcodeFrame.Controls.Add(modernGCodeHeader);
            gcodeFrame.Controls.SetChildIndex(modernGCodeBody, 0);   // Fill docks last, header keeps the top edge
            modernGCodeStrip.Controls.Add(gcodeFrame);

            // ---- canvas card ----
            modernCanvasCard = new RoundedPanel(12) { Dock = DockStyle.Fill, BackColor = UiTheme.Card };
            Panel viewBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            Panel modeRowHost = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = UiTheme.Card };
            Panel viewToolbar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = UiTheme.Card };

            Label viewHint = new Label
            {
                Dock = DockStyle.Left,
                Width = 190,
                Text = "  预览 = 纸面朝向（仅显示）",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.SubText,
                Font = UiTheme.SafeFont(8.5f),
                BackColor = Color.Transparent
            };
            FlowLayoutPanel viewButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 280,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 2, 6, 0),
                BackColor = Color.Transparent
            };
            viewButtons.Controls.Add(MakeShellButton("重置视图", UiButtonKind.Normal, (s, a) =>
            { viewTransform.Reset(); ApplyViewOrientationChange(); SyncViewOrientationMenu(); }, 74));
            viewButtons.Controls.Add(MakeShellButton("旋转 90°", UiButtonKind.Normal, (s, a) =>
            { viewTransform.RotateClockwise(); ApplyViewOrientationChange(); SyncViewOrientationMenu(); }, 74));
            viewButtons.Controls.Add(MakeShellButton("适应窗口", UiButtonKind.Normal, (s, a) =>
            { try { CmsPicBoxResetZooming_Click(this, EventArgs.Empty); } catch (Exception err) { Logger.Error(err, "view fit"); } }, 74));

            // ---- preview / setup segment (setup page embeds MachineSetupForm here) ----
            FlowLayoutPanel segment = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 190,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(6, 3, 0, 0),
                BackColor = Color.Transparent
            };
            modernTabPreview = MakeShellButton("预览", UiButtonKind.Primary, (s, a) => SwitchCenterPage(false), 74);
            modernTabSetup = MakeShellButton("写字配置", UiButtonKind.Normal, (s, a) => ShowMachineSetup(), 86);
            segment.Controls.Add(modernTabPreview);
            segment.Controls.Add(modernTabSetup);

            viewToolbar.Controls.Add(viewButtons);
            viewToolbar.Controls.Add(viewHint);
            viewToolbar.Controls.Add(segment);
            viewToolbar.Controls.SetChildIndex(viewHint, 0);
            viewToolbar.Controls.SetChildIndex(segment, 0);

            // ---- setup page host (MachineSetupForm is embedded here in modern mode) ----
            modernConfigHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Background,
                Visible = false
            };

            modernCanvasCard.Controls.Add(viewBody);
            modernCanvasCard.Controls.SetChildIndex(viewBody, 0);
            modernCanvasCard.Controls.Add(viewToolbar);
            modernCanvasCard.Controls.Add(modeRowHost);
            UiTheme.EnableDoubleBuffer(viewBody);
            UiTheme.EnableDoubleBuffer(viewToolbar);

            column.Controls.Add(modernCanvasCard);
            column.Controls.SetChildIndex(modernCanvasCard, 0);
            column.Controls.Add(modernConfigHost);
            column.Controls.Add(modernGCodeStrip);
            UiTheme.EnableDoubleBuffer(modernCanvasCard);
            UiTheme.EnableDoubleBuffer(modernConfigHost);

            // ---- move the real controls ----
            ok &= TryMove(pictureBox1, viewBody, DockStyle.Fill, "pictureBox1");
            ok &= TryMove(fCTBCode, modernGCodeBody, DockStyle.Fill, "fCTBCode");
            TryMove(tLPMitteUnten1Zeile, modeRowHost, DockStyle.Fill, "2DViewModeRow");   // optional
            return ok;
        }

        /// <summary>Switch the center column between the 2D preview and the embedded setup page.</summary>
        private void SwitchCenterPage(bool showSetup)
        {
            modernSetupPage = showSetup;
            modernCanvasCard.Visible = !showSetup;
            modernConfigHost.Visible = showSetup;
            modernGCodeStrip.Visible = !showSetup;
            if (modernTabPreview != null)
                modernTabPreview.BackColor = showSetup ? UiTheme.FillGray : UiTheme.Accent;
            if (modernTabSetup != null)
                modernTabSetup.BackColor = showSetup ? UiTheme.Accent : UiTheme.FillGray;
            if (!showSetup) RefreshPageSummary();   // setup was possibly saved - reload the summary
        }

        /// <summary>
        /// Embeds the machine-setup window as a page of the center column (modern shell only).
        /// The form instance and all its logic stay exactly the same; only its parent changes.
        /// </summary>
        private bool EmbedMachineSetup(Form form)
        {
            if (modernConfigHost == null || form == null || form.IsDisposed) return false;
            if (form.TopLevel)
            {
                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
            }
            if (!ReferenceEquals(form.Parent, modernConfigHost))
            {
                modernConfigHost.Controls.Add(form);
                form.Dock = DockStyle.Fill;
            }
            form.Show();
            SwitchCenterPage(true);
            return true;
        }

        private void ToggleModernGCode()
        {
            modernGCodeOpen = !modernGCodeOpen;
            modernGCodeBody.Visible = modernGCodeOpen;
            modernGCodeStrip.Height = modernGCodeOpen ? 280 : 32;
            modernGCodeHeader.Text = modernGCodeOpen
                ? "   ▾ G 代码（点击收起编辑器）"
                : "   ▸ G 代码（点击展开编辑器）";
        }

        // ---------------------------------------------------------------- right column
        private bool BuildRightColumn(Panel column)
        {
            bool ok = true;

            // ---- connection card ----
            Panel connBody;
            Panel connCard = UiTheme.MakeCard("连接", out connBody);
            Panel connStatus = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = UiTheme.Card };
            modernConnDot = new Label
            {
                Left = 0, Top = 5, Width = 16, Height = 16,
                Text = "●", ForeColor = Color.LightGray,
                Font = UiTheme.SafeFont(10f), BackColor = Color.Transparent
            };
            modernConnText = new Label
            {
                Left = 20, Top = 4, AutoSize = true,
                Text = "未连接",
                Font = UiTheme.SafeFont(9f, FontStyle.Bold),
                ForeColor = UiTheme.Text, BackColor = Color.Transparent
            };
            connStatus.Controls.Add(modernConnDot);
            connStatus.Controls.Add(modernConnText);
            UiTheme.StackBody(connBody, connStatus);
            Panel connButtons = MakeToolbarButtonRow();
            connButtons.Controls.Add(MakeShellButton("连接", UiButtonKind.Primary, (s, a) =>
            { try { if (_serial_form != null) _serial_form.ConnectToGrbl(); } catch (Exception err) { Logger.Error(err, "modern connect"); } }, 62));
            connButtons.Controls.Add(MakeShellButton("断开", UiButtonKind.Normal, (s, a) =>
            { try { if (_serial_form != null) _serial_form.DisconnectFromGrbl(null, null); } catch (Exception err) { Logger.Error(err, "modern disconnect"); } }, 62));
            connButtons.Controls.Add(MakeShellButton("COM CNC", UiButtonKind.Normal, (s, a) => ShowSerialForm(), 78));
            UiTheme.StackBody(connBody, connButtons, 6);
            column.Controls.Add(connCard);
            column.Controls.SetChildIndex(connCard, 4);   // higher index docks first: 连接 must be on top

            // ---- coordinates / jogging / writing cards host the existing user controls ----
            Panel droBody;
            Panel droCard = UiTheme.MakeCard("坐标", out droBody);
            column.Controls.Add(droCard);
            column.Controls.SetChildIndex(droCard, 3);

            Panel jogBody;
            Panel jogCard = UiTheme.MakeCard("点动", out jogBody);
            column.Controls.Add(jogCard);
            column.Controls.SetChildIndex(jogCard, 2);
            Panel streamBody;
            Panel streamCard = UiTheme.MakeCard("书写", out streamBody);
            modernFileChip = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(250, 0),
                ForeColor = UiTheme.SubText,
                Font = UiTheme.SafeFont(8.5f),
                Text = "尚未载入文件",
                Margin = new Padding(0, 0, 0, 6)
            };
            UiTheme.StackBody(streamBody, modernFileChip);
            column.Controls.Add(streamCard);
            column.Controls.SetChildIndex(streamCard, 1);

            // ---- advanced (collapsible) ----
            Panel advBodyHost;
            Label advHeader;
            Panel advCard = UiTheme.MakeCard("高级（复位 / 解锁 / 坐标系 / 倍率）▸", out advBodyHost, out advHeader);
            advHeader.Cursor = Cursors.Hand;
            advHeader.Click += (s, a) => ToggleModernAdvanced();
            foreach (Control caption in advHeader.Controls) { caption.Cursor = Cursors.Hand; caption.Click += (s, a) => ToggleModernAdvanced(); }
            modernAdvancedBody = advBodyHost;
            modernAdvancedBody.Visible = false;
            column.Controls.Add(advCard);
            column.Controls.SetChildIndex(advCard, 0);

            // ---- critical moves ----
            ok &= TryMove(ucdro, droBody, DockStyle.Top, "ucdro");
            ok &= TryMove(ucJogControlAll, jogBody, DockStyle.Top, "ucJogControlAll");
            ok &= TryMove(ucStreaming, streamBody, DockStyle.Top, "ucStreaming");
            UiTheme.ApplyControlSkin(ucdro);
            ucdro.EnableModernLayout();
            UiTheme.ApplyControlSkin(ucJogControlAll);
            ucJogControlAll.EnableCenteredLayout();
            UiTheme.ApplyControlSkin(ucStreaming);

            // ---- optional moves into the advanced card ----
            TryMove(ucFlowControl, modernAdvancedBody, DockStyle.Top, "ucFlowControl");
            TryMove(ucSetCoordinateSystem, modernAdvancedBody, DockStyle.Top, "ucSetCoordinateSystem");
            TryMove(ucSetOffset, modernAdvancedBody, DockStyle.Top, "ucSetOffset");
            TryMove(ucOverrides, modernAdvancedBody, DockStyle.Top, "ucOverrides");
            TryMove(ucMoveToZero, modernAdvancedBody, DockStyle.Top, "ucMoveToZero");
            TryMove(ucMoveToGraphic, modernAdvancedBody, DockStyle.Top, "ucMoveToGraphic");
            return ok;
        }

        private void ToggleModernAdvanced()
        {
            modernAdvancedOpen = !modernAdvancedOpen;
            modernAdvancedBody.Visible = modernAdvancedOpen;
        }

        /// <summary>Re-parent one existing control; logs and reports failure instead of throwing.</summary>
        private bool TryMove(Control control, Control newParent, DockStyle dock, string name)
        {
            try
            {
                if (control == null || newParent == null) throw new InvalidOperationException("null control");
                if (control.Parent != null) control.Parent.Controls.Remove(control);
                newParent.Controls.Add(control);
                newParent.Controls.SetChildIndex(control, 0);
                control.Dock = dock;
                control.Visible = true;
                return true;
            }
            catch (Exception err)
            {
                Logger.Error(err, "ModernShell move {0}", name);
                return false;
            }
        }

        // ---------------------------------------------------------------- live status
        private void StartModernStatusTimer()
        {
            modernStatusTimer = new Timer { Interval = 750 };
            modernStatusTimer.Tick += (s, a) =>
            {
                try { UpdateModernStatus(); }
                catch (Exception) { /* status refresh must never crash the main window */ }
            };
            modernStatusTimer.Start();
            FormClosed += (s, a) => { try { modernStatusTimer.Stop(); modernStatusTimer.Dispose(); } catch (Exception) { } };
            // NOTE: no Activated hook here on purpose - the setup summary does disk I/O + JSON
            // deserialization and firing it on every window activation made clicks feel laggy.
        }

        private void UpdateModernStatus()
        {
            if (!modernShellActive) return;

            bool connected = false;
            string portText = "";
            try
            {
                if (_serial_form != null && !_serial_form.IsDisposed)
                {
                    connected = _serial_form.SerialPortOpen && _serial_form.IsConnectedToGrbl();
                    portText = Properties.Settings.Default.serialPort1 + " · " + Properties.Settings.Default.serialBaud1;
                }
            }
            catch (Exception) { connected = false; }
            modernConnDot.ForeColor = connected ? UiTheme.Green : Color.LightGray;
            string state = connected ? portText + " 已连接" : "未连接（COM CNC 窗口可详细操作）";
            if (modernConnText.Text != state) modernConnText.Text = state;

            string fileText;
            if (!string.IsNullOrEmpty(boundWritingProgram))
                fileText = "已载入：写字程序" + (boundWritingStale ? "（设置已变化，需重新生成）" : "");
            else if (!string.IsNullOrEmpty(lastLoadFile) && lastLoadFile != "Nothing loaded")
                fileText = "已打开：" + Path.GetFileName(lastLoadFile);
            else
                fileText = "尚未载入文件";
            try
            {
                if (writingPageTask != null && !string.IsNullOrEmpty(writingPageTask.StatusText))
                    fileText += "\n" + writingPageTask.StatusText;
            }
            catch (Exception) { }
            if (modernFileChip.Text != fileText) modernFileChip.Text = fileText;

            if (!modernGCodeOpen)
            {
                string header = "   ▸ G 代码";
                try { header += string.Format(" · {0} 行", fCTBCode.LinesCount); } catch (Exception) { }
                header += "（点击展开编辑器）";
                if (modernGCodeHeader.Text != header) modernGCodeHeader.Text = header;
            }
        }

        /// <summary>
        /// Left-column summary of the last saved machine setup. Deliberately NOT called on
        /// window activation: it reads and JSON-deserializes a file, which is far too heavy
        /// for that. Callers: shell startup, setup window closed, setup page left.
        /// </summary>
        private void RefreshPageSummary()
        {
            if (modernPageSummary == null) return;
            try
            {
                string warning;
                var state = new MachineSetupStateService(MachineSetupStateService.DefaultPath).Load(out warning);
                if (state == null || state.Profile == null)
                {
                    modernPageSummary.Text = "尚未保存机器配置。\n点下方按钮完成机器配置与写字排版。";
                    if (modernStatusProfile != null) modernStatusProfile.Text = "机器配置：未设置";
                    return;
                }
                var profile = state.Profile;
                string pen = (profile.PenUpValue.HasValue && profile.PenDownValue.HasValue)
                    ? string.Format("抬笔 Z{0} · 落笔 Z{1}", profile.PenUpValue.Value, profile.PenDownValue.Value)
                    : "笔控未配置";
                string speed = (profile.Speeds != null && profile.Speeds.DrawingMmPerMin.HasValue)
                    ? string.Format("绘图 F{0} · 空移 F{1}", profile.Speeds.DrawingMmPerMin.Value,
                                    profile.Speeds.TravelMmPerMin.HasValue ? profile.Speeds.TravelMmPerMin.Value.ToString() : "?")
                    : "速度未配置";
                int regions = state.Regions != null ? state.Regions.Count : 0;
                modernPageSummary.Text = string.Format("机器：{0}\n{1}\n{2}\n区域：{3} 个 · 验证状态：{4}",
                    profile.DisplayName ?? "未命名", pen, speed, regions, profile.VerificationStatus ?? "Unverified");
                if (modernStatusProfile != null)
                    modernStatusProfile.Text = string.Format("机器：{0}（{1}）", profile.DisplayName ?? "未命名",
                        profile.VerificationStatus ?? "Unverified");
            }
            catch (Exception err)
            {
                modernPageSummary.Text = "配置读取失败：" + err.Message;
            }
        }

        private void AppendStatusLabels()
        {
            try
            {
                var spring = new ToolStripStatusLabel { Spring = true, Text = "" };
                modernStatusProfile = new ToolStripStatusLabel
                {
                    Text = "机器配置：读取中…",
                    ForeColor = UiTheme.SubText,
                    BorderSides = ToolStripStatusLabelBorderSides.Left
                };
                var uiTag = new ToolStripStatusLabel
                {
                    Text = "现代界面",
                    ForeColor = UiTheme.Accent,
                    ToolTipText = "AutoWriter 现代界面（AutoWriter 菜单可切回经典界面，重启生效）"
                };
                statusStrip1.Items.Add(spring);
                statusStrip1.Items.Add(modernStatusProfile);
                statusStrip1.Items.Add(uiTag);
            }
            catch (Exception err) { Logger.Error(err, "AppendStatusLabels"); }
        }
    }
}
