using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Services;
using GrblPlotter.AutoWriter.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

class PageFeaturesTests
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    static BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Get(Form f, string name) { return f.GetType().GetField(name, flags).GetValue(f); }
    static object Call(Form f, string name) { return f.GetType().GetMethod(name, flags).Invoke(f, null); }
    static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    [STAThread] static int Main(string[] args)
    {
        try { Run(args[0]); Console.WriteLine("PASS " + checks + " page feature checks"); return 0; }
        catch (Exception e) { while (e.InnerException != null) e = e.InnerException; Console.Error.WriteLine(e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); return 1; }
    }
    static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var sheet = new PageLayoutOptions { WidthMm = 183, HeightMm = 253, LeftMm = 10, RightMm = 10, TopMm = 10, BottomMm = 10, AutoWrap = true, LinesPerPage = 28, FontHeightPercent = 95 };
        string text = string.Join("\n", Enumerable.Repeat("工王", 29).ToArray());
        var pages = TextLayoutService.LayoutPages(text, BuiltinGlyphLibrary.Resolve, 1, 999, 12, 1, sheet);
        Check(pages.Count == 2 && pages[0].LineCount == 28 && pages[1].LineCount == 1, "28 rows then next sheet");
        double step = 233.0 / 28;
        Check(Math.Abs(pages[0].Strokes[0].Points[0].Y - pages[0].Strokes[7].Points[0].Y - step) < 1e-8, "computed row pitch");
        var spaced = TextLayoutService.LayoutPages("工\n\n王", BuiltinGlyphLibrary.Resolve, 1, 0, 12, 1, sheet);
        Check(spaced[0].LineCount == 3, "blank row occupies slot");
        sheet.FontHeightPercent = 0;
        Reject(() => TextLayoutService.LayoutPages("工", BuiltinGlyphLibrary.Resolve, 1, 0, 12, 1, sheet), "oversized glyph rejected");
        sheet.FontHeightPercent = 95;
        sheet.RotationDegrees = 180;
        var rotated = TextLayoutService.RotatePages(pages, sheet);
        var before = pages[0].Strokes[0].Points[0]; var after = rotated[0].Strokes[0].Points[0];
        Check(Math.Abs(before.X + after.X - 183) < 1e-8 && Math.Abs(before.Y + after.Y + 253) < 1e-8, "180 degrees about page center");
        Check(pages[0].Strokes[0].Points[0].X == before.X, "rotation preserves original data");
        sheet.RotationDegrees = 90;
        Reject(() => TextLayoutService.RotatePages(pages, sheet), "rotation outside sheet rejected");
        var square = new PageLayoutOptions { WidthMm = 253, HeightMm = 253, RotationDegrees = 90 };
        var quarter = TextLayoutService.RotatePages(pages, square);
        Check(Math.Abs(quarter[0].Strokes[0].Points[0].X - (253 + before.Y)) < 1e-8, "90 degrees coordinate");
        square.RotationDegrees = 270;
        var three = TextLayoutService.RotatePages(pages, square);
        Check(Math.Abs(three[0].Strokes[0].Points[0].Y - (before.X - 253)) < 1e-8, "270 degrees coordinate");
        sheet.RotationDegrees = 180;
        string pdf = Path.Combine(folder, "writing-28-lines.pdf");
        WritingPdfService.Export(pdf, rotated, sheet);
        using (var document = PdfDocument.Open(pdf))
        {
            Check(document.NumberOfPages == 2, "PDF page count");
            Check(Math.Abs(document.GetPage(1).Width - 183 * 72 / 25.4) < 0.001, "PDF physical width");
            Check(document.GetPage(1).Paths.Count > 0, "PDF vector paths");
            Check(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(document.GetPage(1)).Contains("工王"), "PDF has extractable Chinese text layer");
            Check(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(document.GetPage(2)).Trim() == "工王", "second page text layer contains only its own text");
        }
        Check(WritingPdfService.ImportText(pdf) == text, "own exported PDF restores original text and newlines");
        var oldBuilder = new PdfDocumentBuilder();
        oldBuilder.AddPage(300, 300).DrawLine(new PdfPoint(10, 10), new PdfPoint(30, 30), 1);
        string legacyPdf = Path.Combine(folder, "legacy-vector-only.pdf"); File.WriteAllBytes(legacyPdf, oldBuilder.Build());
        Reject(() => WritingPdfService.ImportText(legacyPdf), "legacy vector-only PDF still requires OCR");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(300, 300).AddText("First page", 12, new PdfPoint(20, 200), font);
        builder.AddPage(300, 300).AddText("Second page", 12, new PdfPoint(20, 200), font);
        string input = Path.Combine(folder, "text-input.pdf"); File.WriteAllBytes(input, builder.Build());
        string imported = WritingPdfService.ImportText(input);
        Check(imported.Contains("First page") && imported.Contains("Second page"), "PDF text from all pages");
        string chinese = Path.Combine(folder, "chinese-input.pdf");
        if (File.Exists(chinese)) Check(WritingPdfService.ImportText(chinese).Contains("中文导入测试：工王一"), "Chinese CID PDF text extraction");
        var service = new RegionTemplateService(Path.Combine(folder, "templates"));
        var template = RegionTemplate.Create("delete-test", sheet, new[] { new WritingRegion { Text = "工", X = 10, Y = 10, Width = 50, Height = 50, FontSize = 5 } });
        service.Save(template); service.Delete(template); string warning;
        Check(service.List(out warning).Count == 0, "template deleted");
        var assembly = typeof(TextLayoutService).Assembly;
        Type type = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);
        string loadedCode = null;
        Action<string> sink = code => loadedCode = code;
        using (var form = (Form)Activator.CreateInstance(type, flags, null, new object[] { sink, Path.Combine(folder, "state-" + Guid.NewGuid().ToString("N") + ".json") }, null))
        {
            form.Show(); Application.DoEvents();
            var fields = (Dictionary<string, TextBox>)Get(form, "pageFields");
            fields["宽"].Text = "183"; fields["高"].Text = "253";
            fields["上"].Text = "0"; fields["下"].Text = "0";
            fields["行数"].Text = "28"; fields["字高比例"].Text = "95";
            ((TextBox)Get(form, "writingInputText")).Text = text;
            var uiPages = (IList)Call(form, "LayoutAllPages");
            Check(uiPages.Count == 2, "UI uses fixed rows");
            string fingerprint = (string)Call(form, "BuildTaskFingerprint");
            fields["旋转"].Text = "180";
            var uiRotated = (IList<WritingLayoutPage>)Call(form, "LayoutAllPages");
            var uiOriginal = ((IList<WritingLayoutPage>)uiPages)[0].Strokes[0].Points[0];
            Check(Math.Abs(uiRotated[0].Strokes[0].Points[0].X + uiOriginal.X - 183) < 1e-8, "UI output geometry rotates");
            Check((string)Call(form, "BuildTaskFingerprint") != fingerprint, "rotation invalidates bound task fingerprint");
            Check(!((TextBox)Get(form, "lineSpacingInput")).Enabled && !((TextBox)Get(form, "fontSizeInput")).Enabled, "mutually exclusive layout controls");
            var project = (WritingProject)Call(form, "CaptureProject");
            string path = Path.Combine(folder, "rows.awproject");
            WritingProjectService.Save(path, project);
            var reopened = WritingProjectService.Load(path);
            Check(reopened.Page.LinesPerPage == 28 && reopened.Page.FontHeightPercent == 95 && reopened.Page.RotationDegrees == 180, "project round trip");
            var json = File.ReadAllText(path);
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"(LinesPerPage|FontHeightPercent|RotationDegrees)\":[0-9.]+,?", "");
            json = json.Replace(",}", "}");
            string legacy = Path.Combine(folder, "legacy.awproject"); File.WriteAllText(legacy, json);
            var old = WritingProjectService.Load(legacy);
            Check(old.Page.LinesPerPage == 0 && old.Page.RotationDegrees == 0, "old project uses original layout defaults");
            type.GetMethod("OpenProjectFromShell", flags | BindingFlags.Public).Invoke(form, new object[] { path });
            Check(fields["行数"].Text == "28" && (string)type.GetProperty("ProjectFilePath", flags | BindingFlags.Public).GetValue(form, null) == path, "unified open restores project and filename");
            Call(form, "SaveLastState"); fields["行数"].Text = "0"; fields["字高比例"].Text = "0";
            Call(form, "RestoreLastState");
            Check(fields["行数"].Text == "28" && fields["字高比例"].Text == "95", "last editor state restored");
            ((TabControl)Get(form, "setupTabs")).SelectedIndex = 2; Application.DoEvents();
            fields["行数"].Focus(); Application.DoEvents();
            var caption = fields["字高比例"].Parent.Controls.OfType<Label>().Single();
            Check(caption.Text.Contains("%") && caption.Width >= TextRenderer.MeasureText(caption.Text, caption.Font).Width, "percent caption fits rendered text");
            var profile = (MachineProfile)Get(form, "profile");
            profile.PenControlMode = "Z"; profile.PenUpValue = 0; profile.PenDownValue = 5;
            profile.Speeds = new MachineSpeeds { DrawingMmPerMin = 1000, TravelMmPerMin = 1800, ZMmPerMin = 10000 };
            Call(form, "BuildWritingStrokes");
            type.GetMethod("ChangePreviewPage", flags | BindingFlags.Public).Invoke(form, new object[] { 1 });
            Check(((NumericUpDown)Get(form, "pageSelector")).Value == 2 && !string.IsNullOrEmpty(loadedCode), "preview next loads second page");
            type.GetMethod("ChangePreviewPage", flags | BindingFlags.Public).Invoke(form, new object[] { -1 });
            Check(((NumericUpDown)Get(form, "pageSelector")).Value == 1, "preview previous loads first page");
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "page-settings.png")); }
            fields["行数"].Text = "0"; fields["字高比例"].Text = "0";
            Check(((TextBox)Get(form, "lineSpacingInput")).Enabled && ((TextBox)Get(form, "fontSizeInput")).Enabled, "manual mode restored");
            string wizardText = null; int wizardCalls = 0;
            type.GetProperty("OpenTextWizard", flags | BindingFlags.Public).SetValue(form, new Action<string>(value => { wizardText = value; wizardCalls++; }), null);
            var wizard = Descendants(form).OfType<Button>().Single(button => button.Text == "从文本创建G代码");
            wizard.PerformClick(); wizard.PerformClick();
            Check(wizardCalls == 2 && wizardText == WritingEditorText.Normalize(((TextBox)Get(form, "writingInputText")).Text), "wizard can reopen with existing text");
            var editor = new ExternalTextEditor { Name = "Test", Program = "test.exe", Arguments = "--reuse-window {file}" };
            Check(editor.BuildArguments("C:\\test folder\\正文.txt") == "--reuse-window \"C:\\test folder\\正文.txt\"", "external editor file argument quoted");
            editor.Arguments = "--wait";
            Check(editor.BuildArguments("C:\\test folder\\正文.txt").EndsWith("\"C:\\test folder\\正文.txt\""), "missing file placeholder appends path");
            string external = Path.Combine(folder, "external-edit.txt");
            File.WriteAllText(external, "外部编辑后的文字", new System.Text.UTF8Encoding(false));
            type.GetField("externalTextFile", flags).SetValue(form, external);
            type.GetField("externalTextOriginal", flags).SetValue(form, WritingEditorText.Normalize(((TextBox)Get(form, "writingInputText")).Text));
            Call(form, "ReloadExternalText");
            Check(((TextBox)Get(form, "writingInputText")).Text == "外部编辑后的文字" && Get(form, "writingPages") == null, "external edits reload and invalidate generated pages");
            ((TextBox)Get(form, "writingInputText")).Focus(); Application.DoEvents();
            var editorPanel = (Panel)Get(form, "externalEditorButtons");
            foreach (Control row in editorPanel.Controls)
            {
                var buttons = row.Controls.OfType<Button>().OrderBy(button => button.Left).ToArray();
                Check(buttons.Length > 0 && buttons[0].Left == 0 && buttons[buttons.Length - 1].Right == row.ClientSize.Width, "editor row fills available width");
                if (buttons.Length == 2) Check(Math.Abs(buttons[0].Width - buttons[1].Width) <= 1 && buttons[0].Top == buttons[1].Top, "editor columns equal width and baseline");
            }
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "text-editor-actions.png")); }
        }
    }
}
