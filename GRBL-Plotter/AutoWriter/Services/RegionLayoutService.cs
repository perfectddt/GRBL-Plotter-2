using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using GrblPlotter.AutoWriter.Models;
namespace GrblPlotter.AutoWriter.Services
{
    [DataContract]
    public sealed class WritingRegion
    {
        [DataMember] public string Text { get; set; }
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public double Width { get; set; }
        [DataMember] public double Height { get; set; }
        [DataMember] public double Left { get; set; }
        [DataMember] public double Right { get; set; }
        [DataMember] public double Top { get; set; }
        [DataMember] public double Bottom { get; set; }
        [DataMember] public double FontSize { get; set; }
        [DataMember] public double LetterGap { get; set; }
        [DataMember] public double LineGap { get; set; }
        [DataMember] public int Alignment { get; set; }
        // 0 flow, 1 every page, 2 first page, 3 last page; omitted legacy field defaults to flow.
        [DataMember(IsRequired = false)] public int PageBehavior { get; set; }
        // Stable across delete/reorder so a template can keep this region's text. Empty on older files.
        [DataMember(IsRequired = false)] public string Id { get; set; }
        // Empty means the region uses the project font. A path is paired with FontSha256 when the project is saved.
        [DataMember(IsRequired = false)] public string FontPath { get; set; }
        [DataMember(IsRequired = false)] public string FontSha256 { get; set; }
        public WritingRegion Clone() { return (WritingRegion)MemberwiseClone(); }
        public void Validate(PageLayoutOptions sheet)
        {
            foreach (double v in new[] { X,Y,Width,Height,Left,Right,Top,Bottom,FontSize,LetterGap,LineGap })
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) throw new ArgumentException("区域参数必须为非负有限值。");
            if (Text == null || FontSize <= 0) throw new ArgumentException("区域文本或字号无效。");
            if (X + Width > sheet.WidthMm + 1e-9 || Y + Height > sheet.HeightMm + 1e-9)
                throw new ArgumentException("区域超出纸张范围，请调整位置或尺寸。");
            if (PageBehavior < 0 || PageBehavior > 3) throw new ArgumentException("区域分页方式无效。");
            Options().Validate();
        }
        internal PageLayoutOptions Options() { return new PageLayoutOptions { WidthMm=Width,HeightMm=Height,LeftMm=Left,RightMm=Right,TopMm=Top,BottomMm=Bottom,AutoWrap=true,Alignment=Alignment }; }
    }
    public static class RegionLayoutService
    {
        public static IList<WritingStroke> Layout(IList<WritingRegion> regions, PageLayoutOptions sheet, Func<char,BuiltinGlyph> resolver, Func<WritingRegion, Func<char, BuiltinGlyph>> regionResolver = null)
        {
            sheet.Validate();
            if (regions == null || regions.Count == 0) throw new InvalidOperationException("请先在纸张上拖动绘制文字区域。");
            var output = new List<WritingStroke>();
            for(int i=0;i<regions.Count;i++)
            {
                var r=regions[i];
                try {
                    if(r==null)throw new ArgumentException("区域为空。");
                    r.Validate(sheet);
                    // Empty rectangles are legitimate placeholders; never substitute sample glyphs.
                    if(string.IsNullOrWhiteSpace(r.Text))continue;
                    var strokes=TextLayoutService.LayoutPage(WritingEditorText.Normalize(r.Text),Choose(r,resolver,regionResolver),r.LetterGap,r.LineGap,12,r.FontSize/BuiltinGlyphLibrary.BodySizeMm,r.Options());
                    foreach(var stroke in strokes) {
                        var points=new List<WritingPoint>();
                        foreach(var p in stroke.Points)points.Add(new WritingPoint(p.X+r.X,p.Y-r.Y));
                        output.Add(new WritingStroke(points));
                    }
                } catch(Exception error) { throw new InvalidOperationException("区域 "+(i+1)+"："+error.Message,error); }
            }
            if(output.Count==0)throw new InvalidOperationException("区域尚未填写可书写的文字。");
            return output;
        }
        public static IList<WritingLayoutPage> LayoutPages(IList<WritingRegion> regions, PageLayoutOptions sheet, Func<char,BuiltinGlyph> resolver, Func<WritingRegion, Func<char, BuiltinGlyph>> regionResolver = null)
        {
            sheet.Validate();
            if (regions == null || regions.Count == 0) throw new InvalidOperationException("请先在纸张上拖动绘制文字区域。");
            var localPages = new List<IList<WritingLayoutPage>>();
            int total = 1;
            for (int i = 0; i < regions.Count; i++) {
                try {
                    var r = regions[i];
                    if (r == null) throw new ArgumentException("区域为空。");
                    r.Validate(sheet);
                    if (string.IsNullOrWhiteSpace(r.Text)) { localPages.Add(null); continue; }
                    IList<WritingLayoutPage> pages;
                    Func<char, BuiltinGlyph> local = Choose(r, resolver, regionResolver);
                    if (r.PageBehavior == 0) {
                        pages = TextLayoutService.LayoutPages(WritingEditorText.Normalize(r.Text), local, r.LetterGap, r.LineGap, 12, r.FontSize / BuiltinGlyphLibrary.BodySizeMm, r.Options());
                        total = Math.Max(total, pages.Count);
                    } else {
                        pages = new List<WritingLayoutPage> { new WritingLayoutPage { PageNumber = 1, Strokes = TextLayoutService.LayoutPage(WritingEditorText.Normalize(r.Text), local, r.LetterGap, r.LineGap, 12, r.FontSize / BuiltinGlyphLibrary.BodySizeMm, r.Options()) } };
                    }
                    localPages.Add(pages);
                } catch (Exception error) { throw new InvalidOperationException("区域 " + (i + 1) + "：" + error.Message, error); }
            }
            var output = new List<WritingLayoutPage>();
            int strokeCount = 0;
            for (int pageIndex = 0; pageIndex < total; pageIndex++) {
                var strokes = new List<WritingStroke>();
                for (int i = 0; i < regions.Count; i++) {
                    var pages = localPages[i]; var r = regions[i];
                    if (pages == null) continue;
                    int local = r.PageBehavior == 0 ? pageIndex : 0;
                    if (local >= pages.Count || (r.PageBehavior == 2 && pageIndex != 0) || (r.PageBehavior == 3 && pageIndex != total - 1)) continue;
                    foreach (var stroke in pages[local].Strokes) {
                        var points = new List<WritingPoint>();
                        foreach (var point in stroke.Points) points.Add(new WritingPoint(point.X + r.X, point.Y - r.Y));
                        strokes.Add(new WritingStroke(points));
                    }
                }
                strokeCount += strokes.Count;
                output.Add(new WritingLayoutPage { PageNumber = pageIndex + 1, Strokes = strokes });
            }
            if (strokeCount == 0) throw new InvalidOperationException("区域尚未填写可书写的文字。");
            return output;
        }
        public static void EnsureWritable(WritingRegion region, PageLayoutOptions sheet, Func<char, BuiltinGlyph> resolver)
        {
            if (region == null) throw new ArgumentNullException("region");
            region.Validate(sheet);
            if (string.IsNullOrWhiteSpace(region.Text)) return;
            try
            {
                if (region.PageBehavior == 0)
                    TextLayoutService.LayoutPages(WritingEditorText.Normalize(region.Text), resolver, region.LetterGap, region.LineGap, 12, region.FontSize / BuiltinGlyphLibrary.BodySizeMm, region.Options());
                else
                    TextLayoutService.LayoutPage(WritingEditorText.Normalize(region.Text), resolver, region.LetterGap, region.LineGap, 12, region.FontSize / BuiltinGlyphLibrary.BodySizeMm, region.Options());
            }
            catch (InvalidOperationException error)
            {
                if (error.Message.IndexOf("没有产生任何笔画", StringComparison.Ordinal) >= 0) return;
                throw;
            }
        }
        public static string Describe(IList<WritingRegion> regions, PageLayoutOptions sheet, Func<char, BuiltinGlyph> resolver, Func<WritingRegion, Func<char, BuiltinGlyph>> regionResolver = null)
        {
            if (regions == null || regions.Count == 0) return "还没有区域。蓝框不会被书写。";
            var notes = new List<string>();
            if (Overlaps(regions)) notes.Add("有区域重叠，文字仍会各自书写。");
            for (int i = 0; i < regions.Count; i++)
            {
                WritingRegion region = regions[i];
                if (region == null || string.IsNullOrWhiteSpace(region.Text)) continue;
                try { EnsureWritable(region, sheet, Choose(region, resolver, regionResolver)); }
                catch (Exception error) { notes.Add("区域 " + (i + 1) + "：" + error.Message); }
            }
            if (notes.Count == 0) notes.Add("当前区域都能排下文字。");
            notes.Add("蓝框和编号只在预览中，红线才是本页笔迹。");
            return string.Join(" ", notes.ToArray());
        }
        private static Func<char, BuiltinGlyph> Choose(WritingRegion region, Func<char, BuiltinGlyph> shared, Func<WritingRegion, Func<char, BuiltinGlyph>> regionResolver)
        {
            if (region == null || string.IsNullOrEmpty(region.FontPath)) return shared;
            if (regionResolver == null) throw new InvalidOperationException("区域指定了独立字体，但没有载入该字库。");
            Func<char, BuiltinGlyph> local = regionResolver(region);
            if (local == null) throw new InvalidOperationException("区域字体无法读取：" + region.FontPath);
            return local;
        }
        public static bool Overlaps(IList<WritingRegion> regions)
        {
            for(int i=0;i<regions.Count;i++)for(int j=i+1;j<regions.Count;j++) {
                var a=regions[i];var b=regions[j];
                if(a.X<b.X+b.Width && b.X<a.X+a.Width && a.Y<b.Y+b.Height && b.Y<a.Y+a.Height)return true;
            }
            return false;
        }
    }
}
