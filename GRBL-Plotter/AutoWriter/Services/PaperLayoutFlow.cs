using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using GrblPlotter.AutoWriter.Models;
namespace GrblPlotter.AutoWriter.Services
{
    [DataContract] public sealed class TextWrapArea
    {
        [DataMember] public int Page;
        [DataMember] public double Left,Top,Width,Height;
    }
    public sealed class LayoutTextAnchor
    {
        public int Index,Page;
        public double X,Y,Width,Height;
        public List<double[]> Paths;
    }
    public static partial class TextLayoutService
    {
        private static void RecordAnchors(WritingLayoutPage page,PageRow row,double x,double y,double height,double scale)
        {
            foreach(var glyph in row.Glyphs)page.Anchors.Add(new LayoutTextAnchor
            {
                Index=glyph.Index,Page=page.PageNumber,X=x+glyph.Start,Y=y,Width=glyph.Advance,Height=Math.Max(.5,height),
                Paths=AnchorPaths(glyph,row,x,y,scale)
            }
            );
        }
        private static List<double[]> AnchorPaths(PlacedGlyph glyph,PageRow row,double x,double y,double scale)
        {
            var result=new List<double[]>();if(glyph.Suppressed)return result;
            foreach(var stroke in glyph.Glyph.Strokes)
            {
                var path=new List<double>();foreach(var p in stroke.Points){path.Add(x+glyph.Start+p.X*scale);path.Add(y+row.MaxY-p.Y*scale);}result.Add(path.ToArray());
            }
            return result;
        }
        private sealed class FlowSlot
        {
            public int Page;
            public double Top,Left,Width;
        }
        private static IList<WritingLayoutPage> LayoutWrapped(string text,Func<char,BuiltinGlyph> resolve,double letter,double gap,double scale,PageLayoutOptions sheet,bool paginate)
        {
            double width=sheet.WidthMm-sheet.LeftMm-sheet.RightMm,height=sheet.HeightMm-sheet.TopMm-sheet.BottomMm;
            double pitch=sheet.LinesPerPage>0?height/sheet.LinesPerPage:BuiltinGlyphLibrary.EmSizeMm*scale+gap;
            if(pitch>height+1e-8)throw new InvalidOperationException("行高超过可写高度，请减小字号。");
            foreach(var a in sheet.WrapAreas)if(a==null||a.Page<1||double.IsNaN(a.Left)||double.IsInfinity(a.Left)||double.IsNaN(a.Top)||double.IsInfinity(a.Top)||double.IsNaN(a.Width)||double.IsInfinity(a.Width)||double.IsNaN(a.Height)||double.IsInfinity(a.Height)||a.Left<0||a.Top<0||a.Width<=0||a.Height<=0||a.Left+a.Width>sheet.WidthMm+1e-6||a.Top+a.Height>sheet.HeightMm+1e-6)throw new ArgumentException("文字环绕范围无效。");
            var slots=new List<FlowSlot>();
            int pageNo=1;
            double top=0;
            Func<int,double> getWidth=index=>
            {
                while(slots.Count<=index)
                {
                    if(top+pitch>height+1e-8)
                    {
                        pageNo++;
                        top=0;
                    }
                    if(!paginate&&pageNo>1)throw new InvalidOperationException("文字环绕后超出当前页。");
                    double absolute=sheet.TopMm+top;
                    bool blocked=false;
                    double skip=top+pitch;
                    if(sheet.ReservedAreas!=null)foreach(var area in sheet.ReservedAreas)if(area.Page==pageNo&&absolute<area.TopMm+area.HeightMm-1e-8&&absolute+pitch>area.TopMm+1e-8)
                    {
                        blocked=true;
                        skip=Math.Max(skip,area.TopMm+area.HeightMm-sheet.TopMm);
                    }
                    var spans=new List<double[]>
                    {
                        new[]
                        {
                            sheet.LeftMm,sheet.WidthMm-sheet.RightMm
                        }
                    }
                    ;
                    foreach(var area in sheet.WrapAreas)if(area.Page==pageNo&&absolute<area.Top+area.Height+1&&absolute+pitch>area.Top-1)
                    {
                        var remaining=new List<double[]>();
                        foreach(var span in spans)
                        {
                            double l=Math.Max(span[0],area.Left-1),r=Math.Min(span[1],area.Left+area.Width+1);
                            if(r<=l)
                            {
                                remaining.Add(span);
                                continue;
                            }
                            if(l>span[0])remaining.Add(new[]
                            {
                                span[0],l
                            }
                            );
                            if(r<span[1])remaining.Add(new[]
                            {
                                r,span[1]
                            }
                            );
                        }
                        spans=remaining;
                        skip=Math.Max(skip,area.Top+area.Height+1-sheet.TopMm);
                    }
                    double[] best=null;
                    foreach(var span in spans)if(best==null||span[1]-span[0]>best[1]-best[0])best=span;
                    if(blocked||best==null||best[1]-best[0]+1e-8<Math.Min(width,BuiltinGlyphLibrary.EmSizeMm*scale))
                    {
                        top=Math.Ceiling(skip/pitch-1e-9)*pitch;
                        continue;
                    }
                    slots.Add(new FlowSlot
                    {
                        Page=pageNo,Top=top,Left=best[0],Width=best[1]-best[0]
                    }
                    );
                    top+=pitch;
                }
                return slots[index].Width;
            }
            ;
            var rows=BuildRows(text,resolve,letter,scale,width,sheet.AutoWrap,getWidth,sheet.BlankGlyphs);
            var pages=new List<WritingLayoutPage>();
            for(int index=0;index<rows.Count;index++)
            {
                var row=rows[index];
                var slot=slots[index];
                while(pages.Count<slot.Page)pages.Add(new WritingLayoutPage
                {
                    PageNumber=pages.Count+1,Strokes=new List<WritingStroke>()
                }
                );
                var page=pages[slot.Page-1];
                double inkHeight=row.Strokes.Count==0?BuiltinGlyphLibrary.BodySizeMm*scale:row.MaxY-row.MinY;
                if(inkHeight>pitch+1e-8)throw new InvalidOperationException("笔迹高度超过环绕行槽，请减小字号。");
                double offset=sheet.Alignment==1?(slot.Width-row.Width)/2:sheet.Alignment==2?slot.Width-row.Width:0;
                if(page.LineCount++>0)page.SourceText+="\n";
                foreach(var glyph in row.Glyphs)page.SourceText+=text[glyph.Index];
                RecordAnchors(page,row,slot.Left+offset,sheet.TopMm+slot.Top,inkHeight,scale);
                foreach(var stroke in row.Strokes)
                {
                    var points=new List<WritingPoint>();
                    foreach(var p in stroke.Points)points.Add(new WritingPoint(slot.Left+offset+p.X,-sheet.TopMm-slot.Top+p.Y-row.MaxY));
                    page.Strokes.Add(new WritingStroke(points));
                }
            }
            int count=0;
            foreach(var page in pages)count+=page.Strokes.Count;
            if(count==0&&(sheet.BlankGlyphs==null||sheet.BlankGlyphs.Count==0))throw new InvalidOperationException("正文没有产生任何笔画。");
            return pages;
        }
    }
}
