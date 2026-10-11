using System;
using System.IO;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
class WritingNextTests {
 static int checks;
 static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
 static void Throws(Action action, string name) { try { action(); } catch { checks++; return; } throw new Exception(name); }
 static BuiltinGlyph Mark(char symbol, double advance) {
  return new BuiltinGlyph { Symbol = symbol, AdvanceMm = advance, Strokes = new List<WritingStroke> { new WritingStroke(new[] { new WritingPoint(0, 1), new WritingPoint(Math.Min(advance, 1), 1) }) } };
 }
 static BuiltinGlyph Resolve(char symbol) {
  if (symbol == ' ') return new BuiltinGlyph { Symbol = ' ', AdvanceMm = 4, Strokes = new List<WritingStroke>() };
  if ("AB.HiYou".IndexOf(symbol) >= 0 || symbol == 'H' || symbol == 'i' || symbol == 'Y' || symbol == 'o' || symbol == 'u') return Mark(symbol, symbol == '.' ? 2 : symbol == ' ' ? 4 : 10);
  throw new InvalidOperationException("缺字" + symbol);
 }
 static PageLayoutOptions Sheet(double width) { return new PageLayoutOptions { WidthMm = width, HeightMm = 40, LeftMm = 0, RightMm = 0, TopMm = 0, BottomMm = 0, AutoWrap = true }; }
 static int Main() { try { Run(); Console.WriteLine("PASS " + checks + " writing-next checks"); return 0; } catch (Exception error) { Console.WriteLine(error); return 1; } }
 static void Run() {
  bool periodWithLetter = false, wordMoved = false;
  foreach (var stroke in TextLayoutService.LayoutPage("AB.", Resolve, 0, 0, 12, 1, Sheet(20)))
   if (stroke.Points[0].X > 5) periodWithLetter = true;
  foreach (var stroke in TextLayoutService.LayoutPage("Hi You", Resolve, 0, 0, 12, 1, Sheet(36)))
   if (stroke.Points[0].X > 15 && stroke.Points[0].X < 22) wordMoved = true;
  Check(periodWithLetter, "sentence mark stays with the previous character");
  Check(wordMoved, "english word wraps at the word");
  var indexes = new List<int> { 0, 1, 2, 3, 4 };
  Check(WritingLineBreak.KeepOnLine("HelloZ", indexes, 5) == 5, "word wider than the line still breaks by character");
  Check(WritingLineBreak.CannotStartLine('。') && !WritingLineBreak.CannotStartLine('工'), "closing punctuation is recognised");
  var sheet = Sheet(30); sheet.HeightMm = 20;
  var plain = new List<WritingStroke> { new WritingStroke(new[] { new WritingPoint(1, -1), new WritingPoint(2, -1) }) };
  var once = WritingVariation.Apply(plain, 7, 0.2, sheet);
  var twice = WritingVariation.Apply(plain, 7, 0.2, sheet);
  Check(once[0].Points[0].X == twice[0].Points[0].X && once[0].Points[0].Y == twice[0].Points[0].Y, "same seed repeats");
  Check(WritingVariation.Apply(plain, 8, 0.2, sheet)[0].Points[0].X != once[0].Points[0].X, "different seed changes the path");
  Check(ReferenceEquals(WritingVariation.Apply(plain, 7, 0, sheet), plain), "zero variation keeps the path");
  Throws(() => WritingVariation.Apply(plain, 1, 0.35, new PageLayoutOptions { WidthMm = 1, HeightMm = 1, LeftMm = 0, RightMm = 0, TopMm = 0, BottomMm = 0, AutoWrap = true }), "variation outside the sheet is refused");
  var hits = GlyphCoverageService.Scan("工\n字", symbol => { if (symbol == '工') return BuiltinGlyphLibrary.Resolve(symbol); throw new InvalidOperationException("no"); }, 2);
  Check(hits.Count == 1 && hits[0].RegionNumber == 2 && hits[0].Line == 2 && hits[0].Column == 1 && hits[0].Symbol == '字', "missing glyph is located");
  Check(GlyphCoverageService.Describe(hits).Contains("区域 2") && GlyphCoverageService.Describe(hits).Contains("不会静默跳过"), "coverage report names the place");
  string directory = Path.Combine(Path.GetTempPath(), "aw-next-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(directory);
  string baseFile = Path.Combine(directory, "base.fontpack.json");
  string extraFile = Path.Combine(directory, "extra.fontpack.json");
  File.WriteAllText(baseFile, Pack("A"));
  File.WriteAllText(extraFile, Pack("B"));
  string before = File.ReadAllText(baseFile);
  var overlaid = GlyphFontPack.LoadFromFile(baseFile).OverlayWith(GlyphFontPack.LoadFromFile(extraFile));
  Check(overlaid.Contains('A') && overlaid.Contains('B') && File.ReadAllText(baseFile) == before, "supplement overlays without rewriting the base pack");
  var gridSheet = new PageLayoutOptions { WidthMm = 90, HeightMm = 60, LeftMm = 0, RightMm = 0, TopMm = 0, BottomMm = 0, AutoWrap = true };
  var cells = WritingGridService.Cells(gridSheet, 2, 3);
  var lines = WritingGridService.Lines(gridSheet, 2, 3);
  Check(cells.Count == 6 && lines.Count == (2 + 1) + (3 + 1), "table creates cells and optional lines");
  Check(!WritingCommandGuard.Blocks(false, "G1 X1", false) && WritingCommandGuard.Blocks(true, "G1 X1", false) && WritingCommandGuard.Blocks(true, "G0 X1", true), "motion is blocked only while a writing task is active");
  Check(!WritingCommandGuard.Blocks(true, "?", false) && !WritingCommandGuard.Blocks(true, "#stop", false) && !WritingCommandGuard.Blocks(true, "G90", false), "status, stop and non-motion stay available");
  Check(WritingPreviewBinding.BlocksStart(true, "G1 X1\nG1 X2", "G1 X1\nG1 X2") && !WritingPreviewBinding.BlocksStart(false, "G1 X1", "G1 X1"), "stale loaded program blocks its own start");
  Check(!WritingPreviewBinding.BlocksStart(true, "G1 X9", "G1 X1"), "a different program is not treated as the stale writing job");
  var task = new PageTaskService();
  task.Clock = () => new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
  string refusal;
  Check(task.Bind("f", new[] { "G1 X1" }, out refusal) == PageTaskBindResult.Created, "task binds");
  task.NoteLoaded(); task.NoteStarted();
  task.Clock = () => new DateTime(2026, 10, 11, 0, 0, 5, DateTimeKind.Utc);
  task.NoteLines(2, 1, 4);
  Check(task.BlocksManualCommand && task.SentLines == 2 && task.TotalLines == 4 && task.RunSeconds >= 5 && task.ProgressText.Contains("不是已写完的字数"), "send progress is separate from finished glyphs");
  task.NoteFinish(PageCompletionWitness.FromController(true, true, true, true, true));
  Check(task.ReliablePage == 1 && task.CheckpointPositionKnown && task.CheckpointText.Contains("不会自动续写"), "idle pen-up records a checkpoint without auto continue");
  var broken = new PageTaskService();
  broken.Bind("f", new[] { "G1 X1" }, out refusal); broken.NoteLoaded(); broken.NoteStarted(); broken.NoteDisconnect();
  Check(broken.ReliablePage == 0 && !broken.CheckpointPositionKnown && broken.CheckpointText.Contains("不会自动续写") && broken.LogEntries.Count > 0, "disconnect does not advance the checkpoint");
  var project = new WritingProject { Version = 1, Text = "工", FontPath = "", FontSha256 = "", PageEnabled = true, Page = gridSheet, FontSizeMm = 6, LetterSpacingMm = 1, LineSpacingMm = 0, VariationMm = 0.2, VariationSeed = 4, GridRows = 2, GridColumns = 3 };
  string path = Path.Combine(directory, "p.awproject");
  WritingProjectService.Save(path, project);
  var loaded = WritingProjectService.Load(path);
  Check(loaded.VariationSeed == 4 && loaded.VariationMm == 0.2 && loaded.GridRows == 2, "new project fields round trip");
  string json = File.ReadAllText(path).Replace("\"VariationMm\":0.2,", "").Replace("\"VariationSeed\":4,", "").Replace("\"GridRows\":2,", "").Replace("\"GridColumns\":3,", "");
  File.WriteAllText(path, json);
  Check(WritingProjectService.Load(path).VariationMm == 0, "older project without the new fields still opens");
 }
 static string Pack(string symbol) {
  return "{\"schemaVersion\":1,\"name\":\"" + symbol + "\",\"source\":\"t\",\"license\":\"t\",\"emSizeMm\":12,\"bodySizeMm\":10,\"baselineMm\":1,\"advanceCjkMm\":10,\"advanceSpaceMm\":6,\"glyphs\":[{\"c\":\"" + symbol + "\",\"advanceMm\":10,\"strokes\":[[1,1,2,2]]}]}";
 }
}
