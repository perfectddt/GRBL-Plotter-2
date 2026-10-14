using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
using GrblPlotter.AutoWriter.Ui;

namespace GrblPlotter.AutoWriter
{
    internal sealed class MachineSetupForm : Form
    {
        private readonly MachineProfileService profiles = new MachineProfileService();
        private readonly PropertyGrid properties = new PropertyGrid { Dock = DockStyle.Fill };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 46, AutoEllipsis = true };
        private readonly Dictionary<string, TextBox> inputs = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> overrides = new Dictionary<string, TextBox>();
        private readonly TextBox result = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 92 };
        private MachineProfile profile;
        private readonly MachineSetupStateService lastState;
        private TabControl setupTabs;
        private TextBox programPreview;
        private TextBox applyText;
        private TextBox writingText;
        // End-of-program park: explicit operator choice. Checked by default because a program that
        // stops at the last stroke forces the operator to jog the tool back before the next sheet.
        private CheckBox parkEnabled;
        private TextBox parkX;
        private TextBox parkY;
        // Start-point rule: the program's first pen-down point is the work origin, so the operator
        // controls where the character lands by positioning the sheet. Checked by default because
        // the built-in geometry carries its own margin and would otherwise start 1 mm inside.
        private CheckBox startAtOrigin;
        // Multi-character input (stage 2, §11.2-1): one string -> one continuous program.
        // Empty falls back to the built-in sample strokes so the old checks keep working.
        private TextBox writingInputText;
        private TextBox letterSpacingInput;
        private TextBox lineSpacingInput;
        // Font size = glyph body height in mm; empty keeps the native 10 mm body (scale 1.0).
        private TextBox fontSizeInput;
        // Font selector (stage 2 字库路线): built-in schematic glyphs or a discovered
        // font pack. Packs load lazily on selection; discovery never blocks the dialog.
        private CheckBox pageEnabled;
        private CheckBox autoWrap;
        private NumericUpDown pageSelector;
        private NumericUpDown regionPageSelector;
        private Label regionPageTotal;
        private Label pageTotal;
        private IList<WritingLayoutPage> writingPages;
        private bool updatingPage;
        private CheckBox regionsEnabled;
        private RegionCanvas regionCanvas;
        private readonly RegionHistory regionHistory = new RegionHistory();
        private List<WritingRegion> gestureBefore;
        private List<WritingRegion> regions = new List<WritingRegion>();
        private Label regionInfo;
        private readonly Dictionary<string, TextBox> pageFields = new Dictionary<string, TextBox>();
        private ComboBox fontSelector;
        private readonly List<string> fontPackPaths = new List<string>();
        private GlyphFontPack selectedFontPack;
        // Optional sink that loads a generated writing program into the main G-code editor so the
        // existing preview / simulation / send pipeline can be used. Null in an unconnected shell.
        private readonly Action<string> loadIntoPreview;
        private readonly Action<string> loadBoundPage;
        private readonly PageTaskService pageTask;
        private Label pageTaskStatus;
        private Button confirmPaperButton;
        private bool suppressTaskInvalidation;
        private readonly Action markLoadedStale;
        private readonly Action importGraphic;
        private string supplementPath = "";
        private GlyphFontPack supplementPack;
        private readonly Dictionary<string, GlyphFontPack> regionFonts = new Dictionary<string, GlyphFontPack>(StringComparer.OrdinalIgnoreCase);
        private TextBox variationAmount;
        private TextBox variationSeed;
        private TextBox gridRowsInput;
        private TextBox gridColumnsInput;
        private CheckBox drawGridLines;
        private TextBox penStartX;
        private TextBox penStartY;
        private Button penStartButton;
        private ToolTip penStartTips;
        /// <summary>Signature of the X/Y values last applied to the machine (or read back); drives the dual role of the 笔起点 button.</summary>
        private string penStartApplied;

        /// <summary>Page rectangle and writing boxes in work coordinates for the main view background.</summary>
        internal Action<RectangleF?, RectangleF[]> SheetOverlay { get; set; }
        /// <summary>Set the black start point in work coordinates; returns a refusal or null. The pen does not move.</summary>
        internal Func<double, double, string> PenStartRequested { get; set; }
        internal Func<double[]> PenStartQuery { get; set; }

        internal MachineSetupForm()
            : this(null)
        { }

        internal MachineSetupForm(Action<string> loadIntoPreview)
            : this(loadIntoPreview, MachineSetupStateService.DefaultPath)
        { }

        internal MachineSetupForm(Action<string> loadIntoPreview, string statePath)
            : this(loadIntoPreview, statePath, null, null)
        { }

        internal MachineSetupForm(Action<string> loadIntoPreview, string statePath, PageTaskService pageTask, Action<string> loadBoundPage)
            : this(loadIntoPreview, statePath, pageTask, loadBoundPage, null, null)
        { }

        internal MachineSetupForm(Action<string> loadIntoPreview, string statePath, PageTaskService pageTask, Action<string> loadBoundPage, Action markLoadedStale, Action importGraphic)
        {
            lastState = new MachineSetupStateService(statePath);
            this.loadIntoPreview = loadIntoPreview;
            this.loadBoundPage = loadBoundPage;
            this.pageTask = pageTask;
            this.markLoadedStale = markLoadedStale;
            this.importGraphic = importGraphic;
            Text = "AutoWriter " + AutoWriterBuild.Version + " — 机器配置与校准";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            Size = new Size(840, 670);
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            SuspendLayout();
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.SuspendLayout();
            setupTabs = tabs;
            var machineTab = new TabPage("机器配置");
            var calibrationTab = new TabPage("步进计算与校准");
            tabs.TabPages.AddRange(new[] { machineTab, calibrationTab });
            var programText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text, Font = new Font("Consolas", 9f),
                ScrollBars = ScrollBars.Both, WordWrap = false };
            programPreview = programText;
            properties.PropertyValueChanged += (sender, args) => { programPreview.Clear(); InvalidateWriting(); RefreshApplyReportQuiet(); };
            var programButtons = new UiGroup("校准程序");
            var startX = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Width = 90, Margin = new Padding(2, 6, 0, 0) };
            var startY = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Width = 90, Margin = new Padding(2, 6, 0, 0) };
            programButtons.AddLabel("起点/中心 X、Y（工作坐标 mm）");
            programButtons.NewRow();
            programButtons.Add(startX);
            programButtons.Add(startY);
            programButtons.Row.EqualColumns = 2;
            programButtons.NewRow();
            programButtons.AddButton("10mm短线", () => Execute(() => programText.Text = CalibrationProgramService.GenerateShortLine(profile, (double)startX.Value, (double)startY.Value, 10)));
            programButtons.AddButton("十字（臂长5mm）", () => Execute(() => programText.Text = CalibrationProgramService.GenerateCross(profile, (double)startX.Value, (double)startY.Value, 5)));
            programButtons.NewRow();
            programButtons.AddButton("10mm方框", () => Execute(() => programText.Text = CalibrationProgramService.GenerateSquare10Mm(profile, (double)startX.Value, (double)startY.Value)));
            programButtons.AddButton("导出程序", () => Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(programText.Text)) throw new InvalidOperationException("请先生成程序。");
                using (var dialog = new SaveFileDialog { Filter = "G-code (*.nc)|*.nc", FileName = "calibration.nc" })
                    if (dialog.ShowDialog(DialogOwner) == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, programText.Text);
            }));

            var applyTab = new TabPage("应用与写字预览") { BackColor = UiTheme.Background };
            tabs.TabPages.Add(applyTab);
            /* 2026-10-12 layout pass: this tab used to be two flat, wrapping bands holding ~20
             * buttons with the parameter labels scattered in between. Everything now sits in
             * titled cards ordered the way an operator walks through a job:
             *   内容与字体 -> 页面与分页 -> 笔迹变化与表格 -> 速度覆盖 -> 生成与项目
             * Every control kept its handler; nothing is hidden behind a drop-down. */
            // Same shape as 区域排版: a scrolling column of cards on the left, the previews on
            // the right, so the vertical space of the embedded dialog is not wasted on scrolling.
            var applySplitRoot = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                BackColor = UiTheme.CardBorder,   // the splitter strip is painted with this: keep it visible so the column can be dragged
                SplitterWidth = 6
            };
            applySplitRoot.Panel1.BackColor = UiTheme.Background;
            applySplitRoot.Panel2.BackColor = UiTheme.Background;
            var applyHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Background };
            var applyColumn = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Background,
                Padding = new Padding(8, 6, 8, 6)
            };
            applyHost.Controls.Add(applyColumn);
            applySplitRoot.Panel1.Controls.Add(applyHost);
            ApplySplitDistanceWhenLaidOut(applySplitRoot, () => 360);
            var groups = new List<UiGroup>();

            // ---- 1. 内容与字体 ----
            var groupContent = new UiGroup("内容与字体");
            groups.Add(groupContent);
            groupContent.AddLabel("文本");
            groupContent.NewRow();
            groupContent.AddButton("导入UTF-8文本", ImportWritingText);
            groupContent.AddButton("导入图片或SVG", ImportGraphic);
            writingInputText = new TextBox
            {
                Text = "",
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            writingInputText.TextChanged += (sender, args) => {
                string raw = writingInputText.Text;
                string normalized = WritingEditorText.Normalize(raw);
                if (raw == normalized) return;
                int start = writingInputText.SelectionStart;
                int end = start + writingInputText.SelectionLength;
                writingInputText.Text = normalized;
                int newStart = WritingEditorText.Normalize(raw.Substring(0, start)).Length;
                int newEnd = WritingEditorText.Normalize(raw.Substring(0, end)).Length;
                writingInputText.Select(Math.Min(newStart, normalized.Length), Math.Max(0, Math.Min(newEnd, normalized.Length) - Math.Min(newStart, normalized.Length)));
            };
            groupContent.AddWide(writingInputText, 58);
            groupContent.NewRow();
            groupContent.AddLabel("字体");
            groupContent.NewRow();
            fontSelector = new ScrollableFontComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 244,
                Height = UiTheme.ControlHeight,
                Margin = new Padding(0)
            };
            PopulateFontSelector();
            fontSelector.SelectedIndexChanged += (sender, args) => { selectedFontPack = null; InvalidateWriting(); };
            fontSelector.Dock = DockStyle.Fill;
            groupContent.Row.Controls.Add(fontSelector);
            groupContent.NewRow();
            fontSizeInput = groupContent.AddField("字号mm", "10", 42);
            letterSpacingInput = groupContent.AddField("字距mm", "2", 42);
            groupContent.NewRow();
            lineSpacingInput = groupContent.AddField("行距mm", "2", 42);
            groupContent.NewRow();
            groupContent.AddButton("缺字报告", ShowCoverage);
            groupContent.AddButton("补充缺字字库", ChooseSupplement);
            groupContent.Note("内置可用：" + BuiltinGlyphLibrary.AvailableSymbols + "；空文本 = 示例笔画。");

            // ---- 2. 页面与分页 ----
            var groupPage = new UiGroup("页面与分页");
            groups.Add(groupPage);
            pageFields.Add("宽", PageField(groupPage, "宽mm", "210", 46));
            pageFields.Add("高", PageField(groupPage, "高mm", "297", 46));
            groupPage.NewRow();
            pageFields.Add("左", PageField(groupPage, "左", "10", 40));
            pageFields.Add("右", PageField(groupPage, "右", "10", 40));
            groupPage.NewRow();
            pageFields.Add("上", PageField(groupPage, "上", "10", 40));
            pageFields.Add("下", PageField(groupPage, "下", "10", 40));
            groupPage.NewRow();
            pageEnabled = groupPage.AddCheck("自动分页（纸张左上角=工作原点）", true);
            groupPage.NewRow();
            autoWrap = groupPage.AddCheck("自动换行", true);
            groupPage.NewRow();
            startAtOrigin = groupPage.AddCheck("起笔点=原点（第一个落笔点 X0 Y0）", false);
            groupPage.NewRow();
            parkEnabled = groupPage.AddCheck("写完回原点（抬笔）", true);
            groupPage.NewRow();
            parkX = groupPage.AddField("回位 X", "0", 54);
            parkY = groupPage.AddField("回位 Y", "0", 54);
            groupPage.NewRow();
            groupPage.AddButton("上一页", () => { if (pageSelector.Value > 1) pageSelector.Value--; });
            pageSelector = groupPage.Add(new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1, Width = 58, Height = UiTheme.ControlHeight, Margin = new Padding(0) }, 6);
            pageTotal = groupPage.Add(new Label { Text = " / 未生成", AutoSize = true, ForeColor = UiTheme.SubText, Margin = new Padding(0) }, 2);
            groupPage.AddButton("下一页", () => { if (writingPages != null && pageSelector.Value < writingPages.Count) pageSelector.Value++; });
            groupPage.Row.EqualColumns = 4;
            pageSelector.ValueChanged += (sender, args) => {
                if (updatingPage) return;
                if (writingText != null) writingText.Clear();
                if (regionCanvas != null) { regionCanvas.Strokes = null; regionCanvas.Invalidate(); }
                if (regionPageSelector != null) regionPageSelector.Value = pageSelector.Value;
                status.Text = "页码已切换；请重新生成并载入当前页。换纸后确认相同工作原点，手动开始书写。";
            };
            groupPage.Note("多页请点窗口下方“冻结多页任务”。写完一页并确认空闲后，换纸点“已换纸并确认位置”，再在主界面手动开始。");

            // ---- 3. 笔迹变化与表格 ----
            var groupVariation = new UiGroup("笔迹变化与表格");
            groups.Add(groupVariation);
            variationAmount = groupVariation.AddField("变化mm", "0", 46);
            variationSeed = groupVariation.AddField("种子", "1", 54);
            groupVariation.NewRow();
            gridRowsInput = groupVariation.AddField("表格行", "0", 44);
            gridColumnsInput = groupVariation.AddField("列", "0", 44);
            groupVariation.NewRow();
            drawGridLines = groupVariation.AddCheck("格线写入笔迹", false);

            // ---- 4. 速度覆盖 ----
            var groupSpeed = new UiGroup("速度覆盖（留空用配置值）");
            groups.Add(groupSpeed);
            AddOverride(groupSpeed, "Z抬", "penup");
            AddOverride(groupSpeed, "Z落", "pendown");
            groupSpeed.NewRow();
            AddOverride(groupSpeed, "Z速", "zfeed");
            AddOverride(groupSpeed, "绘图", "draw");
            groupSpeed.NewRow();
            AddOverride(groupSpeed, "空移", "travel");

            // ---- 5. 生成与项目 ----
            var groupRun = new UiGroup("生成与项目");
            groups.Add(groupRun);
            groupRun.AddButton("生成写字预览", GenerateWritingPreview, UiButtonKind.Primary);
            groupRun.AddButton("生成并载入主界面预览", LoadWritingIntoMainPreview, UiButtonKind.Primary);
            groupRun.NewRow();
            groupRun.AddButton("刷新应用报告", RefreshApplyReport);
            groupRun.AddButton("检查连续排队", CheckContinuousQueue);
            groupRun.AddButton("导出写字程序", ExportWritingProgram);
            groupRun.NewRow();
            groupRun.AddButton("保存写字项目", SaveWritingProject);
            groupRun.AddButton("打开写字项目", OpenWritingProject);

            // Cards dock from the highest child index downwards, so add them bottom-first.
            for (int index = groups.Count - 1; index >= 0; index--)
                applyColumn.Controls.Add(groups[index].Card);

            pageEnabled.CheckedChanged += (sender, args) => { UpdatePageMode(); InvalidateWriting(); };
            autoWrap.CheckedChanged += (sender, args) => InvalidateWriting();
            foreach (TextBox box in new[] { writingInputText, letterSpacingInput, lineSpacingInput, fontSizeInput, parkX, parkY, variationAmount, variationSeed, gridRowsInput, gridColumnsInput })
                box.TextChanged += (sender, args) => InvalidateWriting();
            drawGridLines.CheckedChanged += (sender, args) => InvalidateWriting();
            parkEnabled.CheckedChanged += (sender, args) => InvalidateWriting();
            startAtOrigin.CheckedChanged += (sender, args) => InvalidateWriting();
            UpdatePageMode();
            applyText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text, ScrollBars = ScrollBars.Vertical, WordWrap = false };
            writingText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text, Font = new Font("Consolas", 9f),
                ScrollBars = ScrollBars.Both, WordWrap = false };
            var applySplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            applySplit.Panel1.Controls.Add(applyText);
            applySplit.Panel1.Controls.Add(PreviewHeader("应用报告（配置将如何落地）"));
            applySplit.Panel2.Controls.Add(writingText);
            applySplit.Panel2.Controls.Add(PreviewHeader("写字程序预览（生成的 G 代码）"));
            // SplitterDistance must be set after the control has a real size (Form.Load is still
            // too early - see ApplySplitDistanceWhenLaidOut); keep it non-fatal so the dialog always opens.
            ApplySplitDistanceWhenLaidOut(applySplit, () => Math.Max(applySplit.Panel1MinSize, applySplit.Height / 2));
            applySplitRoot.Panel2.Controls.Add(applySplit);
            applyTab.Controls.Add(applySplitRoot);

            BuildRegionTab(tabs, applyTab);
            Controls.Add(tabs);
            var taskBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false,
                Padding = new Padding(10, 5, 6, 0), BackColor = UiTheme.Background };
            pageTaskStatus = new Label { AutoSize = true, Text = pageTask == null ? "写字任务：未连接主界面" : pageTask.StatusText,
                Padding = new Padding(0, 6, 8, 0), ForeColor = UiTheme.SubText };
            taskBar.Controls.Add(pageTaskStatus);
            UiTheme.AddButton(taskBar, "冻结多页任务", FreezePageTask);
            confirmPaperButton = new Button { Text = "已换纸并确认位置", AutoSize = true, Height = 30, Enabled = false };
            confirmPaperButton.Click += (sender, args) => ConfirmPaperFromSetup();
            UiTheme.StyleFlatButton(confirmPaperButton, UiButtonKind.Success);
            taskBar.Controls.Add(confirmPaperButton);
            Controls.Add(taskBar);
            Controls.Add(status);
            Controls.SetChildIndex(status, Controls.Count - 1);
            if (pageTask != null)
            {
                pageTask.Changed += RefreshPageTaskStatus;
                FormClosed += (sender, args) => pageTask.Changed -= RefreshPageTaskStatus;
            }
            machineTab.BackColor = UiTheme.Background;
            Panel machineColumn;
            Panel machineDetail = BuildSettingsPage(machineTab, out machineColumn);
            var toolbar = new UiGroup("机器配置文件");
            toolbar.AddButton("新建（未验证）", NewProfile);
            toolbar.AddButton("打开配置", OpenProfile);
            toolbar.NewRow();
            toolbar.AddButton("保存配置", SaveProfile, UiButtonKind.Primary);
            toolbar.Note("空值表示尚未验证。配置保存不会连接机器或写入固件。");
            var help = new UiGroup("配置说明");
            help.Note("在右侧展开 AxisMapping / Speeds / Delays / Capabilities 设置参数。截图数值只能记录为候选。");
            help.Note("读取实机：用 --inspect-machine 启动，再手动连接；该模式只允许查询。");
            machineColumn.Controls.Add(help.Card);
            machineColumn.Controls.Add(toolbar.Card);
            machineDetail.Controls.Add(properties);
            machineDetail.Controls.Add(PreviewHeader("机器参数"));

            Panel calibrationColumn;
            Panel calibrationDetail = BuildSettingsPage(calibrationTab, out calibrationColumn);
            var motor = new UiGroup("电机与驱动");
            AddCalibrationField(motor, "angle", "步距角（度）", "1.8");
            AddCalibrationField(motor, "micro", "驱动细分", "1");
            var belt = new UiGroup("同步带与丝杆");
            AddCalibrationField(belt, "pitch", "同步带节距（mm）", "2");
            AddCalibrationField(belt, "teeth", "同步轮齿数", "16");
            AddCalibrationField(belt, "lead", "丝杆导程（mm/圈）", "8");
            belt.AddButton("同步带计算", () => Calculate(() => MotionCalibrationService.CalculateBelt(Value("angle"), Value("micro"), Value("pitch"), Value("teeth"))));
            belt.AddButton("丝杆计算", () => Calculate(() => MotionCalibrationService.CalculateScrew(Value("angle"), Value("micro"), Value("lead"))));
            var gear = new UiGroup("电子齿轮");
            AddCalibrationField(gear, "pulses", "内部每圈脉冲数", "7200");
            AddCalibrationField(gear, "numerator", "电子齿轮分子 N", "1");
            AddCalibrationField(gear, "denominator", "电子齿轮分母 D", "1");
            gear.Note("内部脉冲 = 外部脉冲 × N / D。");
            gear.AddButton("电子齿轮计算", () => Calculate(() => MotionCalibrationService.CalculateEffective(Value("pulses"), Value("lead"), Value("numerator"), Value("denominator"))));
            var measured = new UiGroup("实测修正");
            AddCalibrationField(measured, "old", "原步数/mm", "");
            AddCalibrationField(measured, "commanded", "指令距离（mm）", "");
            AddCalibrationField(measured, "measured", "实测距离（mm）", "");
            measured.AddButton("实测修正", () => Execute(() => result.Text = "修正步数/mm：" + MotionCalibrationService.CorrectMeasuredStepsPerMm(Value("old"), Value("commanded"), Value("measured")).ToString("G12", CultureInfo.InvariantCulture)));
            foreach (var group in new[] { programButtons, measured, gear, belt, motor })
                calibrationColumn.Controls.Add(group.Card);
            var previews = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = UiTheme.Background };
            result.Dock = DockStyle.Fill;
            result.BackColor = UiTheme.Card;
            result.ForeColor = UiTheme.Text;
            previews.Panel1.Controls.Add(result);
            previews.Panel1.Controls.Add(PreviewHeader("计算结果"));
            previews.Panel2.Controls.Add(programText);
            previews.Panel2.Controls.Add(PreviewHeader("校准程序预览（G 代码）"));
            calibrationDetail.Controls.Add(previews);
            ApplySplitDistanceWhenLaidOut(previews, () => Math.Max(25, previews.Height / 3));
            NewProfile();            RestoreLastState();
            tabs.ResumeLayout(false);
            ResumeLayout(false);
        }

        private Panel BuildSettingsPage(TabPage tab, out Panel column)
        {
            tab.BackColor = UiTheme.Background;
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
                BackColor = UiTheme.CardBorder,   // the splitter strip is painted with this: keep it visible so the column can be dragged
                SplitterWidth = 6 };
            split.Panel1.BackColor = split.Panel2.BackColor = UiTheme.Background;
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Background };
            column = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Background, Padding = new Padding(8, 6, 8, 6) };
            host.Controls.Add(column);
            split.Panel1.Controls.Add(host);
            tab.Controls.Add(split);
            ApplySplitDistanceWhenLaidOut(split, () => 360);
            return split.Panel2;
        }

        /// <summary>
        /// Apply a SplitterDistance once the SplitContainer has a real size. Form.Load fires before
        /// the TabControl lays out its pages (probed 2026-10-14: every split is still at its 192x70
        /// default then), so the previous Load-time assignments threw ArgumentOutOfRangeException and
        /// were silently swallowed, leaving the left columns at the ~163px default. Retry on Resize
        /// until the target distance fits, then stop listening.
        /// </summary>
        private static void ApplySplitDistanceWhenLaidOut(SplitContainer split, Func<int> distance)
        {
            EventHandler apply = null;
            apply = (sender, args) =>
            {
                int span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                if (span <= 220) return;   // still at the design-time default size, not laid out yet
                int target = distance();
                if (target < split.Panel1MinSize || target > span - split.Panel2MinSize - split.SplitterWidth) return;
                split.Resize -= apply;
                try { split.SplitterDistance = target; } catch (ArgumentOutOfRangeException) { }
            };
            split.Resize += apply;
            apply(null, EventArgs.Empty);
        }

        private void AddCalibrationField(UiGroup group, string key, string label, string text)
        {
            TextBox input = group.AddField(label, text, 80);
            foreach (Control child in input.Parent.Controls)
                if (child is Label) child.Width = 144;
            inputs.Add(key, input);
            group.Row.EqualColumns = 1;
            group.NewRow();
        }

        private static Label PreviewHeader(string text)
        {
            Label header = UiTheme.FieldLabel(text);
            header.Dock = DockStyle.Top;
            header.BackColor = UiTheme.Background;
            return header;
        }

        private void BuildRegionTab(TabControl tabs, TabPage writingTab)
        {
            /* 2026-10-12 layout pass: the 18 controls of this tab used to sit in one wrapping
             * band above the canvas. They are now grouped into a scrolling side column in the
             * order an operator works: 模式与页面 -> 区域编辑 -> 排列顺序 -> 模板 -> 笔起点
             * -> 显示 -> 生成与项目. Same controls, same handlers - nothing in a drop-down. */
            var tab = new TabPage("区域排版") { BackColor = UiTheme.Background };
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                BackColor = UiTheme.CardBorder,   // the splitter strip is painted with this: keep it visible so the column can be dragged
                SplitterWidth = 6
            };
            split.Panel1.BackColor = UiTheme.Background;
            split.Panel2.BackColor = UiTheme.Background;
            var sideHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Background };
            var column = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Background,
                Padding = new Padding(8, 6, 8, 6)
            };
            sideHost.Controls.Add(column);
            split.Panel1.Controls.Add(sideHost);
            ApplySplitDistanceWhenLaidOut(split, () => 360);

            var groups = new List<UiGroup>();

            // ---- 1. 排版模式与页面 ----
            var groupMode = new UiGroup("排版模式与页面");
            groups.Add(groupMode);
            regionsEnabled = groupMode.AddCheck("多区域排版", false);
            groupMode.NewRow();
            var previousRegionPage = groupMode.AddButton("上一页", () => { if (pageSelector.Value > 1) pageSelector.Value--; });
            previousRegionPage.AutoSize = false;
            previousRegionPage.Width = 58;
            regionPageSelector = groupMode.Add(new NumericUpDown
            { Minimum = 1, Maximum = 100000, Value = pageSelector.Value, Width = 58, Height = UiTheme.ControlHeight, Margin = new Padding(0) }, 6);
            regionPageTotal = groupMode.Add(new Label
            { Text = " / 未生成", AutoSize = false, Width = 76, Height = UiTheme.ControlHeight,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.SubText, Margin = new Padding(0) }, 2);
            var nextRegionPage = groupMode.AddButton("下一页", () => { if (writingPages != null && pageSelector.Value < writingPages.Count) pageSelector.Value++; });
            nextRegionPage.AutoSize = false;
            nextRegionPage.Width = 58;
            groupMode.Row.EqualColumns = 4;
            regionPageSelector.ValueChanged += (sender, args) => { if (!updatingPage) pageSelector.Value = regionPageSelector.Value; };
            groupMode.NewRow();
            groupMode.AddButton("纸张与字体设置", () => tabs.SelectedTab = writingTab);

            // ---- 2. 区域编辑 ----
            var groupEdit = new UiGroup("区域编辑");
            groups.Add(groupEdit);
            groupEdit.AddButton("生成表格", MakeGrid);
            groupEdit.AddButton("编辑选中区域", () => EditRegion(regionCanvas.Selected));
            groupEdit.NewRow();
            groupEdit.AddButton("复制选中区域", CopySelectedRegion);
            groupEdit.AddButton("删除选中区域", () => {
                int index = regionCanvas.Selected;
                if (index >= 0 && index < regions.Count) { RememberRegions(); regions.RemoveAt(index); regionCanvas.Selected = Math.Min(index, regions.Count - 1); InvalidateWriting(); }
            });

            // ---- 3. 排列顺序 ----
            var groupOrder = new UiGroup("排列顺序");
            groups.Add(groupOrder);
            groupOrder.AddButton("书写顺序提前", () => MoveRegionOrder(-1));
            groupOrder.AddButton("书写顺序推后", () => MoveRegionOrder(1));
            groupOrder.NewRow();
            groupOrder.AddButton("撤销", UndoRegions);
            groupOrder.AddButton("重做", RedoRegions);

            // ---- 4. 模板 ----
            var groupTemplate = new UiGroup("模板");
            groups.Add(groupTemplate);
            groupTemplate.AddButton("区域模板", ManageRegionTemplates);

            // ---- 5. 笔起点 ----
            var groupPen = new UiGroup("笔起点");
            groups.Add(groupPen);
            penStartX = groupPen.AddField("黑色起点 X", "0", 58);
            penStartX.KeyDown += PenStartBoxKeyDown;
            penStartY = groupPen.AddField("Y(向下)", "0", 58);
            penStartY.KeyDown += PenStartBoxKeyDown;
            groupPen.NewRow();
            penStartButton = groupPen.AddButton("笔起点", PenStartButtonClicked, UiButtonKind.Primary);
            groupPen.AddButton("读取黑色起点", ReadPenStart);
            penStartTips = new ToolTip();
            penStartTips.SetToolTip(penStartButton, "改了 X/Y 数字后点它 = 按坐标设置预览标记（不修改机器坐标）；数字没改就点它 = 进入图上点选起点。");
            penStartTips.SetToolTip(penStartX, "输入纸面 X 毫米，回车或点「笔起点」生效。");
            penStartTips.SetToolTip(penStartY, "输入从纸顶向下的毫米，回车或点「笔起点」生效。");
            groupPen.Note("改了 X/Y 数字后点「笔起点」= 按坐标设置预览标记（不修改机器坐标）；数字没改就点它 = 进入图上点选起点。");

            // ---- 6. 显示 ----
            var groupView = new UiGroup("显示");
            groups.Add(groupView);
            var snap = groupView.AddCheck("吸附1mm", false);
            snap.CheckedChanged += (sender, args) => { if (regionCanvas != null) regionCanvas.SnapToGrid = snap.Checked; };
            var grid = groupView.AddCheck("网格", true);
            grid.CheckedChanged += (sender, args) => { if (regionCanvas != null) { regionCanvas.ShowGrid = grid.Checked; regionCanvas.Invalidate(); } };

            // ---- 7. 生成与项目 ----
            var groupRun = new UiGroup("生成与项目");
            groups.Add(groupRun);
            groupRun.AddButton("区域笔迹预览", () => Execute(() => {
                InvalidateWriting();
                if (!regionsEnabled.Checked) throw new InvalidOperationException("请先勾选多区域单页。");
                BuildWritingStrokes();
            }), UiButtonKind.Success);
            groupRun.NewRow();
            groupRun.AddButton("生成并载入主界面预览", LoadWritingIntoMainPreview);
            groupRun.NewRow();
            groupRun.AddButton("保存写字项目", SaveWritingProject);
            groupRun.AddButton("打开写字项目", OpenWritingProject);

            // Cards dock from the highest child index downwards, so add them bottom-first.
            for (int index = groups.Count - 1; index >= 0; index--)
                column.Controls.Add(groups[index].Card);

            regionInfo = new Label { Dock = DockStyle.Bottom, Height = 64, Text = "空白处拖动画新框。拖框内部移动，拖橙点改大小。滚轮缩放，右键平移，都不改变毫米尺寸。蓝框不写，红线是本页笔迹。改过 X/Y 数字后点「笔起点」= 按坐标设置预览标记；数字没改时点它 = 进图上点选（左键定点，右键或 Esc 取消）。" };
            regionCanvas = new RegionCanvas { Regions = regions };
            regionCanvas.PenStartPicked = AcceptPickedPenStart;
            regionCanvas.GestureStarted = () => gestureBefore = regions.ConvertAll(item => item.Clone());
            regionCanvas.GestureFinished = AcceptRegionGesture;
            regionCanvas.UndoRequested = UndoRegions;
            regionCanvas.RedoRequested = RedoRegions;
            regionCanvas.RegionCreated = r => {
                // Numeric editor uses hundredths of a millimetre; clamp rounding at page edges.
                var sheet = ReadPage(); r.X = Math.Round(r.X, 2); r.Y = Math.Round(r.Y, 2);
                r.Width = Math.Min(Math.Round(r.Width, 2), sheet.WidthMm - r.X);
                r.Height = Math.Min(Math.Round(r.Height, 2), sheet.HeightMm - r.Y);
                if (string.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N");
                using (var editor = new RegionEditorForm(r, sheet, regions.Count + 1))
                    if (editor.ShowDialog(DialogOwner) == DialogResult.OK) {
                        try { AcceptRegion(editor.Value); }
                        catch (Exception error) { MessageBox.Show(DialogOwner, error.Message + "\n未添加区域，正文不会被裁掉。", "区域"); return; }
                        StampRegionFont(editor.Value);
                        RememberRegions(); regions.Add(editor.Value); regionCanvas.Selected = regions.Count - 1; InvalidateWriting();
                    }
            };
            regionCanvas.EditRequested = EditRegion;
            regionsEnabled.CheckedChanged += (sender, args) => { UpdatePageMode(); InvalidateWriting(); };
            split.Panel2.Controls.Add(regionCanvas);   // Fill docks last
            split.Panel2.Controls.Add(regionInfo);     // Bottom docks first, keeps the lower edge
            tab.Controls.Add(split);
            tabs.TabPages.Add(tab);
            tab.Enter += (sender, args) => { RefreshRegionCanvas(); ReadPenStart(); };
        }
        private void ManageRegionTemplates()
        {
            Execute(() => {
                using (var dialog = new RegionTemplateForm(ReadPage(), regions)) {
                    if (dialog.ShowDialog(DialogOwner) != DialogResult.OK) return;
                    var template = dialog.SelectedTemplate;
                    string match = template.DescribeKeep(regions);
                    var updated = template.Apply(regions, dialog.KeepText);
                    double[] values = { template.Sheet.WidthMm, template.Sheet.HeightMm, template.Sheet.LeftMm, template.Sheet.RightMm, template.Sheet.TopMm, template.Sheet.BottomMm };
                    string[] keys = { "宽", "高", "左", "右", "上", "下" };
                    for (int i=0;i<keys.Length;i++) pageFields[keys[i]].Text=Number(values[i]);
                    autoWrap.Checked=template.Sheet.AutoWrap; pageEnabled.Checked=true;
                    RememberRegions();
                    regions=updated; regionCanvas.Regions=regions; regionCanvas.Selected=-1;
                    regionsEnabled.Checked=true; pageSelector.Value=1;
                    UpdatePageMode(); InvalidateWriting();
                    status.Text="已应用模板："+template.Name+"。"+match+"。"+(dialog.KeepText ? "有稳定编号时按编号保留文字，否则按顺序。" : "区域文字已清空。")+"请重新生成预览。";
                }
            });
        }
        private void CopySelectedRegion()
        {
            Execute(() => {
                int index = regionCanvas.Selected;
                if (index < 0 || index >= regions.Count) throw new InvalidOperationException("请先点击一个区域。");
                PageLayoutOptions sheet = ReadPage();
                WritingRegion copy = regions[index].Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                bool placed = false;
                for (int step = 1; step <= 12; step++)
                {
                    double x = regions[index].X + 5 * step, y = regions[index].Y + 5 * step;
                    if (x + copy.Width <= sheet.WidthMm + 1e-9 && y + copy.Height <= sheet.HeightMm + 1e-9) { copy.X = x; copy.Y = y; placed = true; break; }
                }
                if (!placed)
                {
                    copy.X = Math.Max(0, Math.Min(sheet.WidthMm - copy.Width, regions[index].X + 3));
                    copy.Y = Math.Max(0, Math.Min(sheet.HeightMm - copy.Height, regions[index].Y + 3));
                }
                copy.Validate(sheet);
                RememberRegions(); regions.Add(copy); regionCanvas.Selected = regions.Count - 1; InvalidateWriting();
            });
        }
        private void MoveRegionOrder(int delta)
        {
            int index = regionCanvas == null ? -1 : regionCanvas.Selected;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= regions.Count) { status.Text = "请先选中区域，再调整书写顺序。蓝框上的数字就是书写顺序。"; return; }
            RememberRegions();
            WritingRegion region = regions[index];
            regions.RemoveAt(index); regions.Insert(target, region);
            regionCanvas.Selected = target; InvalidateWriting();
            status.Text = "书写顺序已改为 " + (target + 1) + "。";
        }
        private void RememberRegions() { regionHistory.Remember(regions); }
        private void UndoRegions()
        {
            IList<WritingRegion> previous = regionHistory.Undo(regions);
            if (previous == null) { status.Text = "没有可撤销的区域编辑。"; return; }
            regions = new List<WritingRegion>(previous); regionCanvas.Regions = regions;
            if (regionCanvas.Selected >= regions.Count) regionCanvas.Selected = regions.Count - 1;
            InvalidateWriting(); status.Text = "已撤销。区域文字和框都恢复到上一步。";
        }
        private void RedoRegions()
        {
            IList<WritingRegion> next = regionHistory.Redo(regions);
            if (next == null) { status.Text = "没有可重做的区域编辑。"; return; }
            regions = new List<WritingRegion>(next); regionCanvas.Regions = regions;
            if (regionCanvas.Selected >= regions.Count) regionCanvas.Selected = regions.Count - 1;
            InvalidateWriting(); status.Text = "已重做区域编辑。";
        }
        private void AcceptRegionGesture(bool changed)
        {
            if (!changed || gestureBefore == null) { gestureBefore = null; return; }
            try
            {
                int index = regionCanvas.Selected;
                if (index >= 0 && index < regions.Count) AcceptRegion(regions[index]);
            }
            catch (Exception error)
            {
                regions = gestureBefore; regionCanvas.Regions = regions; gestureBefore = null;
                status.Text = error.Message + " 已恢复原区域，不会静默裁掉正文。";
                RefreshRegionCanvas(); return;
            }
            regionHistory.Remember(gestureBefore); gestureBefore = null; InvalidateWriting();
        }
        private void AcceptRegion(WritingRegion region)
        {
            RegionLayoutService.EnsureWritable(region, ReadPage(), ResolverFor(region));
        }
        private Func<char, BuiltinGlyph> ChainResolver()
        {
            GlyphFontPack pack = SelectedPackOrNull();
            EnsureSupplementLoaded();
            if (supplementPack != null) pack = pack == null ? supplementPack : pack.OverlayWith(supplementPack);
            return pack == null ? (Func<char, BuiltinGlyph>)BuiltinGlyphLibrary.Resolve : pack.ToResolver();
        }
        private Func<char, BuiltinGlyph> ResolverFor(WritingRegion region)
        {
            if (region == null || string.IsNullOrEmpty(region.FontPath)) return ChainResolver();
            GlyphFontPack pack = LoadRegionFont(region.FontPath);
            EnsureSupplementLoaded();
            if (supplementPack != null) pack = pack.OverlayWith(supplementPack);
            return pack.ToResolver();
        }
        private void EnsureSupplementLoaded()
        {
            if (supplementPack == null && !string.IsNullOrEmpty(supplementPath))
                supplementPack = GlyphFontPack.LoadFromFile(supplementPath);
        }
        private void StampRegionFont(WritingRegion region)
        {
            if (region == null) return;
            region.FontPath = region.FontPath == null ? "" : region.FontPath.Trim();
            region.FontSha256 = region.FontPath.Length == 0 ? "" : WritingProjectService.Fingerprint(region.FontPath);
        }
        private GlyphFontPack LoadRegionFont(string path)
        {
            GlyphFontPack pack;
            if (!regionFonts.TryGetValue(path, out pack))
            {
                pack = GlyphFontPack.LoadFromFile(path);
                regionFonts[path] = pack;
            }
            return pack;
        }
        private void EditRegion(int index)
        {
            Execute(() => {
                if (index < 0 || index >= regions.Count) throw new InvalidOperationException("请先点击一个区域。");
                using (var editor = new RegionEditorForm(regions[index], ReadPage(), index + 1))
                    if (editor.ShowDialog(DialogOwner) == DialogResult.OK) {
                        AcceptRegion(editor.Value);
                        RememberRegions();
                        if (string.IsNullOrEmpty(editor.Value.Id)) editor.Value.Id = string.IsNullOrEmpty(regions[index].Id) ? Guid.NewGuid().ToString("N") : regions[index].Id;
                        StampRegionFont(editor.Value);
                        regions[index] = editor.Value; InvalidateWriting();
                    }
            });
        }
        private void RefreshRegionCanvas()
        {
            if (regionCanvas == null) return;
            try {
                regionCanvas.Sheet = ReadPage(); regionCanvas.DrawEnabled = regionsEnabled.Checked;
                string report = regionsEnabled.Checked ? RegionLayoutService.Describe(regions, regionCanvas.Sheet, ChainResolver(), ResolverFor) : "";
                var warnings = new List<int>();
                if (regionsEnabled.Checked)
                    for (int i = 0; i < regions.Count; i++)
                        try { if (regions[i] != null && !string.IsNullOrWhiteSpace(regions[i].Text)) RegionLayoutService.EnsureWritable(regions[i], regionCanvas.Sheet, ResolverFor(regions[i])); }
                        catch (Exception) { warnings.Add(i); }
                regionCanvas.Warnings = warnings.ToArray();
                regionInfo.Text = regionsEnabled.Checked
                    ? "拖框移动，拖橙点改大小。滚轮缩放、右键平移，毫米尺寸不变。编号是书写顺序。" + report
                    : "勾选多区域后，在空白处拖动画框。蓝框不写，红线是本页笔迹。";
            } catch (Exception error) { regionCanvas.Sheet = null; regionCanvas.DrawEnabled = false; regionInfo.Text = "请修正纸张参数：" + error.Message; }
            regionCanvas.Invalidate();
        }

        private Dictionary<string, TextBox> StateTextFields()
        {
            var fields = new Dictionary<string, TextBox> {
                { "text", writingInputText }, { "letter", letterSpacingInput }, { "line", lineSpacingInput },
                { "size", fontSizeInput }, { "parkX", parkX }, { "parkY", parkY },
                { "variation", variationAmount }, { "seed", variationSeed }, { "gridRows", gridRowsInput }, { "gridCols", gridColumnsInput } };
            foreach (var pair in overrides) fields.Add("override." + pair.Key, pair.Value);
            foreach (var pair in pageFields) fields.Add("page." + pair.Key, pair.Value);
            foreach (var pair in inputs) fields.Add("calibration." + pair.Key, pair.Value);
            return fields;
        }
        private Dictionary<string, CheckBox> StateChecks()
        {
            return new Dictionary<string, CheckBox> { { "page", pageEnabled }, { "wrap", autoWrap },
                { "park", parkEnabled }, { "origin", startAtOrigin }, { "regions", regionsEnabled }, { "gridInk", drawGridLines } };
        }
        private void SaveLastState()
        {
            var state = new MachineSetupState { Profile = profile, TabIndex = setupTabs.SelectedIndex,
                TabName = setupTabs.SelectedTab.Text, Regions = regions.ConvertAll(r => r.Clone()),
                FontPath = fontSelector.SelectedIndex > 0 ? Path.GetFullPath(fontPackPaths[fontSelector.SelectedIndex - 1]) : "" };
            foreach (var pair in StateTextFields()) state.Text.Add(pair.Key, pair.Key == "text" ? WritingEditorText.Normalize(pair.Value.Text) : pair.Value.Text);
            foreach (var pair in StateChecks()) state.Checks.Add(pair.Key, pair.Value.Checked);
            state.Text["selectedPage"] = pageSelector.Value.ToString(CultureInfo.InvariantCulture);
            state.Text["supplement"] = supplementPath ?? "";
            lastState.Save(state);
        }
        private void RestoreLastState()
        {
            suppressTaskInvalidation = true;
            try { RestoreLastStateCore(); }
            finally { suppressTaskInvalidation = false; RefreshPageTaskStatus(); }
        }
        private void RestoreLastStateCore()
        {
            string warning;
            var state = lastState.Load(out warning);
            if (state != null)
            {
                regions = state.Regions ?? new List<WritingRegion>(); regionCanvas.Regions = regions;
                profile = state.Profile; properties.SelectedObject = profile;
                foreach (var pair in StateTextFields()) { string value; if (state.Text.TryGetValue(pair.Key, out value)) pair.Value.Text = pair.Key == "text" ? WritingEditorText.Normalize(value) : value; }
                foreach (var pair in StateChecks()) { bool value; if (state.Checks.TryGetValue(pair.Key, out value)) pair.Value.Checked = value; }
                if (state.FontPath.Length > 0)
                {
                    int index = fontPackPaths.FindIndex(path => string.Equals(Path.GetFullPath(path), state.FontPath, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        fontPackPaths.Add(state.FontPath);
                        fontSelector.Items.Add("上次字体：" + Path.GetFileName(state.FontPath)); index = fontPackPaths.Count - 1;
                    }
                    fontSelector.SelectedIndex = index + 1;
                    if (!File.Exists(state.FontPath)) warning = (warning == null ? "" : warning + "；") + "上次字体缺失，请恢复字库或重新选择字体：" + state.FontPath;
                }
                string supplement;
                if (state.Text.TryGetValue("supplement", out supplement) && !string.IsNullOrEmpty(supplement))
                {
                    if (File.Exists(supplement)) { supplementPath = supplement; supplementPack = null; }
                    else warning = (warning == null ? "" : warning + "；") + "补充字库缺失，原字库未改：" + supplement;
                }
                string savedPage; int restoredPage;
                if (state.Text.TryGetValue("selectedPage", out savedPage) && int.TryParse(savedPage, out restoredPage))
                    pageSelector.Value = Math.Max(1, Math.Min(100000, restoredPage));
                int restoredTab = state.TabIndex <= 0 ? 0 : state.TabIndex <= 2 ? 1 : state.TabIndex - 1;
                if (!string.IsNullOrEmpty(state.TabName))
                    for (int i = 0; i < setupTabs.TabCount; i++)
                        if (setupTabs.TabPages[i].Text == state.TabName) { restoredTab = i; break; }
                if (restoredTab >= 0 && restoredTab < setupTabs.TabCount) setupTabs.SelectedIndex = restoredTab;
                UpdatePageMode(); RefreshApplyReportQuiet(); InvalidateWriting(); programPreview.Clear();
                status.Text = "已恢复上次窗口设置；请重新生成预览。";
            }
            if (warning != null) status.Text = warning;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            try { SaveLastState(); }
            catch (Exception error)
            {
                status.Text = "上次设置保存失败：" + error.Message;
                MessageBox.Show(DialogOwner, status.Text, "窗口设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void NewProfile()
        {
            if (programPreview != null) programPreview.Clear();
            profile = new MachineProfile { ProfileId = Guid.NewGuid().ToString("N"), DisplayName = "新机器（未验证）",
                AxisMapping = new CoordinateMapping(), UsableBoundsWorkMm = new CoordinateBounds(),
                Speeds = new MachineSpeeds(), Delays = new MachineDelays(), Capabilities = new MachineCapabilities(),
                VerificationEvidence = new List<string>() };
            properties.SelectedObject = profile;
            status.Text = "配置未验证；不会自动应用到现有工艺。计算栏预填值仅为示例。";
            if (writingText != null) writingText.Clear();
            RefreshApplyReportQuiet();
        }

        private void OpenProfile()
        {
            using (var dialog = new OpenFileDialog { Filter = "机器配置 (*.json)|*.json" })
                if (dialog.ShowDialog(DialogOwner) == DialogResult.OK) Execute(() =>
                {
                    profile = profiles.Load(dialog.FileName);
                    programPreview.Clear();
                    if (writingText != null) writingText.Clear();
                    properties.SelectedObject = profile;
                    RefreshApplyReportQuiet();
                    status.Text = "已读取：" + dialog.FileName;
                });
        }

        private void SaveProfile()
        {
            using (var dialog = new SaveFileDialog { Filter = "机器配置 (*.json)|*.json", FileName = "machine-profile.json", DefaultExt = "json" })
                if (dialog.ShowDialog(DialogOwner) == DialogResult.OK) Execute(() =>
                {
                    profiles.Save(dialog.FileName, profile);
                    status.Text = "已保存：" + dialog.FileName + "（覆盖时保留备份）";
                });
        }

        private void Calculate(Func<MotionCalibrationResult> calculation)
        {
            Execute(() => { var value = calculation(); result.Text = string.Format(CultureInfo.InvariantCulture,
                "步数/mm：{0:G12}\r\n毫米/步：{1:G12}\r\n外部脉冲/圈：{2:G12}；移动毫米/圈：{3:G12}\r\n仅计算，不写入控制板；电子齿轮约定须与驱动器说明一致。",
                value.StepsPerMm, value.MmPerStep, value.PulsesPerRevolution, value.TravelPerRevolutionMm); });
        }

        // Explicit "apply machine profile" summary. Unknown values stay unknown; nothing is filled in
        // unless the operator typed an explicit override, which is marked "（显式覆盖）".
        private void RefreshApplyReport()
        {
            try { applyText.Text = MachineProfileApplier.BuildReport(profile, ReportOverrides()).ToText(); }
            catch (Exception error) { MessageBox.Show(DialogOwner, error.Message, "应用报告", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RefreshApplyReportQuiet()
        {
            if (applyText == null) return;
            try { applyText.Text = MachineProfileApplier.BuildReport(profile, ReportOverrides()).ToText(); }
            catch (Exception error) { applyText.Text = "应用报告暂不可用：" + error.Message; }
        }

        // For the report, an unparsable override is ignored (treated as "not supplied") so the
        // report keeps rendering while the operator is still typing. Generation stays strict.
        private WritingProgramOptions ReportOverrides()
        {
            return new WritingProgramOptions
            {
                PenUpValue = TryOverride("penup"),
                PenDownValue = TryOverride("pendown"),
                ZMmPerMin = TryOverride("zfeed"),
                DrawingMmPerMin = TryOverride("draw"),
                TravelMmPerMin = TryOverride("travel")
            };
        }

        private double? TryOverride(string key)
        {
            try { return Override(key); }
            catch (ArgumentException) { return null; }
        }

        private double? Override(string key)
        {
            string text = overrides[key].Text.Trim();
            if (text.Length == 0) return null;
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException("覆盖值不是有效数值（小数点使用 . ）：" + text);
            return value;
        }

        private WritingProgramOptions BuildWritingOptions()
        {
            return new WritingProgramOptions
            {
                RequireVerifiedRun = false,
                PenUpValue = Override("penup"),
                PenDownValue = Override("pendown"),
                ZMmPerMin = Override("zfeed"),
                DrawingMmPerMin = Override("draw"),
                TravelMmPerMin = Override("travel"),
                ParkPosition = ParkPoint(),
                AlignStartToOrigin = startAtOrigin != null && startAtOrigin.Checked
            };
        }

        // Null when the operator switched the end park off, which leaves the historical behaviour
        // (the program stops at the end of the last stroke).
        private WritingPoint? ParkPoint()
        {
            if (parkEnabled == null || !parkEnabled.Checked) return null;
            return new WritingPoint(ParkValue(parkX, "回原点 X"), ParkValue(parkY, "回原点 Y"));
        }

        private static double ParkValue(TextBox box, string name)
        {
            string text = box.Text.Trim();
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException(name + " 不是有效数值（小数点使用 . ）：" + text);
            return value;
        }

        // Strokes for the writing preview/export/load path: the operator's text laid out by
        // TextLayoutService, or the historical single-工 sample when the text box is empty.
        private IList<WritingStroke> BuildWritingStrokes()
        {
            IList<WritingLayoutPage> pages = LayoutAllPages();
            PublishPageList(pages);
            int index = 0;
            if ((regionsEnabled.Checked || pageEnabled.Checked) && writingPages != null && writingPages.Count > 0)
                index = (int)pageSelector.Value - 1;
            return pages[index].Strokes;
        }

        private IList<WritingLayoutPage> LayoutAllPages()
        {
            if (regionsEnabled.Checked)
            {
                return FinishPages(RegionLayoutService.LayoutPages(regions, ReadPage(), ChainResolver(), ResolverFor));
            }
            string text = writingInputText == null ? "" : WritingEditorText.Normalize(writingInputText.Text);
            if (text.Length == 0)
                return FinishPages(new[] { new WritingLayoutPage { Strokes = SampleStrokes(), PageNumber = 1 } });
            double sizeMm = LayoutValue(fontSizeInput, "字号");
            if (sizeMm <= 0)
                throw new ArgumentException("字号必须大于 0（字身高度 mm，小数点使用 . ）：" + fontSizeInput.Text);
            Func<char, BuiltinGlyph> resolver = ChainResolver();
            var hits = GlyphCoverageService.Scan(text, resolver, 0);
            if (hits.Count > 0) throw new ArgumentException(GlyphCoverageService.Describe(hits));
            if (pageEnabled.Checked)
                // A 12mm em contains the complete glyph envelope. The old 14mm line box
                // added two extra millimetres before the operator's requested gap.
                return FinishPages(TextLayoutService.LayoutPages(text, resolver,
                    LayoutValue(letterSpacingInput, "字距"), LayoutValue(lineSpacingInput, "行距"),
                    12.0, sizeMm / BuiltinGlyphLibrary.BodySizeMm, ReadPage()));
            return FinishPages(new[] { new WritingLayoutPage { Strokes = TextLayoutService.Layout(text, resolver,
                LayoutValue(letterSpacingInput, "字距"), LayoutValue(lineSpacingInput, "行距"),
                BuiltinGlyphLibrary.LineHeightMm, sizeMm / BuiltinGlyphLibrary.BodySizeMm), PageNumber = 1 } });
        }
        private IList<WritingLayoutPage> FinishPages(IList<WritingLayoutPage> pages)
        {
            double amount = variationAmount == null ? 0 : LayoutValue(variationAmount, "手写变化");
            if (amount < 0 || amount > WritingVariation.MaxAmountMm) throw new ArgumentException("手写变化幅度必须在 0 到 0.35 mm。0 表示关闭。");
            int seed = 1;
            if (variationSeed != null && variationSeed.Text.Trim().Length > 0 && !int.TryParse(variationSeed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                throw new ArgumentException("手写变化种子必须是整数。");
            if (amount > 0) pages = WritingVariation.ApplyPages(pages, seed, amount, ReadPage());
            int rows = GridCount(gridRowsInput), columns = GridCount(gridColumnsInput);
            if (drawGridLines != null && drawGridLines.Checked && rows > 0 && columns > 0)
                pages = WritingGridService.WithLines(pages, WritingGridService.Lines(ReadPage(), rows, columns));
            return pages;
        }
        private static int GridCount(TextBox box)
        {
            if (box == null || box.Text.Trim().Length == 0) return 0;
            int value;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0 || value > 40)
                throw new ArgumentException("表格行列必须是 0 到 40 的整数。0 表示不用表格。");
            return value;
        }
        private void ImportWritingText()
        {
            Execute(() => {
                using (var dialog = new OpenFileDialog { Filter = "UTF-8 文本 (*.txt)|*.txt|所有文件|*.*" })
                    if (dialog.ShowDialog(DialogOwner) == DialogResult.OK)
                    {
                        writingInputText.Text = WritingEditorText.Normalize(File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)));
                        status.Text = "已导入 UTF-8 文本。请重新生成预览。";
                    }
            });
        }
        private void ShowCoverage()
        {
            Execute(() => {
                var hits = new List<GlyphCoverageHit>();
                if (regionsEnabled.Checked)
                    for (int i = 0; i < regions.Count; i++) hits.AddRange(GlyphCoverageService.Scan(regions[i] == null ? "" : regions[i].Text, ResolverFor(regions[i]), i + 1));
                else hits.AddRange(GlyphCoverageService.Scan(writingInputText.Text, ChainResolver(), 0));
                MessageBox.Show(DialogOwner, GlyphCoverageService.Describe(hits), "缺字报告");
            });
        }
        private void ChooseSupplement()
        {
            Execute(() => {
                using (var dialog = new OpenFileDialog { Filter = "字库包 (*.fontpack.json)|*.fontpack.json" })
                    if (dialog.ShowDialog(DialogOwner) == DialogResult.OK)
                    {
                        supplementPack = GlyphFontPack.LoadFromFile(dialog.FileName);
                        supplementPath = Path.GetFullPath(dialog.FileName);
                        InvalidateWriting();
                        status.Text = "已叠加补充字库「" + supplementPack.Name + "」。原来的字库文件没有被改写。";
                    }
            });
        }
        private void ImportGraphic()
        {
            Execute(() => {
                if (pageTask != null && pageTask.IsActive) throw new InvalidOperationException("多页任务进行中，不能把图片或 SVG 载入主界面。");
                if (importGraphic == null) throw new InvalidOperationException("请从主界面打开本窗口。图片和 SVG 使用主界面原有导入，不进入写字分页。");
                using (PenControlSync.Begin(profile, BuildWritingOptions())) importGraphic();
                status.Text = "已打开主界面导入；笔控仅用于本次转换，不修改设备档或工具表。";
            });
        }
        private void MakeGrid()
        {
            Execute(() => {
                int rows = GridCount(gridRowsInput), columns = GridCount(gridColumnsInput);
                List<WritingRegion> cells = WritingGridService.Cells(ReadPage(), rows, columns);
                RememberRegions();
                regions = cells; regionCanvas.Regions = regions; regionCanvas.Selected = -1; regionsEnabled.Checked = true;
                InvalidateWriting();
                status.Text = (drawGridLines.Checked ? "已生成表格，格线会写进每一页。" : "已生成格子，格线不书写。") + " 移动格子后如要格线跟着走，请重新生成表格。";
            });
        }

        private void PublishPageList(IList<WritingLayoutPage> pages)
        {
            if (regionsEnabled.Checked)
            {
                writingPages = pages;
                updatingPage = true;
                try { pageSelector.Value = Math.Min(pageSelector.Value, writingPages.Count); regionPageSelector.Value = pageSelector.Value; }
                finally { updatingPage = false; }
                var strokes = writingPages[(int)pageSelector.Value - 1].Strokes;
                regionCanvas.Sheet = ReadPage(); regionCanvas.Strokes = strokes; regionCanvas.Invalidate();
                pageTotal.Text = " / " + writingPages.Count + " 页（区域）";
                regionPageTotal.Text = pageTotal.Text;
                return;
            }
            if (pageEnabled.Checked)
            {
                writingPages = pages;
                updatingPage = true;
                try { pageSelector.Value = Math.Min(pageSelector.Value, writingPages.Count); }
                finally { updatingPage = false; }
                pageTotal.Text = " / " + writingPages.Count + " 页";
            }
        }

        // Font selector population: builtin entry plus every discovered pack, by pack name.
        // Any discovery/loading failure keeps the dialog usable with the builtin glyphs only.
        private void InvalidateWriting()
        {
            if (writingText != null) writingText.Clear();
            writingPages = null;
            if (pageTotal != null) pageTotal.Text = " / 未生成";
            if (regionPageTotal != null) regionPageTotal.Text = " / 未生成";
            if (regionCanvas != null) { regionCanvas.Strokes = null; RefreshRegionCanvas(); }
            if (pageTask != null && !suppressTaskInvalidation)
                pageTask.Invalidate("文本、排版、字体、纸张或机器映射已变化，冻结页不能再使用。");
            if (markLoadedStale != null && !suppressTaskInvalidation) markLoadedStale();
            RefreshPageTaskStatus();
        }
        private void UpdatePageMode()
        {
            bool multi = regionsEnabled != null && regionsEnabled.Checked;
            if (multi) pageEnabled.Checked = true;
            startAtOrigin.Enabled = !pageEnabled.Checked;
            if (pageEnabled.Checked) startAtOrigin.Checked = false;
            foreach (TextBox box in pageFields.Values) box.Enabled = pageEnabled.Checked;
            autoWrap.Enabled = pageEnabled.Checked;
            if (pageSelector != null) pageSelector.Enabled = pageEnabled.Checked;
            if (regionPageSelector != null) regionPageSelector.Enabled = multi;
            pageEnabled.Enabled = !multi;
        }
        private PageLayoutOptions ReadPage()
        {
            var page = new PageLayoutOptions { WidthMm = LayoutValue(pageFields["宽"], "纸宽"), HeightMm = LayoutValue(pageFields["高"], "纸高"),
                LeftMm = LayoutValue(pageFields["左"], "左边距"), RightMm = LayoutValue(pageFields["右"], "右边距"),
                TopMm = LayoutValue(pageFields["上"], "上边距"), BottomMm = LayoutValue(pageFields["下"], "下边距"), AutoWrap = autoWrap.Checked };
            page.Validate(); return page;
        }
        private WritingProject CaptureProject()
        {
            var options = BuildWritingOptions();
            string path = fontSelector.SelectedIndex > 0 ? Path.GetFullPath(fontPackPaths[fontSelector.SelectedIndex - 1]) : "";
            var project = new WritingProject { RegionsEnabled = regionsEnabled.Checked, Regions = regions.ConvertAll(r => r.Clone()), SelectedPage = (int)pageSelector.Value, Version = 1, Text = WritingEditorText.Normalize(writingInputText.Text), FontPath = path,
                FontSha256 = path.Length == 0 ? "" : WritingProjectService.Fingerprint(path),
                PageEnabled = pageEnabled.Checked, Page = ReadPage(), FontSizeMm = LayoutValue(fontSizeInput, "字号"),
                LetterSpacingMm = LayoutValue(letterSpacingInput, "字距"), LineSpacingMm = LayoutValue(lineSpacingInput, "行距"),
                AlignStartToOrigin = options.AlignStartToOrigin, ParkEnabled = parkEnabled.Checked,
                ParkX = ParkValue(parkX, "停靠X"), ParkY = ParkValue(parkY, "停靠Y"), PenUpValue = options.PenUpValue,
                PenDownValue = options.PenDownValue, ZMmPerMin = options.ZMmPerMin,
                DrawingMmPerMin = options.DrawingMmPerMin, TravelMmPerMin = options.TravelMmPerMin,
                SupplementPath = supplementPath ?? "", SupplementSha256 = string.IsNullOrEmpty(supplementPath) ? "" : WritingProjectService.Fingerprint(supplementPath),
                VariationSeed = variationSeed == null || !int.TryParse(variationSeed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSeed) ? 1 : parsedSeed,
                VariationMm = variationAmount == null ? 0 : LayoutValue(variationAmount, "手写变化"),
                DrawGridLines = drawGridLines != null && drawGridLines.Checked, GridRows = GridCount(gridRowsInput), GridColumns = GridCount(gridColumnsInput) };
            foreach (WritingRegion region in project.Regions)
                if (region != null && !string.IsNullOrEmpty(region.FontPath)) region.FontSha256 = WritingProjectService.Fingerprint(region.FontPath);
            project.Validate(); return project;
        }
        private void SaveWritingProject()
        {
            Execute(() => {
                WritingProject project = CaptureProject();
                using (var dialog = new SaveFileDialog { Filter = "写字项目 (*.awproject)|*.awproject", DefaultExt = "awproject", FileName = "writing.awproject" })
                    if (dialog.ShowDialog(DialogOwner) == DialogResult.OK) { WritingProjectService.Save(dialog.FileName, project); status.Text = "已保存写字项目；机器配置另行保存，覆盖时保留备份。"; }
            });
        }
        private void OpenWritingProject()
        {
            using (var dialog = new OpenFileDialog { Filter = "写字项目 (*.awproject)|*.awproject" })
                if (dialog.ShowDialog(DialogOwner) == DialogResult.OK) Execute(() => {
                    WritingProject project = WritingProjectService.Load(dialog.FileName);
                    // Resolve and validate the exact font before changing any current editor fields.
                    GlyphFontPack pack = project.FontPath.Length == 0 ? null : GlyphFontPack.LoadFromFile(project.FontPath);
                    int fontIndex = 0;
                    if (pack != null)
                    {
                        int index = fontPackPaths.FindIndex(path => string.Equals(Path.GetFullPath(path), project.FontPath, StringComparison.OrdinalIgnoreCase));
                        if (index < 0) { fontPackPaths.Add(project.FontPath); fontSelector.Items.Add(pack.Name + "（项目字体）"); index = fontPackPaths.Count - 1; }
                        fontIndex = index + 1;
                    }
                    pageEnabled.Checked = project.PageEnabled; autoWrap.Checked = project.Page.AutoWrap;
                    double[] values = { project.Page.WidthMm, project.Page.HeightMm, project.Page.LeftMm, project.Page.RightMm, project.Page.TopMm, project.Page.BottomMm };
                    string[] keys = { "宽", "高", "左", "右", "上", "下" };
                    for (int i = 0; i < keys.Length; i++) pageFields[keys[i]].Text = Number(values[i]);
                    writingInputText.Text = WritingEditorText.Normalize(project.Text); fontSizeInput.Text = Number(project.FontSizeMm);
                    letterSpacingInput.Text = Number(project.LetterSpacingMm); lineSpacingInput.Text = Number(project.LineSpacingMm);
                    startAtOrigin.Checked = project.AlignStartToOrigin; parkEnabled.Checked = project.ParkEnabled;
                    parkX.Text = Number(project.ParkX); parkY.Text = Number(project.ParkY);
                    overrides["penup"].Text = Number(project.PenUpValue); overrides["pendown"].Text = Number(project.PenDownValue);
                    overrides["zfeed"].Text = Number(project.ZMmPerMin); overrides["draw"].Text = Number(project.DrawingMmPerMin);
                    overrides["travel"].Text = Number(project.TravelMmPerMin); fontSelector.SelectedIndex = fontIndex; selectedFontPack = pack;
                    pageSelector.Value = Math.Max(1, Math.Min(100000, project.SelectedPage));
                    regions = project.Regions ?? new List<WritingRegion>(); regionCanvas.Regions = regions; regionCanvas.Selected = -1;
                    regionsEnabled.Checked = project.RegionsEnabled;
                    variationAmount.Text = Number(project.VariationMm);
                    variationSeed.Text = project.VariationSeed.ToString(CultureInfo.InvariantCulture);
                    gridRowsInput.Text = project.GridRows.ToString(CultureInfo.InvariantCulture);
                    gridColumnsInput.Text = project.GridColumns.ToString(CultureInfo.InvariantCulture);
                    drawGridLines.Checked = project.DrawGridLines;
                    supplementPath = project.SupplementPath ?? "";
                    supplementPack = supplementPath.Length == 0 ? null : GlyphFontPack.LoadFromFile(supplementPath);
                    UpdatePageMode(); InvalidateWriting(); status.Text = "已打开写字项目；请确认当前机器配置并重新生成预览。";
                });
        }
        private static string Number(double? value) { return value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : ""; }

        private void PopulateFontSelector()
        {
            fontSelector.Items.Clear();
            fontPackPaths.Clear();
            selectedFontPack = null;
            fontSelector.Items.Add("内置（工王一，示意图形）");
            try
            {
                foreach (string path in GlyphFontPackCatalog.DiscoverPackPaths())
                {
                    try
                    {
                        // Peek, not LoadFromFile: reading name+count costs milliseconds;
                        // a full parse is ~0.5 s per pack and stalled the dialog open
                        // by seconds (2026-10-12 click-stall fix). The real load still
                        // happens - and revalidates everything - when the pack is used.
                        GlyphFontPackInfo probe = GlyphFontPack.Peek(path);
                        fontPackPaths.Add(path);
                        fontSelector.Items.Add(probe.Name + "（" + probe.GlyphCount + " 字）");
                    }
                    catch (Exception err)
                    {
                        // A defective pack must not hide the others; surface it as its own entry.
                        fontPackPaths.Add(path);
                        fontSelector.Items.Add("无法加载: " + Path.GetFileName(path) + "（" + err.Message + "）");
                    }
                }
            }
            catch (Exception)
            {
                // Discovery itself failed (missing/locked folder): builtin only.
            }
            fontSelector.SelectedIndex = 0;
        }

        private GlyphFontPack SelectedPackOrNull()
        {
            int index = fontSelector == null ? -1 : fontSelector.SelectedIndex;
            if (index <= 0 || index > fontPackPaths.Count)
                return null;
            if (selectedFontPack != null)
                return selectedFontPack;
            selectedFontPack = GlyphFontPack.LoadFromFile(fontPackPaths[index - 1]);
            return selectedFontPack;
        }

        private static double LayoutValue(TextBox box, string name)
        {
            string text = box == null ? "" : box.Text.Trim();
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException(name + " 不是有效数值（小数点使用 . ）：" + text);
            return value;
        }

        private void GenerateWritingPreview()
        {
            Execute(() => { InvalidateWriting(); writingText.Text = WritingProgramService.Generate(profile, BuildWritingStrokes(), BuildWritingOptions()); });
        }

        private void CheckContinuousQueue()
        {
            Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(writingText.Text)) throw new InvalidOperationException("请先生成写字预览。");
                double? penDown = Override("pendown") ?? profile.PenDownValue;
                if (!penDown.HasValue) throw new InvalidOperationException("落笔值未知，无法检查连续排队；请显式覆盖或补全配置。");
                var violations = WritingProgramService.FindContinuousViolations(writingText.Text, penDown.Value);
                MessageBox.Show(DialogOwner, violations.Count == 0
                    ? "连续排队检查通过：无 G4 同步块，每个落笔行后紧接 XY 绘制。"
                    : "发现 " + violations.Count + " 项问题：\r\n" + string.Join("\r\n", violations),
                    "连续排队检查", MessageBoxButtons.OK, violations.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });
        }

        private void ExportWritingProgram()
        {
            Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(writingText.Text)) throw new InvalidOperationException("请先生成写字预览。");
                using (var dialog = new SaveFileDialog { Filter = "G-code (*.nc)|*.nc", FileName = "autowriter-writing.nc" })
                    if (dialog.ShowDialog(DialogOwner) == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, writingText.Text);
            });
        }

        // Generate (if needed) and hand the program to the main editor so the existing
        // preview / simulation / send controls can be used. Loading never starts a stream.
        private void LoadWritingIntoMainPreview()
        {
            Execute(() =>
            {
                if (pageTask != null && pageTask.IsActive)
                    throw new InvalidOperationException("多页任务进行中。请用“已换纸并确认位置”准备下一页，或先改动文本/纸张使任务失效后再单独载入。");
                // Always regenerate: the button says "generate and load", and the overrides, the end
                // park and the profile may all have changed since the last preview was produced.
                InvalidateWriting();
                writingText.Text = WritingProgramService.Generate(profile, BuildWritingStrokes(), BuildWritingOptions());
                ApplyPenPreview();

                if (loadIntoPreview == null)
                    throw new InvalidOperationException("当前入口未连接主界面预览；请从主界面 AutoWriter 菜单打开本窗口。");
                loadIntoPreview(writingText.Text);
                PublishSheetOverlay();
                status.Text = "已载入当前第 " + pageSelector.Value + " 页；换纸并确认相同工作原点后手动开始。不会自动发送或自动续页。";
            });
        }

        private int AxisSign(bool xAxis)
        {
            CoordinateMapping map = profile == null ? null : profile.AxisMapping;
            int? value = map == null ? null : (xAxis ? map.XDirection : map.YDirection);
            return value.HasValue && value.Value < 0 ? -1 : 1;
        }

        /// <summary>Paper millimetres (left, down) to work coordinates, the same mapping the generator uses.</summary>
        private PointF PaperToWork(double xMm, double downMm)
        {
            return new PointF((float)(xMm * AxisSign(true)), (float)(-downMm * AxisSign(false)));
        }

        private RectangleF PaperRect(double x, double down, double width, double height)
        {
            PointF a = PaperToWork(x, down), b = PaperToWork(x + width, down + height);
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        private void PublishSheetOverlay()
        {
            if (SheetOverlay == null) return;
            try
            {
                if (!pageEnabled.Checked) { SheetOverlay(null, new RectangleF[0]); return; }
                PageLayoutOptions sheet = ReadPage();
                var boxes = new List<RectangleF>();
                if (regionsEnabled.Checked)
                {
                    foreach (WritingRegion r in regions)
                        if (r != null) boxes.Add(PaperRect(r.X, r.Y, r.Width, r.Height));
                }
                else
                    boxes.Add(PaperRect(sheet.LeftMm, sheet.TopMm, sheet.WidthMm - sheet.LeftMm - sheet.RightMm, sheet.HeightMm - sheet.TopMm - sheet.BottomMm));
                SheetOverlay(PaperRect(0, 0, sheet.WidthMm, sheet.HeightMm), boxes.ToArray());
            }
            catch (Exception) { SheetOverlay(null, new RectangleF[0]); }
        }

        private void ApplyPenStart()
        {
            Execute(() =>
            {
                if (PenStartRequested == null)
                    throw new InvalidOperationException("请从主界面 AutoWriter 菜单打开本窗口。");
                double x = LayoutValue(penStartX, "黑色起点 X"), down = LayoutValue(penStartY, "黑色起点 Y");
                PointF work = PaperToWork(x, down);
                string refusal = PenStartRequested(work.X, work.Y);
                if (refusal != null) throw new InvalidOperationException(refusal);
                penStartX.Text = x.ToString("0.##", CultureInfo.InvariantCulture);
                penStartY.Text = down.ToString("0.##", CultureInfo.InvariantCulture);
                penStartApplied = PenStartSignature(penStartX.Text, penStartY.Text);
                ShowPenStartMarker(x, down);
                status.Text = "黑色起点已设在纸上 X " + x.ToString("0.##", CultureInfo.InvariantCulture) + " mm、向下 " + down.ToString("0.##", CultureInfo.InvariantCulture) + " mm。笔不动，文字不动。";
            });
        }

        private void PenStartBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyPenStart();
            e.Handled = true; e.SuppressKeyPress = true;
        }

        /// <summary>One button, two jobs: apply the typed X/Y when they were edited, otherwise arm click-to-pick on the canvas.</summary>
        private void PenStartButtonClicked()
        {
            if (regionCanvas == null) return;
            if (regionCanvas.PenStartPickMode) { EndPenStartPick(); status.Text = "已取消笔起点点选。"; return; }
            if (penStartApplied == null || PenStartSignature(penStartX.Text, penStartY.Text) != penStartApplied)
            {
                ApplyPenStart();
                status.Text += "（数字已改，按坐标设置预览标记；想图上点选请再点一次「笔起点」。）";
                return;
            }
            TogglePenStartPick();
        }

        private static string PenStartSignature(string x, string y)
        {
            return (x ?? "").Trim() + "|" + (y ?? "").Trim();
        }

        /// <summary>Toggle click-to-pick mode: the next left click on the region canvas becomes the black start point.</summary>
        private void TogglePenStartPick()
        {
            if (regionCanvas == null) return;
            if (regionCanvas.PenStartPickMode) { EndPenStartPick(); status.Text = "已取消笔起点点选。"; return; }
            if (PenStartRequested == null) { status.Text = "请从主界面 AutoWriter 菜单打开本窗口。"; return; }
            if (regionCanvas.Sheet == null || regionCanvas.PageRectangle().IsEmpty) { status.Text = "请先设置有效纸张，再点选笔起点。"; return; }
            regionCanvas.PenStartPickMode = true;
            penStartButton.BackColor = Color.Khaki;
            regionCanvas.Focus();
            regionCanvas.Invalidate();
            status.Text = "在下图点左键，该位置就是笔起点（黑色标记）。右键、Esc 或再点「笔起点」取消。";
        }

        private void EndPenStartPick()
        {
            if (regionCanvas != null) { regionCanvas.PenStartPickMode = false; regionCanvas.Cursor = Cursors.Default; regionCanvas.Invalidate(); }
            if (penStartButton != null) penStartButton.BackColor = Control.DefaultBackColor;
        }

        private void AcceptPickedPenStart(PointF? picked)
        {
            EndPenStartPick();
            if (!picked.HasValue) { status.Text = "已取消笔起点点选，起点未变。"; return; }
            Execute(() =>
            {
                if (PenStartRequested == null)
                    throw new InvalidOperationException("请从主界面 AutoWriter 菜单打开本窗口。");
                double x = Math.Round(picked.Value.X, 1), down = Math.Round(picked.Value.Y, 1);
                penStartX.Text = x.ToString("0.##", CultureInfo.InvariantCulture);
                penStartY.Text = down.ToString("0.##", CultureInfo.InvariantCulture);
                penStartApplied = PenStartSignature(penStartX.Text, penStartY.Text);
                PointF work = PaperToWork(x, down);
                string refusal = PenStartRequested(work.X, work.Y);
                if (refusal != null) throw new InvalidOperationException(refusal);
                ShowPenStartMarker(x, down);
                status.Text = "笔起点已设在纸上 X " + x.ToString("0.##", CultureInfo.InvariantCulture) + " mm、向下 " + down.ToString("0.##", CultureInfo.InvariantCulture) + " mm。笔不动，文字不动。";
            });
        }

        private void ShowPenStartMarker(double xMm, double downMm)
        {
            if (regionCanvas == null) return;
            regionCanvas.PenStartMm = new PointF((float)xMm, (float)downMm);
            regionCanvas.Invalidate();
        }

        private void ReadPenStart()
        {
            if (PenStartQuery == null) return;
            double[] work = PenStartQuery();
            if (work == null || work.Length < 2) return;
            double x = work[0] * AxisSign(true), down = -work[1] * AxisSign(false);
            penStartX.Text = x.ToString("0.##", CultureInfo.InvariantCulture);
            penStartY.Text = down.ToString("0.##", CultureInfo.InvariantCulture);
            penStartApplied = PenStartSignature(penStartX.Text, penStartY.Text);
            ShowPenStartMarker(x, down);
        }

        private void ApplyPenPreview()
        {
            // The 2D view needs the same pen Z values the generator writes, otherwise travel is drawn as a stroke.
            Properties.Settings.Default.gui2DPenZEnable = true;
            WritingProgramOptions effective = BuildWritingOptions();
            double? penUp = effective.PenUpValue ?? profile.PenUpValue;
            double? penDown = effective.PenDownValue ?? profile.PenDownValue;
            if (penUp.HasValue)
                Properties.Settings.Default.gui2DPenZUpValue = penUp.Value;
            if (penDown.HasValue)
                Properties.Settings.Default.gui2DPenZDownValue = penDown.Value;

        }

        private void FreezePageTask()
        {
            if (pageTask == null || loadBoundPage == null)
            {
                status.Text = "请从主界面 AutoWriter 菜单打开本窗口后再冻结多页任务。";
                return;
            }
            Execute(() =>
            {
                List<string> programs = BuildFrozenPrograms();
                string refusal;
                PageTaskBindResult result = pageTask.Bind(BuildTaskFingerprint(), programs, out refusal);
                if (result == PageTaskBindResult.Refused)
                    throw new InvalidOperationException(refusal);
                string program = pageTask.PeekUnloadedProgram();
                if (program == null)
                {
                    status.Text = pageTask.StatusText + " 已冻结，不重复载入。";
                    RefreshPageTaskStatus();
                    return;
                }
                ApplyPenPreview();
                loadBoundPage(program);
                PublishSheetOverlay();
                pageTask.NoteLoaded();
                SyncSelectorToTask();
                status.Text = pageTask.StatusText + " 已载入第 " + pageTask.CurrentPage + " 页。请在主界面手动开始。不会自动回零、定位或发送。";
                RefreshPageTaskStatus();
            });
        }

        private void ConfirmPaperFromSetup()
        {
            if (pageTask == null || loadBoundPage == null)
            {
                status.Text = "请从主界面 AutoWriter 菜单打开本窗口后再确认换纸。";
                return;
            }
            Execute(() =>
            {
                if (pageTask.Phase == PageTaskPhase.WaitingPaper && !pageTask.ConfirmPaper())
                {
                    RefreshPageTaskStatus();
                    return;
                }
                string program = pageTask.PeekUnloadedProgram();
                if (program == null)
                {
                    status.Text = pageTask.StatusText + " 不重复载入，也不会发送。";
                    RefreshPageTaskStatus();
                    return;
                }
                ApplyPenPreview();
                loadBoundPage(program);
                PublishSheetOverlay();
                pageTask.NoteLoaded();
                SyncSelectorToTask();
                status.Text = pageTask.StatusText + " 已载入。请在主界面手动开始。不会自动回零或发送。";
                RefreshPageTaskStatus();
            });
        }

        private List<string> BuildFrozenPrograms()
        {
            IList<WritingLayoutPage> pages = LayoutAllPages();
            PublishPageList(pages);
            WritingProgramOptions options = BuildWritingOptions();
            var programs = new List<string>();
            for (int i = 0; i < pages.Count; i++)
            {
                string body = WritingProgramService.Generate(profile, pages[i].Strokes, options);
                programs.Add("; AutoWriter frozen page " + (i + 1) + "/" + pages.Count + Environment.NewLine + body);
            }
            return programs;
        }

        private string BuildTaskFingerprint()
        {
            WritingProject project = CaptureProject();
            var builder = new StringBuilder();
            builder.Append(project.Text).Append('\n').Append(project.FontPath).Append('\n').Append(project.FontSha256).Append('\n');
            builder.Append(project.RegionsEnabled).Append('|').Append(project.PageEnabled).Append('|').Append(project.AlignStartToOrigin).Append('|').Append(project.ParkEnabled).Append('|');
            AppendNumber(builder, project.Page.WidthMm); AppendNumber(builder, project.Page.HeightMm);
            AppendNumber(builder, project.Page.LeftMm); AppendNumber(builder, project.Page.RightMm);
            AppendNumber(builder, project.Page.TopMm); AppendNumber(builder, project.Page.BottomMm);
            builder.Append(project.Page.AutoWrap).Append('|').Append(project.Page.Alignment).Append('|');
            AppendNumber(builder, project.FontSizeMm); AppendNumber(builder, project.LetterSpacingMm); AppendNumber(builder, project.LineSpacingMm);
            AppendNumber(builder, project.ParkX); AppendNumber(builder, project.ParkY);
            builder.Append(project.PenUpValue).Append('|').Append(project.PenDownValue).Append('|');
            builder.Append(project.ZMmPerMin).Append('|').Append(project.DrawingMmPerMin).Append('|').Append(project.TravelMmPerMin).Append('|');
            if (project.Regions != null)
            {
                foreach (WritingRegion region in project.Regions)
                {
                    builder.Append(region.Text).Append('|').Append(region.PageBehavior).Append('|').Append(region.Alignment).Append('|');
                    AppendNumber(builder, region.X); AppendNumber(builder, region.Y); AppendNumber(builder, region.Width); AppendNumber(builder, region.Height);
                    AppendNumber(builder, region.Left); AppendNumber(builder, region.Right); AppendNumber(builder, region.Top); AppendNumber(builder, region.Bottom);
                    AppendNumber(builder, region.FontSize); AppendNumber(builder, region.LetterGap); AppendNumber(builder, region.LineGap);
                    builder.Append(region.FontPath).Append('|').Append(region.FontSha256).Append('|');
                }
            }
            builder.Append(project.SupplementPath).Append('|').Append(project.SupplementSha256).Append('|');
            builder.Append(project.VariationSeed).Append('|'); AppendNumber(builder, project.VariationMm);
            builder.Append(project.DrawGridLines).Append('|').Append(project.GridRows).Append('|').Append(project.GridColumns).Append('|');
            CoordinateMapping map = profile == null ? null : profile.AxisMapping;
            if (map != null)
            {
                builder.Append(map.XAxis).Append('|').Append(map.YAxis).Append('|').Append(map.ZAxis).Append('|');
                builder.Append(map.XDirection).Append('|').Append(map.YDirection).Append('|').Append(map.ZDirection).Append('|');
                builder.Append(map.WorkOrigin);
            }
            builder.Append('|').Append(profile == null ? "" : profile.ProfileId);
            builder.Append('|').Append(profile == null ? null : profile.PenUpValue);
            builder.Append('|').Append(profile == null ? null : profile.PenDownValue);
            return builder.ToString();
        }

        private static void AppendNumber(StringBuilder builder, double value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private void SyncSelectorToTask()
        {
            if (pageTask == null || pageSelector == null) return;
            decimal page = pageTask.CurrentPage;
            if (page < pageSelector.Minimum) page = pageSelector.Minimum;
            if (page > pageSelector.Maximum) page = pageSelector.Maximum;
            updatingPage = true;
            try
            {
                pageSelector.Value = page;
                if (regionPageSelector != null) regionPageSelector.Value = page;
            }
            finally { updatingPage = false; }
            if (regionCanvas != null && writingPages != null && pageTask.CurrentPage >= 1 && pageTask.CurrentPage <= writingPages.Count)
            {
                regionCanvas.Sheet = ReadPage();
                regionCanvas.Strokes = writingPages[pageTask.CurrentPage - 1].Strokes;
                regionCanvas.Invalidate();
            }
        }

        private void RefreshPageTaskStatus()
        {
            if (pageTaskStatus == null || pageTask == null) return;
            pageTaskStatus.Text = pageTask.StatusText;
            if (confirmPaperButton != null) confirmPaperButton.Enabled = pageTask.PaperButtonEnabled;
        }

        // Built-in sample geometry (three strokes forming "工" inside a 10x10 mm area).
        // It only exercises the generation and continuous-queue checks; it is not a font.
        private static IList<WritingStroke> SampleStrokes()
        {
            var strokes = new List<WritingStroke>();
            strokes.Add(new WritingStroke(new[] { new WritingPoint(1, 11), new WritingPoint(11, 11) }));
            strokes.Add(new WritingStroke(new[] { new WritingPoint(6, 1), new WritingPoint(6, 11) }));
            strokes.Add(new WritingStroke(new[] { new WritingPoint(1, 1), new WritingPoint(11, 1) }));
            return strokes;
        }

        /// <summary>One labelled override input; leaving it empty keeps the profile value.</summary>
        private void AddOverride(UiGroup group, string label, string key)
        {
            TextBox box = group.AddField(label, "", 62);
            box.TextChanged += (sender, args) => InvalidateWriting();
            overrides.Add(key, box);
        }

        /// <summary>Page-size / margin field: adds the label + input pair and wires invalidation.</summary>
        private TextBox PageField(UiGroup group, string label, string text, int width)
        {
            TextBox box = group.AddField(label, text, width);
            box.TextChanged += (sender, args) => InvalidateWriting();
            return box;
        }

        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception error) { MessageBox.Show(DialogOwner, error.Message, "参数或文件错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>
        /// Owner for every dialog. While embedded in the modern main window this form is not a
        /// top-level window, and ShowDialog/MessageBox with such an owner can open BEHIND the
        /// main window (looks like a dead button). TopLevelControl walks up to the real window.
        /// </summary>
        private IWin32Window DialogOwner
        { get { return TopLevelControl ?? (IWin32Window)this; } }

        /// <summary>Switch tabs (0 机器配置, 1 步进计算与校准, 2 写字预览, 3 区域排版).</summary>
        internal void ShowTab(int index)
        {
            if (setupTabs != null && index >= 0 && index < setupTabs.TabCount)
                setupTabs.SelectedIndex = index;
        }

        private double Value(string key)
        {
            double value;
            if (!double.TryParse(inputs[key].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException("请输入有效数值，小数点使用 . ：" + key);
            return value;
        }

        private void AddField(TableLayoutPanel panel, string key, string label, string example)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var box = new TextBox { Text = example, Dock = DockStyle.Fill };
            panel.Controls.Add(box, 1, row);
            inputs.Add(key, box);
        }

        private static void Button(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 30 };
            button.Click += (sender, args) => action();
            panel.Controls.Add(button);
        }
    }

    // The native dropdown list needs WS_HSCROLL in addition to the horizontal extent.
    internal sealed class ScrollableFontComboBox : ComboBox
    {
        private readonly ToolTip nameTip = new ToolTip();
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ComboInfo
        {
            public int Size; public Rect Item, Button; public int State;
            public IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr window, ref ComboInfo info);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal int HorizontalExtent { get; private set; }
        internal bool HorizontalScrollEnabled { get; private set; }
        protected override void OnDropDown(EventArgs e)
        {
            int width = Width;
            foreach (object item in Items)
                width = Math.Max(width, TextRenderer.MeasureText(item.ToString(), Font).Width + 32);
            HorizontalExtent = width;
            DropDownWidth = Math.Min(width, Math.Max(Width, Screen.FromControl(this).WorkingArea.Width / 2));
            ComboInfo info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            if (GetComboBoxInfo(Handle, ref info) && info.List != IntPtr.Zero)
            {
                SetWindowLong(info.List, -16, GetWindowLong(info.List, -16) | 0x100000);
                SendMessage(Handle, 0x015e, (IntPtr)width, IntPtr.Zero);
                SendMessage(info.List, 0x0194, (IntPtr)width, IntPtr.Zero);
                HorizontalScrollEnabled = (GetWindowLong(info.List, -16) & 0x100000) != 0;
            }
            base.OnDropDown(e);
        }
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            nameTip.SetToolTip(this, SelectedItem == null ? "" : SelectedItem.ToString());
            base.OnSelectedIndexChanged(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) nameTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
