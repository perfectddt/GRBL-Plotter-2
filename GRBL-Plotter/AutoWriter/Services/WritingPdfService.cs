using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GrblPlotter.AutoWriter.Services
{
    public static class WritingPdfService
    {
        private const double PointsPerMm = 72.0 / 25.4;
        private const string SourcePrefix = "AutoWriter-Text-v1:";
        public static string ImportText(string path)
        {
            var text = new StringBuilder();
            using (var document = PdfDocument.Open(path))
            {
                string source = document.Information.Subject;
                if (source != null && source.StartsWith(SourcePrefix, StringComparison.Ordinal))
                    return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(source.Substring(SourcePrefix.Length)));
                foreach (var page in document.GetPages())
                {
                    string content = ContentOrderTextExtractor.GetText(page);
                    if (string.IsNullOrWhiteSpace(content))
                        throw new InvalidDataException("PDF 第 " + page.Number + " 页没有可提取文字（旧版纯笔迹 PDF 请从原文字重新导出；扫描件需要 OCR）。原文字未修改。");
                    if (text.Length > 0) text.Append("\n\n");
                    text.Append(content);
                }
            }
            if (text.Length == 0) throw new InvalidDataException("PDF 没有可提取的文字。");
            return text.ToString();
        }
        private static string N(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        private static string Hex(string text) { var result = new StringBuilder(); foreach (char c in text) result.Append(((int)c).ToString("X4")); return result.ToString(); }
        private static string StreamObject(string content) { return "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream"; }

        // The visible page consists exclusively of pen paths. A Type0 font with an explicit
        // ToUnicode CMap carries invisible (3 Tr) UTF-16 text, independent of installed fonts.
        public static void Export(string path, IList<WritingLayoutPage> pages, PageLayoutOptions sheet, string originalText = null)
        {
            sheet.Validate();
            if (pages == null || pages.Count == 0) throw new InvalidOperationException("没有可导出的页面。");
            var allText = new StringBuilder();
            var chars = new SortedSet<char>();
            foreach (var source in pages) {
                if (allText.Length > 0) allText.Append('\n');
                allText.Append(source.SourceText);
                foreach (char c in source.SourceText ?? "") if (!char.IsControl(c)) chars.Add(c);
            }
            string preserved = originalText ?? allText.ToString();
            var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /AutoWriterUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            var mappings = new List<char>(chars);
            for (int i = 0; i < mappings.Count; i += 100) {
                int count = Math.Min(100, mappings.Count - i); cmap.Append(count).Append(" beginbfchar\n");
                for (int j = 0; j < count; j++) { string h = Hex(mappings[i + j].ToString()); cmap.Append('<').Append(h).Append("> <").Append(h).Append(">\n"); }
                cmap.Append("endbfchar\n");
            }
            cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            var kids = new StringBuilder();
            for (int i = 0; i < pages.Count; i++) kids.Append(8 + 2 * i).Append(" 0 R ");
            var objects = new List<string> {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Count " + pages.Count + " /Kids [" + kids + "] >>",
                "<< /Type /Font /Subtype /Type0 /BaseFont /STSong-Light /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 6 0 R >>",
                "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /STSong-Light /CIDSystemInfo << /Registry (Adobe) /Ordering (GB1) /Supplement 4 >> /FontDescriptor 5 0 R /DW 1000 >>",
                "<< /Type /FontDescriptor /FontName /STSong-Light /Flags 6 /FontBBox [-250 -250 1250 1250] /ItalicAngle 0 /Ascent 880 /Descent -120 /CapHeight 880 /StemV 80 >>",
                StreamObject(cmap.ToString()),
                "<< /Producer (AutoWriter)" + (string.IsNullOrWhiteSpace(preserved) ? "" : " /Subject (" + SourcePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(preserved)) + ")") + " >>"
            };
            for (int index = 0; index < pages.Count; index++) {
                var source = pages[index];
                var content = new StringBuilder(N(0.3 * PointsPerMm) + " w\n");
                foreach (var stroke in source.Strokes) {
                    if (stroke.Points.Count == 0) continue;
                    var first = stroke.Points[0];
                    content.Append(N(first.X * PointsPerMm)).Append(' ').Append(N((sheet.HeightMm + first.Y) * PointsPerMm)).Append(" m\n");
                    for (int i = 1; i < stroke.Points.Count; i++) {
                        var point = stroke.Points[i];
                        content.Append(N(point.X * PointsPerMm)).Append(' ').Append(N((sheet.HeightMm + point.Y) * PointsPerMm)).Append(" l\n");
                    }
                    content.Append("S\n");
                }
                string[] lines = (source.SourceText ?? "").Split('\n');
                double pitch = (sheet.HeightMm - sheet.TopMm - sheet.BottomMm) * PointsPerMm / Math.Max(sheet.LinesPerPage, lines.Length);
                for (int row = 0; row < lines.Length; row++) {
                    string line = lines[row].TrimEnd('\r'); if (line.Length == 0) continue;
                    double size = Math.Min(pitch * 0.8, (sheet.WidthMm - sheet.LeftMm - sheet.RightMm) * PointsPerMm / line.Length);
                    content.Append("BT /F0 ").Append(N(size)).Append(" Tf 3 Tr 1 0 0 1 ").Append(N(sheet.LeftMm * PointsPerMm)).Append(' ')
                        .Append(N((sheet.HeightMm - sheet.TopMm) * PointsPerMm - (row + 1) * pitch)).Append(" Tm <").Append(Hex(line)).Append("> Tj ET\n");
                }
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(sheet.WidthMm * PointsPerMm) + " " + N(sheet.HeightMm * PointsPerMm) + "] /Resources << /Font << /F0 3 0 R >> >> /Contents " + (9 + index * 2) + " 0 R >>");
                objects.Add(StreamObject(content.ToString()));
            }
            byte[] bytes;
            using (var stream = new MemoryStream()) {
                Action<string> write = value => { byte[] data = Encoding.ASCII.GetBytes(value); stream.Write(data, 0, data.Length); };
                write("%PDF-1.7\n"); var offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++) { offsets.Add(stream.Position); write((i + 1) + " 0 obj\n" + objects[i] + "\nendobj\n"); }
                long xref = stream.Position;
                write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long offset in offsets) write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /Info 7 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
                bytes = stream.ToArray();
            }
            path = Path.GetFullPath(path); string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(temp, bytes); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
