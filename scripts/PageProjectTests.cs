using System;
using System.IO;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
internal static class PageProjectTests
{
    static int checks;
    static void Assert(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static void Throws(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Assert(threw, name); }
    static PageLayoutOptions Page(double width, double height) { return new PageLayoutOptions { WidthMm = width + 4, HeightMm = height + 6, LeftMm = 2, RightMm = 2, TopMm = 3, BottomMm = 3, AutoWrap = true }; }
    static IList<WritingStroke> Layout(string text, PageLayoutOptions page) { return TextLayoutService.LayoutPage(text, BuiltinGlyphLibrary.Resolve, 2, 4, 14, 1, page); }
    static string Geometry(IList<WritingStroke> strokes) { string value = ""; foreach (var stroke in strokes) foreach (var p in stroke.Points) value += p.X.ToString("R") + "," + p.Y.ToString("R") + ";"; return value; }
    static string Program(WritingProject p) {
        var profile = new MachineProfile { ProfileId = "offline", DisplayName = "offline", PenControlMode = "Z", PenUpValue = 0, PenDownValue = 5,
            AxisMapping = new CoordinateMapping { WorkOrigin = "G54", XDirection = 1, YDirection = -1 },
            UsableBoundsWorkMm = new CoordinateBounds { MinX = 0, MaxX = 100, MinY = 0, MaxY = 100, MinZ = -1, MaxZ = 6 },
            Speeds = new MachineSpeeds { DrawingMmPerMin = 1000, TravelMmPerMin = 2000, ZMmPerMin = 300 },
            Delays = new MachineDelays { PenUpMs = 0, PenDownMs = 0 }, Capabilities = new MachineCapabilities { RequiresContinuousPenMotion = true } };
        return WritingProgramService.Generate(profile, Layout(p.Text, p.Page), new WritingProgramOptions { ParkPosition = new WritingPoint(p.ParkX, p.ParkY) });
    }
    static int Main(string[] args) {
        var exact = Page(26, 12); var two = Layout("工王", exact);
        Assert(two.Count == 7, "all strokes fit exact width and height");
        Assert(two[0].Points[0].X == 3 && two[0].Points[0].Y == -3, "margin top-left coordinate");
        Assert(two[3].Points[0].X == 17, "spacing between cells");
        var wrap = Layout("工王", Page(25.9, 30));
        Assert(wrap[3].Points[0].X == 3 && wrap[3].Points[0].Y == -17, "automatic wrap retains character");
        Throws(() => Layout("工王", Page(25.9, 23.9)), "vertical overflow rejects entire layout");
        var noWrap = Page(25, 40); noWrap.AutoWrap = false; Throws(() => Layout("工王", noWrap), "wrap disabled");
        Throws(() => Layout("工", Page(11.9, 12)), "oversized cell");
        Assert(Geometry(Layout("工\r\n王", Page(12,30))) == Geometry(Layout("工\r王", Page(12,30))) && Geometry(Layout("工\r王",Page(12,30))) == Geometry(Layout("工\n王",Page(12,30))), "linebreak normalization");
        Throws(() => Layout("工\t王", Page(100,100)), "tab explicit error");
        Throws(() => Layout("工好", Page(100,100)), "missing glyph error");
        Throws(() => Layout("   ", Page(100,100)), "spaces only no sample");
        Assert(Layout("工 王", Page(40,12)).Count == 7, "space retained advance");
        Throws(() => Layout("工\n", Page(12,12)), "trailing explicit line overflow");
        var close = TextLayoutService.LayoutPage("工\n王", BuiltinGlyphLibrary.Resolve, 2, 0, 14, 1, Page(12,20));
        Assert(close[0].Points[0].Y == -3 && close[3].Points[0].Y == -13, "zero gap touches actual ink bounds");
        var gap = TextLayoutService.LayoutPage("工\n王", BuiltinGlyphLibrary.Resolve, 2, 2, 14, 1, Page(12,22));
        Assert(gap[3].Points[0].Y == -15, "two mm actual ink gap");
        Assert(Geometry(Layout("工\r\r\n王",Page(12,30))) == Geometry(Layout("工\n王",Page(12,30))), "Windows soft CRCRLF single newline");
        var blank = TextLayoutService.LayoutPage("工\n\n王", BuiltinGlyphLibrary.Resolve, 2, 2, 14, 1, Page(12,34));
        Assert(blank[3].Points[0].Y == -27, "blank line reserves body height and gaps");
        var mixed = TextLayoutService.LayoutPage("一\n工", BuiltinGlyphLibrary.Resolve, 2, 2, 14, 1, Page(12,12));
        Assert(mixed[1].Points[0].Y == -5, "different row ink heights use visible gap");
        var bad = Page(10,10); bad.LeftMm = double.NaN; Throws(() => bad.Validate(), "nonfinite margin");
        var project = new WritingProject { SelectedPage = 2, Version = 1, Text = "工王\n一", FontPath = "", FontSha256 = "", PageEnabled = true, Page = Page(26,30), FontSizeMm = 10, LetterSpacingMm = 2, LineSpacingMm = 4, ParkEnabled = true, ParkX = 0, ParkY = 0, DrawingMmPerMin = 1200 };
        string path = Path.Combine(args[0], "roundtrip.awproject"); WritingProjectService.Save(path,project);
        var loaded = WritingProjectService.Load(path);
        Assert(loaded.SelectedPage == 2, "selected page roundtrip");
        Assert(loaded.Text == project.Text && loaded.DrawingMmPerMin == 1200 && loaded.Page.AutoWrap, "options roundtrip");
        Assert(Geometry(Layout(loaded.Text,loaded.Page)) == Geometry(Layout(project.Text,project.Page)), "geometry deterministic roundtrip");
        Assert(Program(loaded) == Program(project), "gcode deterministic roundtrip");
        Assert(Program(loaded).Contains("X3 Y3"), "negative paper Y maps positive machine Y");
        Assert(WritingProgramService.FindContinuousViolations(Program(loaded),5).Count == 0, "queue invariant");
        string original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("\"SelectedPage\":2,", ""));
        Assert(WritingProjectService.Load(path).SelectedPage == 0, "old v1 project optional page defaults");
        File.WriteAllText(path, original);
        WritingProjectService.Save(path,project);
        Assert(Directory.GetFiles(args[0],"roundtrip.awproject.bak-*").Length == 1, "backup kept");
        foreach (string invalid in new[] { "{", "{}", original.Replace("\"Version\":1", "\"Version\":2"), original.Replace("\"FontSizeMm\":10,", "") }) {
            File.WriteAllText(path,invalid); Throws(() => WritingProjectService.Load(path), "corrupt/missing/version project");
        }
        project.FontPath = Path.Combine(args[0],"font.json"); File.WriteAllText(project.FontPath,"font"); project.FontSha256 = WritingProjectService.Fingerprint(project.FontPath); WritingProjectService.Save(path,project);
        File.WriteAllText(project.FontPath,"changed"); Throws(() => WritingProjectService.Load(path), "changed font rejected");
        project.FontPath += ".missing"; Throws(() => WritingProjectService.ValidateFont(project), "missing font rejected");
        project.AlignStartToOrigin = true; Throws(() => project.Validate(), "page alignment conflict");
        Console.WriteLine("PASS: " + checks + " page/project checks"); return 0;
    }
}
