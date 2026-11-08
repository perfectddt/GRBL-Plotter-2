using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace GrblPlotter.AutoWriter
{
    // Scene containers use the same identity and persistence as drawable objects.
    public static class PaperScene
    {
        public static bool IsContainer(PageDrawing d) { return d.Kind=="Layer"||d.Kind=="Group"||d.Kind=="Section"||d.Kind=="Body"; }
        public static bool IsGuide(PageDrawing d) { return IsContainer(d)||d.Kind=="Slot"||d.Kind=="Reserve"; }
        public static IEnumerable<PageDrawing> Parents(PageDrawing d,IEnumerable<PageDrawing> all)
        {
            var map=all.Where(x=>!string.IsNullOrEmpty(x.Id)).GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.First());
            var seen=new HashSet<string>(); var pending=new Stack<string>();
            Action<PageDrawing> add=x=> {foreach(var id in new[]{x.LayerId,x.GroupId,x.SectionId})if(!string.IsNullOrEmpty(id))pending.Push(id);};
            add(d);
            while(pending.Count>0) { var id=pending.Pop();PageDrawing parent;if(seen.Add(id)&&map.TryGetValue(id,out parent)){yield return parent;add(parent);} }
        }
        public static bool Visible(PageDrawing d,IEnumerable<PageDrawing> all) { return !d.Hidden&&!Parents(d,all).Any(x=>x.Hidden); }
        public static bool IsLocked(PageDrawing d,IEnumerable<PageDrawing> all) { return d.Locked||Parents(d,all).Any(x=>x.Locked); }
        public static bool Editable(PageDrawing d,IEnumerable<PageDrawing> all) { return Visible(d,all)&&!IsLocked(d,all); }
        public static bool Contains(PageDrawing owner,PageDrawing d,IEnumerable<PageDrawing> all) { return d.Id==owner.Id||Parents(d,all).Any(x=>x.Id==owner.Id); }
        public static IEnumerable<PageDrawing> Ordered(IEnumerable<PageDrawing> source)
        {
            var all=source.ToList();var seen=new HashSet<string>();
            Func<PageDrawing,string> parent=d=>d.GroupId??d.SectionId??d.LayerId;
            Func<PageDrawing,IEnumerable<PageDrawing>> walk=null;
            walk=d=>{
                var output=new List<PageDrawing>();if(!seen.Add(d.Id))return output;output.Add(d);
                foreach(var child in all.Where(x=>parent(x)==d.Id))output.AddRange(walk(child));return output;
            };
            foreach(var root in all.Where(x=>parent(x)==null))foreach(var d in walk(root))yield return d;
            foreach(var root in all.Where(x=>!seen.Contains(x.Id)).ToArray())foreach(var d in walk(root))yield return d;
        }
        public static void Validate(IEnumerable<PageDrawing> source)
        {
            var all=source.ToList();var ids=new HashSet<string>();
            foreach(var d in all)if(string.IsNullOrEmpty(d.Id)||!ids.Add(d.Id))throw new ArgumentException("纸面对象编号重复或为空。");
            foreach(var d in all)
            {
                foreach(var pair in new[]{new[]{d.LayerId,"Layer"},new[]{d.GroupId,"Group"},new[]{d.SectionId,"Section"}})
                {
                    if(string.IsNullOrEmpty(pair[0]))continue;
                    var parent=all.FirstOrDefault(x=>x.Id==pair[0]);
                    if(parent==null||parent.Kind!=pair[1]||parent.Page!=d.Page||parent.Id==d.Id||Parents(parent,all).Any(x=>x.Id==d.Id))throw new ArgumentException("对象容器关系无效或存在循环。");
                }
                if(d.Kind=="Slot"&&(d.SourceStart<0||string.IsNullOrEmpty(d.Text)))throw new ArgumentException("正文占位信息无效。");
                if(!string.IsNullOrEmpty(d.SectionId)&&!IsContainer(d)&&d.Kind!="Slot")
                {
                    var section=all.First(x=>x.Id==d.SectionId).Bounds();var b=d.Bounds();section.Inflate(.01f,.01f);
                    if(!section.Contains(b))throw new ArgumentException("对象超出所属分区，请先移动回分区内。");
                }
            }
        }
        public static Dictionary<int,char> Slots(IEnumerable<PageDrawing> all,string text)
        {
            var map=new Dictionary<int,char>();if(text==null)return map;
            foreach(var d in all.Where(x=>x.Kind=="Slot"))for(int i=0;i<d.Text.Length;i++)
            {int index=d.SourceStart+i;if(index<text.Length&&text[index]==' ')map[index]=d.Text[i];}
            return map;
        }
    }
}
