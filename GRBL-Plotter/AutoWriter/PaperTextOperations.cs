using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
namespace GrblPlotter.AutoWriter
{
    internal sealed partial class PaperEditor
    {
        void MergeTextObjects()
        {
            if(bodySelection.Count>0)DetachBody(false);
            var items=selection.Where(i=>i>=0&&i<Result.Count).Select(i=>Result[i]).ToList();
            if(items.Count<2||items.Any(d=>string.IsNullOrEmpty(d.Text)||!CanTouch(d)||PaperScene.IsGuide(d)))return;
            if(items.Any(d=>d.LayerId!=items[0].LayerId||d.GroupId!=items[0].GroupId||d.SectionId!=items[0].SectionId)){MessageBox.Show(this,"请先将文字放到同一图层、分组和分区后合并。");return;}
            items=items.OrderBy(d=>Math.Round(d.Bounds().Top,1)).ThenBy(d=>d.Bounds().Left).ToList();
            var merged=items[0].Clone();merged.Id=Guid.NewGuid().ToString("N");merged.Name=null;merged.Text=string.Concat(items.Select(d=>d.Text));merged.TextParts=items.SelectMany(d=>d.TextParts??new List<PageDrawing>{d}).Select(d=>d.Clone()).ToList();merged.Paths=items.SelectMany(d=>d.Paths).Select(p=>(double[])p.Clone()).ToList();merged.HasAnchor=false;
            Remember("合并文字");Result.RemoveAll(d=>items.Contains(d));Result.Add(merged);AutoName(merged);selected=Result.Count-1;selection.Clear();selection.Add(selected);canvas.Invalidate();
        }
        void SplitTextObjects()
        {
            if(bodySelection.Count>0){DetachBody(false);return;}
            var items=selection.Where(i=>i>=0&&i<Result.Count).Select(i=>Result[i]).Where(d=>d.Text!=null&&CanTouch(d)&&!PaperScene.IsGuide(d)).ToList();if(items.Count==0)return;
            try
            {
                var replacements=new List<PageDrawing>();
                foreach(var d in items)
                {
                    if(d.Text.Length==1)
                    {foreach(var path in d.Paths){var stroke=d.Clone();stroke.Id=Guid.NewGuid().ToString("N");stroke.Text=null;stroke.TextParts=null;stroke.Name=null;stroke.Paths=new List<double[]>{(double[])path.Clone()};replacements.Add(stroke);}continue;}
                    var parts=d.TextParts;
                    if(parts==null)
                    {
                        parts=new List<PageDrawing>();double x=0,y=0;
                        foreach(char c in d.Text)
                        {
                            if(c=='\r')continue;if(c=='\n'){x=0;y+=(d.TextStyle??AnnotationDefaults).SizeMm+(d.TextStyle??AnnotationDefaults).LineGapMm;continue;}
                            var style=(d.TextStyle??AnnotationDefaults).Clone();if(char.IsWhiteSpace(c)){x+=style.SizeMm;continue;}
                            pendingStyle=style;var part=d.Clone();part.Id=Guid.NewGuid().ToString("N");part.Text=c.ToString();part.TextParts=null;part.Paths=MakeTextPaths(part.Text);Translate(part,x,y);parts.Add(part);x+=part.Bounds().Width+style.LetterGapMm;
                        }
                    }
                    if(parts.Count==0)continue;var old=GroupBounds(parts);var target=d.Bounds();
                    foreach(var source in parts)
                    {var part=source.Clone();part.Id=Guid.NewGuid().ToString("N");part.Name=null;part.LayerId=d.LayerId;part.GroupId=d.GroupId;part.SectionId=d.SectionId;part.HasAnchor=false;foreach(var path in part.Paths)for(int i=0;i<path.Length;i+=2){path[i]=target.Left+(path[i]-old.Left)*target.Width/Math.Max(.001,old.Width);path[i+1]=target.Top+(path[i+1]-old.Top)*target.Height/Math.Max(.001,old.Height);}replacements.Add(part);}
                }
                var next=Result.Where(d=>!items.Contains(d)).Concat(replacements).ToList();PageDrawing.Validate(next,sheet);Remember("拆分文字 / 转笔画");Result=next;selection.Clear();foreach(var d in replacements){AutoName(d);selection.Add(Result.IndexOf(d));}selected=selection.Count>0?selection.First():-1;if(replacements.All(d=>d.Text==null))tools.SelectedItem="编辑节点";canvas.Invalidate();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"未拆分");}
        }
    }
}
