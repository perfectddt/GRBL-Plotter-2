using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    public static class DrawingAnchors
    {
        public static string Canonical(string text)
        {
            return WritingEditorText.Normalize(text??"").Replace("\r\n","\n");
        }
        public static List<PageDrawing> Remap(string oldText,string newText,IEnumerable<PageDrawing> items)
        {
            oldText=Canonical(oldText);
            newText=Canonical(newText);
            var output=items.Select(d=>d.Clone()).ToList();
            if(oldText==newText)return output;
            int prefix=0;
            while(prefix<oldText.Length&&prefix<newText.Length&&oldText[prefix]==newText[prefix])prefix++;
            int suffix=0;
            while(suffix<oldText.Length-prefix&&suffix<newText.Length-prefix&&oldText[oldText.Length-1-suffix]==newText[newText.Length-1-suffix])suffix++;
            int end=oldText.Length-suffix;
            foreach(var d in output.Where(d=>d.Kind=="Slot").ToArray())
            {
                if(d.SourceStart>=end)d.SourceStart+=newText.Length-oldText.Length;
                else if(d.SourceStart>=prefix)output.Remove(d);
            }
            foreach(var d in output)if(d.HasAnchor)
            {
                if(d.AnchorIndex>=end)d.AnchorIndex+=newText.Length-oldText.Length;
                else if(d.AnchorIndex>=prefix&&oldText.Length!=newText.Length)throw new InvalidOperationException("绑定的正文被删除或大幅修改，请先选中相关对象点击“固定页位置”，再应用正文。");
                if(d.AnchorIndex<0||d.AnchorIndex>=newText.Length)throw new InvalidOperationException("批注绑定文字已不存在，请先解除绑定。");
            }
            return output;
        }
        public static List<PageDrawing> Resolve(IEnumerable<PageDrawing> items,IList<WritingLayoutPage> pages)
        {
            var output=items.Select(d=>d.Clone()).ToList();
            var anchors=pages.SelectMany(p=>p.Anchors).ToList();
            foreach(var d in output)if(d.HasAnchor)
            {
                var anchor=anchors.FirstOrDefault(a=>a.Index==d.AnchorIndex);
                if(anchor==null)throw new InvalidOperationException("未找到批注绑定的文字，请检查正文或解除绑定。");
                var b=d.Bounds();
                Shift(d,anchor.X+d.AnchorDx-b.Left,anchor.Y+d.AnchorDy-b.Top);
                d.Page=anchor.Page;
            }
            return output;
        }
        public static void Shift(PageDrawing d,double x,double y)
        {
            foreach(var path in d.Paths)for(int i=0;i<path.Length;i+=2)
            {
                path[i]+=x;
                path[i+1]+=y;
            }
        }
        public static void Bind(PageDrawing d,LayoutTextAnchor anchor)
        {
            if(d.Kind=="Reserve"||d.Kind=="Wrap")throw new InvalidOperationException("预留区和环绕插图固定在页面，不能绑定正文以避免循环排版。");
            var b=d.Bounds();
            d.HasAnchor=true;
            d.AnchorIndex=anchor.Index;
            d.AnchorDx=b.Left-anchor.X;
            d.AnchorDy=b.Top-anchor.Y;
        }
    }
}
