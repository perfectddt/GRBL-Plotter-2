using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using GrblPlotter.AutoWriter.Models;
namespace GrblPlotter.AutoWriter.Services
{
    [DataContract] public sealed class FontDeformation
    {
        [DataMember] public bool Enabled;
        [DataMember] public int Seed=20261108;
        [DataMember] public double SizeMin=100,SizeMax=100,SpacingMin=100,SpacingMax=100,AngleMin,AngleMax,VerticalMin=100,VerticalMax=100,HorizontalMin=100,HorizontalMax=100;
        public FontDeformation Clone(){return (FontDeformation)MemberwiseClone();}
        public void Validate()
        {
            if(Seed<0)throw new ArgumentException("随机种子必须为非负整数。");
            foreach(var range in new[]{new[]{SizeMin,SizeMax},new[]{SpacingMin,SpacingMax},new[]{VerticalMin,VerticalMax},new[]{HorizontalMin,HorizontalMax}})
                if(range.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<10||v>200)||range[0]>range[1])throw new ArgumentException("大小、间距和拉伸范围须为 10～200%，且最小值不能大于最大值。");
            if(double.IsNaN(AngleMin)||double.IsInfinity(AngleMin)||double.IsNaN(AngleMax)||double.IsInfinity(AngleMax)||AngleMin<-30||AngleMax>30||AngleMin>AngleMax)throw new ArgumentException("倾斜范围须为 -30～30 度，且最小值不能大于最大值。");
        }
        public static FontDeformation Recommended(){return new FontDeformation{Enabled=true,SizeMin=95,SizeMax=100,SpacingMin=95,SpacingMax=100,AngleMin=-5,AngleMax=5,VerticalMin=95,VerticalMax=100,HorizontalMin=95,HorizontalMax=100};}
        static double Sample(Random r,double min,double max){return min+(max-min)*r.NextDouble();}
        public BuiltinGlyph Apply(BuiltinGlyph glyph,Random random)
        {
            if(!Enabled)return glyph;Validate();
            double size=Sample(random,SizeMin,SizeMax)/100,xs=size*Sample(random,HorizontalMin,HorizontalMax)/100,ys=size*Sample(random,VerticalMin,VerticalMax)/100,angle=Sample(random,AngleMin,AngleMax)*Math.PI/180,spacing=Sample(random,SpacingMin,SpacingMax)/100;
            double cx=glyph.AdvanceMm/2,cy=6,cos=Math.Cos(angle),sin=Math.Sin(angle);var strokes=new List<WritingStroke>();double left=0,right=glyph.AdvanceMm*xs;
            foreach(var stroke in glyph.Strokes)
            {
                var points=new List<WritingPoint>();foreach(var p in stroke.Points){double x=(p.X-cx)*xs,y=(p.Y-cy)*ys;double rx=cx*xs+x*cos-y*sin,ry=cy+x*sin+y*cos;left=Math.Min(left,rx);right=Math.Max(right,rx);points.Add(new WritingPoint(rx,ry));}strokes.Add(new WritingStroke(points));
            }
            if(left<0)strokes=strokes.Select(s=>new WritingStroke(s.Points.Select(p=>new WritingPoint(p.X-left,p.Y)).ToList())).ToList();
            return new BuiltinGlyph{Symbol=glyph.Symbol,AdvanceMm=right-left,Strokes=strokes,SpacingScale=glyph.SpacingScale*spacing};
        }
    }
}
