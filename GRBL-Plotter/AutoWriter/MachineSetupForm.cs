using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;

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
        private ComboBox fontSelector;
        private readonly List<string> fontPackPaths = new List<string>();
        private GlyphFontPack selectedFontPack;
        // Optional sink that loads a generated writing program into the main G-code editor so the
        // existing preview / simulation / send pipeline can be used. Null in an unconnected shell.
        private readonly Action<string> loadIntoPreview;

        internal MachineSetupForm()
            : this(null)
        { }

        internal MachineSetupForm(Action<string> loadIntoPreview)
        {
            this.loadIntoPreview = loadIntoPreview;
            Text = "AutoWriter — 机器配置与校准";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            Size = new Size(840, 670);
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var machineTab = new TabPage("机器配置");
            var calibrationTab = new TabPage("步进参数计算");
            tabs.TabPages.AddRange(new[] { machineTab, calibrationTab });
            var programTab = new TabPage("校准程序预览");
            tabs.TabPages.Add(programTab);
            var programText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            programPreview = programText;
            properties.PropertyValueChanged += (sender, args) => { programPreview.Clear(); RefreshApplyReportQuiet(); };
            var programButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, AutoSize = true };
            var startX = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Width = 90 };
            var startY = new NumericUpDown { DecimalPlaces = 3, Minimum = -100000, Maximum = 100000, Width = 90 };
            programButtons.Controls.Add(new Label { Text = "起点/中心 X、Y（工作坐标 mm）", AutoSize = true });
            programButtons.Controls.Add(startX);
            programButtons.Controls.Add(startY);
            Button(programButtons, "10mm短线", () => Execute(() => programText.Text = CalibrationProgramService.GenerateShortLine(profile, (double)startX.Value, (double)startY.Value, 10)));
            Button(programButtons, "十字（臂长5mm）", () => Execute(() => programText.Text = CalibrationProgramService.GenerateCross(profile, (double)startX.Value, (double)startY.Value, 5)));
            Button(programButtons, "10mm方框", () => Execute(() => programText.Text = CalibrationProgramService.GenerateSquare10Mm(profile, (double)startX.Value, (double)startY.Value)));
            Button(programButtons, "导出程序", () => Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(programText.Text)) throw new InvalidOperationException("请先生成程序。");
                using (var dialog = new SaveFileDialog { Filter = "G-code (*.nc)|*.nc", FileName = "calibration.nc" })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, programText.Text);
            }));
            programTab.Controls.Add(programText);
            programTab.Controls.Add(programButtons);

            var applyTab = new TabPage("应用与写字预览");
            tabs.TabPages.Add(applyTab);
            var applyToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, AutoSize = true, WrapContents = true };
            Button(applyToolbar, "刷新应用报告", RefreshApplyReport);
            applyToolbar.Controls.Add(new Label { Text = "显式覆盖（留空用配置值）：Z抬", AutoSize = true });
            AddOverride(applyToolbar, "penup");
            applyToolbar.Controls.Add(new Label { Text = "Z落", AutoSize = true });
            AddOverride(applyToolbar, "pendown");
            applyToolbar.Controls.Add(new Label { Text = "Z速", AutoSize = true });
            AddOverride(applyToolbar, "zfeed");
            applyToolbar.Controls.Add(new Label { Text = "绘图", AutoSize = true });
            AddOverride(applyToolbar, "draw");
            applyToolbar.Controls.Add(new Label { Text = "空移", AutoSize = true });
            AddOverride(applyToolbar, "travel");
            var applyButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, AutoSize = true };
            Button(applyButtons, "生成写字预览（示例笔画）", GenerateWritingPreview);
            Button(applyButtons, "检查连续排队", CheckContinuousQueue);
            Button(applyButtons, "导出写字程序", ExportWritingProgram);
            Button(applyButtons, "生成并载入主界面预览", LoadWritingIntoMainPreview);
            parkEnabled = new CheckBox { Text = "写完回原点（抬笔）至 X", Checked = true, AutoSize = true };
            parkX = new TextBox { Text = "0", Width = 46 };
            parkY = new TextBox { Text = "0", Width = 46 };
            applyButtons.Controls.Add(parkEnabled);
            applyButtons.Controls.Add(parkX);
            applyButtons.Controls.Add(new Label { Text = "Y", AutoSize = true });
            applyButtons.Controls.Add(parkY);
            startAtOrigin = new CheckBox { Text = "起笔点=原点（第一个落笔点 X0 Y0）", Checked = true, AutoSize = true };
            applyButtons.Controls.Add(startAtOrigin);
            // Multi-character row: text + letter/line spacing feed TextLayoutService; an empty text
            // keeps the historical single-工 sample so existing checks and habits still work.
            applyButtons.Controls.Add(new Label { Text = "字体", AutoSize = true });
            fontSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            PopulateFontSelector();
            fontSelector.SelectedIndexChanged += (sender, args) => { selectedFontPack = null; };
            applyButtons.Controls.Add(fontSelector);
            applyButtons.Controls.Add(new Label { Text = "文本（内置可用：" + BuiltinGlyphLibrary.AvailableSymbols + "）", AutoSize = true });
            writingInputText = new TextBox { Text = "", Width = 110 };
            applyButtons.Controls.Add(writingInputText);
            applyButtons.Controls.Add(new Label { Text = "字距mm", AutoSize = true });
            letterSpacingInput = new TextBox { Text = "2", Width = 40 };
            applyButtons.Controls.Add(letterSpacingInput);
            applyButtons.Controls.Add(new Label { Text = "行距mm", AutoSize = true });
            lineSpacingInput = new TextBox { Text = "4", Width = 40 };
            applyButtons.Controls.Add(lineSpacingInput);
            applyButtons.Controls.Add(new Label { Text = "字号mm(字身高)", AutoSize = true });
            fontSizeInput = new TextBox { Text = "10", Width = 40 };
            applyButtons.Controls.Add(fontSizeInput);
            applyText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false };
            writingText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            var applySplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            applySplit.Panel1.Controls.Add(applyText);
            applySplit.Panel2.Controls.Add(writingText);
            // SplitterDistance must be set after the control has a real size, and may still be
            // clamped by the panel minimum sizes; keep it non-fatal so the dialog always opens.
            Load += (sender, args) => { try { applySplit.SplitterDistance = Math.Max(applySplit.Panel1MinSize, applySplit.Height / 2); } catch (ArgumentOutOfRangeException) { } };
            applyTab.Controls.Add(applySplit);
            applyTab.Controls.Add(applyButtons);
            applyTab.Controls.Add(applyToolbar);

            Controls.Add(tabs);
            Controls.Add(status);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, AutoSize = true };
            Button(toolbar, "新建（未验证）", NewProfile);
            Button(toolbar, "打开配置", OpenProfile);
            Button(toolbar, "保存配置", SaveProfile);
            machineTab.Controls.Add(properties);
            machineTab.Controls.Add(toolbar);
            var note = new Label { Dock = DockStyle.Bottom, Height = 65,
                Text = "空值表示尚未验证。配置保存不会连接机器或写入固件。\r\n展开 AxisMapping / Speeds / Delays / Capabilities 设置参数。截图数值只能记录为候选。\r\n读取实机：用 --inspect-machine 启动，再手动连接；该模式只允许查询。" };
            machineTab.Controls.Add(note);

            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            AddField(fields, "angle", "步距角（度）", "1.8");
            AddField(fields, "micro", "驱动细分", "1");
            AddField(fields, "pitch", "同步带节距（mm）", "2");
            AddField(fields, "teeth", "同步轮齿数", "16");
            AddField(fields, "lead", "丝杆导程（mm/圈）", "8");
            AddField(fields, "pulses", "内部每圈脉冲数", "7200");
            AddField(fields, "numerator", "电子齿轮分子 N（内部 = 外部 × N/D）", "1");
            AddField(fields, "denominator", "电子齿轮分母 D", "1");
            AddField(fields, "old", "原步数/mm（实测修正）", "");
            AddField(fields, "commanded", "指令距离（mm）", "");
            AddField(fields, "measured", "实测距离（mm）", "");
            var calculations = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
            Button(calculations, "同步带计算", () => Calculate(() => MotionCalibrationService.CalculateBelt(Value("angle"), Value("micro"), Value("pitch"), Value("teeth"))));
            Button(calculations, "丝杆计算", () => Calculate(() => MotionCalibrationService.CalculateScrew(Value("angle"), Value("micro"), Value("lead"))));
            Button(calculations, "电子齿轮计算", () => Calculate(() => MotionCalibrationService.CalculateEffective(Value("pulses"), Value("lead"), Value("numerator"), Value("denominator"))));
            Button(calculations, "实测修正", () => Execute(() => result.Text = "修正步数/mm：" + MotionCalibrationService.CorrectMeasuredStepsPerMm(Value("old"), Value("commanded"), Value("measured")).ToString("G12", CultureInfo.InvariantCulture)));
            calibrationTab.Controls.Add(fields);
            calibrationTab.Controls.Add(calculations);
            calibrationTab.Controls.Add(result);
            NewProfile();
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
                if (dialog.ShowDialog(this) == DialogResult.OK) Execute(() =>
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
                if (dialog.ShowDialog(this) == DialogResult.OK) Execute(() =>
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
            catch (Exception error) { MessageBox.Show(this, error.Message, "应用报告", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
                AlignStartToOrigin = startAtOrigin == null || startAtOrigin.Checked
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
            string text = writingInputText == null ? "" : writingInputText.Text;
            if (string.IsNullOrWhiteSpace(text))
                return SampleStrokes();
            double sizeMm = LayoutValue(fontSizeInput, "字号");
            if (sizeMm <= 0)
                throw new ArgumentException("字号必须大于 0（字身高度 mm，小数点使用 . ）：" + fontSizeInput.Text);
            Func<char, BuiltinGlyph> resolver = BuiltinGlyphLibrary.Resolve;
            GlyphFontPack pack = SelectedPackOrNull();
            if (pack != null)
            {
                RequireGlyphsAvailable(pack, text);
                resolver = pack.ToResolver();
            }
            return TextLayoutService.Layout(text, resolver,
                LayoutValue(letterSpacingInput, "字距"), LayoutValue(lineSpacingInput, "行距"),
                BuiltinGlyphLibrary.LineHeightMm, sizeMm / BuiltinGlyphLibrary.BodySizeMm);
        }

        // Font selector population: builtin entry plus every discovered pack, by pack name.
        // Any discovery/loading failure keeps the dialog usable with the builtin glyphs only.
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
                        GlyphFontPack probe = GlyphFontPack.LoadFromFile(path);
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

        // Precheck BEFORE layout so the operator sees every missing character at once
        // instead of one throw per generation click.
        private static void RequireGlyphsAvailable(GlyphFontPack pack, string text)
        {
            var missing = new SortedSet<char>();
            foreach (char symbol in text)
            {
                if (symbol == '\r' || symbol == '\n')
                    continue;
                if (!pack.ChainHasGlyph(symbol))
                    missing.Add(symbol);
            }
            if (missing.Count == 0)
                return;
            var shown = new List<char>(missing);
            string preview = string.Join(" ", shown.ConvertAll(c => c.ToString()).GetRange(0, Math.Min(shown.Count, 30)).ToArray());
            throw new ArgumentException(
                "字库「" + pack.Name + "」缺 " + shown.Count + " 个字：" + preview +
                (shown.Count > 30 ? " …" : "") +
                "。用 scripts/generate-stage2-fontpack.py 扩包后再写，或改选内置字体。");
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
            Execute(() => { writingText.Text = WritingProgramService.Generate(profile, BuildWritingStrokes(), BuildWritingOptions()); });
        }

        private void CheckContinuousQueue()
        {
            Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(writingText.Text)) throw new InvalidOperationException("请先生成写字预览。");
                double? penDown = Override("pendown") ?? profile.PenDownValue;
                if (!penDown.HasValue) throw new InvalidOperationException("落笔值未知，无法检查连续排队；请显式覆盖或补全配置。");
                var violations = WritingProgramService.FindContinuousViolations(writingText.Text, penDown.Value);
                MessageBox.Show(this, violations.Count == 0
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
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, writingText.Text);
            });
        }

        // Generate (if needed) and hand the program to the main editor so the existing
        // preview / simulation / send controls can be used. Loading never starts a stream.
        private void LoadWritingIntoMainPreview()
        {
            Execute(() =>
            {
                // Always regenerate: the button says "generate and load", and the overrides, the end
                // park and the profile may all have changed since the last preview was produced.
                writingText.Text = WritingProgramService.Generate(profile, BuildWritingStrokes(), BuildWritingOptions());

                // Keep the 2D view's pen up/down detection in step with the profile that produced this
                // program: the generator writes the profile's pen Z values (0 = up, 5 = down here) and
                // the view needs them to tell a travel move from a drawn stroke. Without this the whole
                // program - travel included - is drawn as one pen-down path. See PenZClassifier.
                Properties.Settings.Default.gui2DPenZEnable = true;
                if (profile.PenUpValue.HasValue)
                    Properties.Settings.Default.gui2DPenZUpValue = profile.PenUpValue.Value;
                if (profile.PenDownValue.HasValue)
                    Properties.Settings.Default.gui2DPenZDownValue = profile.PenDownValue.Value;

                if (loadIntoPreview == null)
                    throw new InvalidOperationException("当前入口未连接主界面预览；请从主界面 AutoWriter 菜单打开本窗口。");
                loadIntoPreview(writingText.Text);
                status.Text = "已载入主界面 G-code 编辑器：用原有“模拟/发送”检查，本窗口不会自动发送。";
            });
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

        private void AddOverride(FlowLayoutPanel panel, string key)
        {
            var box = new TextBox { Width = 66 };
            panel.Controls.Add(box);
            overrides.Add(key, box);
        }

        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "参数或文件错误", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
}
