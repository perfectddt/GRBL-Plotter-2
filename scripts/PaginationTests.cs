using System;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Services;
using GrblPlotter.AutoWriter.Models;
internal static class PaginationTests
{
    static int checks;
    static void Check(bool test, string name) { if (!test) throw new Exception(name); checks++; }
    static PageLayoutOptions Paper(double width, double height) { return new PageLayoutOptions { WidthMm=width+4, HeightMm=height+6, LeftMm=2, RightMm=2, TopMm=3, BottomMm=3, AutoWrap=true }; }
    static IList<WritingLayoutPage> Pages(string text, double width, double height, double gap) { return TextLayoutService.LayoutPages(text, BuiltinGlyphLibrary.Resolve, 2, gap, 12, 1, Paper(width,height)); }
    static void Throws(Action action) { bool error=false; try { action(); } catch(InvalidOperationException) { error=true; } Check(error,"reject invalid layout"); }
    static int Main() {
        var two=Pages("工王工王",26,10,0);
        Check(two.Count==2 && two[0].LineCount==1 && two[1].PageNumber==2,"exact fit with wrap pagination");
        Check(two[0].Strokes.Count==7 && two[1].Strokes.Count==7,"all characters preserved");
        Check(two[0].Strokes[0].Points[0].Y==two[1].Strokes[0].Points[0].Y,"same origin every sheet");
        var text="工王\n王工\n工工\n王王";
        var pages=Pages(text,26,22,2);
        var unbounded=TextLayoutService.LayoutPage(text,BuiltinGlyphLibrary.Resolve,2,2,12,1,Paper(26,1000));
        int offset=0;
        foreach(var page in pages) {
            double dy=page.Strokes[0].Points[0].Y-unbounded[offset].Points[0].Y;
            foreach(var stroke in page.Strokes) {
                var original=unbounded[offset++]; Check(stroke.Points.Count==original.Points.Count,"stroke order/size");
                for(int i=0;i<stroke.Points.Count;i++) Check(Math.Abs(stroke.Points[i].X-original.Points[i].X)<1e-9 && Math.Abs(stroke.Points[i].Y-original.Points[i].Y-dy)<1e-9,"ordered geometry apart from sheet translation");
            }
        }
        Check(offset==unbounded.Count,"no omissions or duplicates");
        Check(Pages("工\r\n王",12,10,0).Count==Pages("工\r\r\n王",12,10,0).Count,"newline normalization");
        var blank=Pages("\n工\n\n王\n",12,10,0);
        Check(blank.Count==5 && blank[0].Strokes.Count==0 && blank[2].Strokes.Count==0 && blank[4].Strokes.Count==0,"explicit leading interior trailing blanks preserved");
        Throws(()=>Pages("工",12,9.99,0));
        Throws(()=>Pages("工",11.99,100,0));
        Throws(()=>Pages("  ",100,100,0));
        var nowrap=Paper(25,100); nowrap.AutoWrap=false;
        Throws(()=>TextLayoutService.LayoutPages("工王",BuiltinGlyphLibrary.Resolve,2,0,12,1,nowrap));
        Throws(()=>TextLayoutService.LayoutPage("工\n王",BuiltinGlyphLibrary.Resolve,2,0,12,1,Paper(12,10)));
        Console.WriteLine("PASS: "+checks+" pagination checks"); return 0;
    }
}
