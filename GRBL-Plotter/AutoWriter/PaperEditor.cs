using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    [DataContract] public sealed class AnnotationTextStyle
    {
        [DataMember] public string FontPath;
        [DataMember] public string FontSha256;
        [DataMember] public double SizeMm=10, LetterGapMm=1, LineGapMm=2;
        public AnnotationTextStyle Clone()
        {
            return (AnnotationTextStyle)MemberwiseClone();
        }
        public void Validate()
        {
            if(double.IsNaN(SizeMm)||double.IsInfinity(SizeMm)||SizeMm<=0||SizeMm>1000||double.IsNaN(LetterGapMm)||double.IsInfinity(LetterGapMm)||LetterGapMm<0||double.IsNaN(LineGapMm)||double.IsInfinity(LineGapMm)||LineGapMm<0)throw new ArgumentException("批注字号必须为正数，字距和行距必须为非负有限值。");
        }
    }
    [DataContract] public sealed class PageDrawing
    {
        [DataMember] public int Page;
        [DataMember] public string Kind;
        [DataMember] public bool HasAnchor;
        [DataMember] public int AnchorIndex;
        [DataMember] public double AnchorDx,AnchorDy;
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Text;
        [DataMember] public string LayerId, GroupId, SectionId, Name;
        [DataMember] public bool Hidden, Locked;
        [DataMember] public int SourceStart;
        [DataMember] public AnnotationTextStyle TextStyle;
        [DataMember] public List<PageDrawing> TextParts;
        [DataMember] public List<double[]> Paths = new List<double[]>();
        public PageDrawing Clone()
        {
            return new PageDrawing
            {
                Id=Id,Kind=Kind,TextStyle=TextStyle?.Clone(),TextParts=TextParts?.ConvertAll(x=>x.Clone()),HasAnchor=HasAnchor,AnchorIndex=AnchorIndex,AnchorDx=AnchorDx,AnchorDy=AnchorDy,Page=Page,Text=Text,Paths=Paths.ConvertAll(p=>(double[])p.Clone()),LayerId=LayerId,GroupId=GroupId,SectionId=SectionId,Name=Name,Hidden=Hidden,Locked=Locked,SourceStart=SourceStart
            }
            ;
        }
        public RectangleF Bounds()
        {
            var p=Paths.SelectMany(a=>Enumerable.Range(0,a.Length/2).Select(i=>new PointF((float)a[i*2],(float)a[i*2+1]))).ToArray();
            if(p.Length==0)return RectangleF.Empty;
            return RectangleF.FromLTRB(p.Min(a=>a.X),p.Min(a=>a.Y),p.Max(a=>a.X),p.Max(a=>a.Y));
        }
        public static void Validate(IEnumerable<PageDrawing> items,PageLayoutOptions sheet)
        {
            if(items==null)return;
            foreach(var d in items)
            {
                if(d==null||d.Page<1||d.Paths==null||d.Paths.Count==0)throw new ArgumentException("绘画对象无效。");
                d.TextStyle?.Validate();
                if(d.Kind!=null&&d.Kind!="Reserve"&&d.Kind!="Wrap"&&d.Kind!="Layer"&&d.Kind!="Group"&&d.Kind!="Section"&&d.Kind!="Slot"&&d.Kind!="Body")throw new ArgumentException("未知的纸面对象类型。");
                if(d.HasAnchor && (d.AnchorIndex<0||double.IsNaN(d.AnchorDx)||double.IsInfinity(d.AnchorDx)||double.IsNaN(d.AnchorDy)||double.IsInfinity(d.AnchorDy)||d.Kind=="Reserve"||d.Kind=="Wrap"))throw new ArgumentException("文字绑定信息无效。");
                foreach(var p in d.Paths)
                {
                    if(p==null||p.Length<4||p.Length%2!=0)throw new ArgumentException("绘画路径无效。");
                    for(int i=0;i<p.Length;i++)
                    {
                        double max=i%2==0?sheet.WidthMm:sheet.HeightMm;
                        if(double.IsNaN(p[i])||double.IsInfinity(p[i])||p[i]<0||p[i]>max)throw new ArgumentException("绘画超出纸张，请移动或缩小对象。");
                    }
                }
                if(d.Kind=="Reserve"&&d.Bounds().Height<.1)throw new ArgumentException("预留绘图区至少需要 0.1 mm 高度。");
                if(d.Kind=="Section"&&(d.Bounds().Width<1||d.Bounds().Height<1))throw new ArgumentException("页面分区的宽、高至少为 1 mm。");
            }
            PaperScene.Validate(items);
        }
        public static void Apply(IList<WritingLayoutPage> pages,IList<PageDrawing> items,PageLayoutOptions sheet)
        {
            Validate(items,sheet);
            foreach(var body in items.Where(d=>d.Kind=="Body"&&!PaperScene.Visible(d,items)))if(body.Page<=pages.Count){pages[body.Page-1].Strokes=new List<WritingStroke>();pages[body.Page-1].SourceText="";}
            foreach(var d in PaperScene.Ordered(items))
            {
                if(d.Page>pages.Count)throw new InvalidOperationException("第"+d.Page+"页有绘画，但正文页数已减少，请先在纸面编辑器中删除或调整该页对象。");
                if(PaperScene.IsGuide(d)||!PaperScene.Visible(d,items)) continue;
                var page=pages[d.Page-1];
                var output=new List<WritingStroke>(page.Strokes);
                foreach(var path in d.Paths)
                {
                    var points=new List<WritingPoint>();
                    for(int i=0;i<path.Length;i+=2)points.Add(new WritingPoint(path[i],-path[i+1]));
                    output.Add(new WritingStroke(points));
                }
                page.Strokes=output;
                if(!string.IsNullOrEmpty(d.Text))page.SourceText+=("\n"+d.Text);
            }
        }
    }
    internal sealed partial class PaperEditor:Form
    {
        internal List<PageDrawing> Result;
        internal Func<string,string> RequestText;
        internal AnnotationTextStyle AnnotationDefaults = new AnnotationTextStyle();
        internal Dictionary<string,string> AnnotationFonts = new Dictionary<string,string>
        {
            {
                "当前字库",""
            }
        }
        ;
        internal Func<string,AnnotationTextStyle,List<double[]>> StyledTextPaths;
        AnnotationTextStyle pendingStyle;
        internal bool AllowReserve = true;
        internal Func<List<PageDrawing>,IList<WritingLayoutPage>> Reflow;
        sealed class Snapshot
        {
            internal List<PageDrawing> Drawings;
            internal string Text;
            internal string Label;
        }
        readonly List<Snapshot> undo=new List<Snapshot>(),redo=new List<Snapshot>();
        internal Func<string,List<PageDrawing>,IList<WritingLayoutPage>> ReflowText;
        internal bool BodyEditable = true;
        readonly TextBox bodyInput = new TextBox
        {
            Multiline=true,ScrollBars=ScrollBars.Vertical,WordWrap=true,Dock=DockStyle.Fill
        }
        ;
        readonly Panel bodyPane = new Panel
        {
            Dock=DockStyle.Right,Width=300,Visible=false,Padding=new Padding(6)
        }
        ;
        string bodyText="";
        internal string BodyText
        {
            get
            {
                return bodyText;
            }
            set
            {
                bodyText=WritingEditorText.Normalize(value);
                bodyInput.Text=bodyText;
            }
        }
        int nodePath=-1,nodeIndex=-1;
        readonly Panel inlinePane = new Panel
        {
            Visible=false,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White
        }
        ;
        readonly TextBox inlineInput = new TextBox
        {
            Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical
        }
        ;
        int inlineStart,inlineLength;
        string inlineBase;
        Font inlineFont;
        IList<WritingLayoutPage> pages;
        readonly PageLayoutOptions sheet;
        readonly Func<string,List<double[]>> textPaths;
        readonly Surface canvas;
        readonly ComboBox tools=new ComboBox
        {
            DropDownStyle=ComboBoxStyle.DropDownList,Width=95
        }
        ;
        readonly NumericUpDown page=new NumericUpDown
        {
            Minimum=1,Width=60
        }
        ;
        int selected=-1;
        PointF start,last;
        PageDrawing original;
        bool dragging,resizing;
        List<double> free;
        readonly HashSet<int> selection = new HashSet<int>();
        Dictionary<int,PageDrawing> groupOriginal;
        List<PointF> lasso;
        bool HitSelection(PointF point)
        {
            var bounds=GroupBounds(selection.Select(i=>Result[i]));
            bounds.Inflate(2,2);
            return bounds.Contains(point);
        }
        RectangleF SelectionBounds()
        {
            return GroupBounds(selection.Count>0?selection.Select(i=>Result[i]):new[]
            {
                Result[selected]
            }
            );
        }
        static RectangleF GroupBounds(IEnumerable<PageDrawing> items)
        {
            var boxes=items.Select(d=>d.Bounds()).ToArray();
            return RectangleF.FromLTRB(boxes.Min(b=>b.Left),boxes.Min(b=>b.Top),boxes.Max(b=>b.Right),boxes.Max(b=>b.Bottom));
        }
        void EraseAt(PointF at)
        {
            for(int i=Result.Count-1;i>=0;i--)
            {
                var d=Result[i];
                if(d.Page!=(int)page.Value||PaperScene.IsGuide(d)||d.Text!=null||!CanTouch(d))continue;
                d.Paths=DrawingGeometry.Erase(d.Paths,at,2);
                if(d.Paths.Count==0)Result.RemoveAt(i);
            }
        }
        float zoom = 1;
        PointF pan;
        bool panning;
        Point panPixel;
        internal PaperEditor(IList<WritingLayoutPage> pages,PageLayoutOptions sheet,List<PageDrawing> items,Func<string,List<double[]>> makeText)
        {
            this.pages=pages;
            this.sheet=sheet;
            textPaths=makeText;
            Result=Copy(items);
            Text="纸面编辑 · 正文与绘画";
            Width=1050;
            Height=780;
            MinimumSize=new Size(800,560);
            Font=Ui.UiTheme.SafeFont(9f);
            StartPosition=FormStartPosition.CenterParent;
            var bar=new FlowLayoutPanel
            {
                Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6)
            }
            ;
            tools.Items.AddRange(new object[]
            {
                "选择","移动","正文改字","画笔","直线","矩形","圆","箭头","文字批注","编辑节点","套索","橡皮擦","预留绘图区"
            }
            );
            tools.SelectedIndex=0;
            var toolBar=new FlowLayoutPanel
            {
                Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6)
            }
            ;
            foreach(string name in tools.Items)
            {
                string tool=name;
                var button=Ui.UiTheme.MakeButton(name,()=>
                {
                    tools.SelectedItem=tool;
                }
                ,Ui.UiButtonKind.Normal);
                button.AutoSize=false;
                button.Width=86;
                button.Margin=new Padding(0,0,4,0);
                toolBar.Controls.Add(button);
            }
            tools.SelectedIndexChanged+=(s,e)=>
            {
                foreach(Button b in toolBar.Controls.OfType<Button>())Ui.UiTheme.StyleFlatButton(b,b.Text==tools.Text?Ui.UiButtonKind.Primary:Ui.UiButtonKind.Normal);
            }
            ;
            tools.SelectedIndex=1;
            tools.SelectedIndex=0;
            toolBar.Controls.Add(new Label
            {
                Text="页码",AutoSize=false,Width=40,Height=Ui.UiTheme.ControlHeight,TextAlign=ContentAlignment.MiddleLeft
            }
            );
            page.Maximum=Math.Max(pages.Count,Result.Count==0?1:Result.Max(d=>d.Page));
            toolBar.Controls.Add(page);
            page.ValueChanged+=(s,e)=>
            {
                selected=-1;
                selection.Clear();
                canvas.Invalidate();
            }
            ;
            Add(bar,"删除",()=>
            {
                if(tools.Text!="编辑节点"){DeleteScene();return;}if(selected<0)return;if(tools.Text=="编辑节点")
                {
                    DeleteNode();return;
                }
                Remember();if(selection.Count==0)selection.Add(selected);foreach(int i in selection.OrderByDescending(i=>i))Result.RemoveAt(i);selection.Clear();selected=-1;canvas.Invalidate();
            }
            );
            Add(bar,"撤销",()=>History(undo,redo));
            Add(bar,"重做",()=>History(redo,undo));
            Add(bar,"修改批注",EditText);
            Add(bar,"编辑正文",()=>
            {
                if(!BodyEditable)
                {
                    MessageBox.Show(this,"多区域正文请在区域编辑器修改。");return;
                }
                bodyPane.Visible=!bodyPane.Visible;if(bodyPane.Visible)bodyInput.Focus();
            }
            );
            Add(bar,"跟随文字",BindSelected);
            Add(bar,"固定页位置",()=>
            {
                if(selected<0)return;Remember();foreach(int i in selection)Result[i].HasAnchor=false;canvas.Invalidate();
            }
            );
            Add(bar,"文字环绕",()=>
            {
                if(selected<0||!BodyEditable)return;Remember();foreach(int i in selection)
                {
                    var d=Result[i];if(d.Kind=="Reserve")continue;d.HasAnchor=false;d.Kind=d.Kind=="Wrap"?null:"Wrap";
                }
                RefreshBody();
            }
            );
            Add(bar,"转为笔画",()=>
            {
                SplitTextObjects();
            }
            );
            Add(bar,"合并文字",MergeTextObjects);
            Add(bar,"适应纸面",()=>
            {
                zoom=1;pan=PointF.Empty;canvas.Invalidate();
            }
            );
            Add(bar,"更新正文排版",RefreshBody);
            Add(bar,"应用并关闭",()=>
            {
                try
                {
                    if(!CommitBody())return;PageDrawing.Validate(Result,sheet);DialogResult=DialogResult.OK;Close();
                }
                catch(Exception e)
                {
                    MessageBox.Show(this,e.Message);
                }
            }
            );
            Add(bar,"取消",()=>
            {
                DialogResult=DialogResult.Cancel;Close();
            }
            );
            var note=new Label
            {
                Dock=DockStyle.Bottom,Height=62,Text="滚轮缩放、右键平移；选择工具可拖动，右下角缩放。正文可在本窗口右侧修改。\n套索后自动进入移动，可拖组合框；编辑正文在右侧输入后应用。橡皮擦直径4mm。\n双击正文定位编辑；对象可固定页或跟随正文。应用后需重新生成程序。"
            }
            ;
            canvas=new Surface
            {
                Dock=DockStyle.Fill,BackColor=Color.FromArgb(230,232,235)
            }
            ;
            canvas.Paint+=PaintPaper;
            tools.SelectedIndexChanged += (s,e) =>
            {
                nodePath=nodeIndex=-1;
                canvas.Invalidate();
            }
            ;
            canvas.MouseDoubleClick += (s,e) => SceneDoubleClick(e);
            var inlineBar=new Ui.UiRow(Ui.UiTheme.RowHeight)
            {
                Dock=DockStyle.Bottom
            }
            ;
            inlineBar.Place(Ui.UiTheme.MakeButton("应用改字",()=>CommitInline(),Ui.UiButtonKind.Primary),0);
            inlineBar.Place(Ui.UiTheme.MakeButton("取消",()=>inlinePane.Visible=false),0);
            inlinePane.Controls.Add(inlineInput);
            inlinePane.Controls.Add(inlineBar);
            canvas.Controls.Add(inlinePane);
            inlineInput.KeyDown += (s,e) =>
            {
                if(e.KeyCode==Keys.Escape)
                {
                    inlinePane.Visible=false;
                    e.SuppressKeyPress=true;
                }
                else if(e.KeyCode==Keys.Enter&&!e.Shift)
                {
                    CommitInline();
                    e.SuppressKeyPress=true;
                }
            }
            ;
            canvas.MouseWheel += (s,e) =>
            {
                zoom=Math.Max(.5f,Math.Min(8f,zoom*(e.Delta>0?1.2f:1/1.2f)));
                canvas.Invalidate();
            }
            ;
            canvas.MouseDown+=Down;
            canvas.MouseMove+=Move;
            canvas.MouseUp+=Up;
            var bodyTitle=new Label
            {
                Text="正文（应用后重排，关闭整个编辑器前不写回项目）",Dock=DockStyle.Top,Height=40
            }
            ;
            var applyBody=Ui.UiTheme.MakeButton("应用正文并重排",()=>CommitBody(),Ui.UiButtonKind.Primary);
            applyBody.Dock=DockStyle.Bottom;
            bodyPane.Controls.Add(bodyInput);
            bodyPane.Controls.Add(applyBody);
            bodyPane.Controls.Add(bodyTitle);
            var documentHost=new Panel
            {
                Dock=DockStyle.Fill
            }
            ;
            documentHost.Controls.Add(canvas);
            documentHost.Controls.Add(bodyPane);
            Controls.Add(documentHost);
            Controls.Add(note);
            Controls.Add(bar);
            Controls.Add(toolBar);
            InitializeScene(bar,toolBar,documentHost,note);
            FormClosed += (s,e) =>
            {
                inlineFont?.Dispose();
                tools.Dispose();
            }
            ;
        }
        static List<PageDrawing> Copy(List<PageDrawing> a)
        {
            return a.ConvertAll(x=>x.Clone());
        }
        void Add(Control c,string name,Action a)
        {
            var b=Ui.UiTheme.MakeButton(name,()=>{
                if(new[]{"修改批注","跟随文字","固定页位置","文字环绕","转为笔画"}.Contains(name)&&selection.Any(i=>!CanTouch(Result[i])))return;
                a();
            },name=="应用并关闭"?Ui.UiButtonKind.Primary:Ui.UiButtonKind.Normal);
            b.AutoSize=false;
            b.Name=name;
            b.Width=100;
            b.Margin=new Padding(0,0,4,0);
            c.Controls.Add(b);
        }
        void BeginInline(Point location)
        {
            if(!BodyUnlocked)return;
            if(dragging||panning)Up(canvas,new MouseEventArgs(MouseButtons.Left,1,location.X,location.Y,0));
            if(!BodyEditable)
            {
                MessageBox.Show(this,"多区域正文请在区域编辑器修改。");
                return;
            }
            if(!CommitBody())return;
            var point=Mm(location);
            var anchor=pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==(int)page.Value).OrderBy(a=>Math.Pow(a.X-point.X,2)+Math.Pow(a.Y-point.Y,2)).FirstOrDefault();
            string canonical=DrawingAnchors.Canonical(BodyText);
            var row=anchor==null?new List<LayoutTextAnchor>():pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==anchor.Page&&(bodySelection.Count>0?bodySelection.Contains(a.Index):Math.Abs(a.Y-anchor.Y)<1e-6)).OrderBy(a=>a.Index).ToList();
            if(bodySelection.Count>0&&row.Count>1&&row[row.Count-1].Index-row[0].Index+1!=row.Count)row=row.Where(a=>a.Index==anchor.Index).ToList();
            inlineStart=row.Count==0?canonical.Length:row.Min(a=>a.Index);
            inlineLength=row.Count==0?0:row.Max(a=>a.Index)+1-inlineStart;
            inlineBase=BodyText;
            inlineInput.Text=canonical.Substring(inlineStart,inlineLength);
            var oldFont=inlineFont;
            double em=anchor==null?10:Math.Max(anchor.Height,anchor.Width*.75);
            inlineFont=Ui.UiTheme.SafeFont((float)Math.Max(10,Math.Min(28,em*Paper().Height/sheet.HeightMm*.65)));
            inlineInput.Font=inlineFont;
            oldFont?.Dispose();
            int cursor=anchor==null?inlineInput.TextLength:anchor.Index-inlineStart;
            bodyPane.Visible=false;
            var pixel=row.Count==0?location:Point.Round(Pixel(row[0].X,row[0].Y));
            int width=Math.Min(450,Math.Max(180,canvas.Width-pixel.X-6));
            inlinePane.SetBounds(Math.Max(0,Math.Min(pixel.X,canvas.Width-width)),Math.Max(0,Math.Min(pixel.Y,canvas.Height-100)),width,100);
            inlinePane.Visible=true;
            inlinePane.BringToFront();
            inlineInput.Focus();
            inlineInput.Select(Math.Min(cursor,inlineInput.TextLength),0);
        }
        bool CommitInline()
        {
            if(!inlinePane.Visible)return true;
            if(BodyText!=inlineBase)
            {
                MessageBox.Show(this,"正文已改变，请取消后重新定位。");
                return false;
            }
            string previous=bodyInput.Text;
            string candidate=DrawingAnchors.Canonical(inlineBase).Remove(inlineStart,inlineLength).Insert(inlineStart,DrawingAnchors.Canonical(inlineInput.Text));
            inlinePane.Visible=false;
            bodyInput.Text=WritingEditorText.Normalize(candidate);
            if(CommitBody())return true;
            bodyInput.Text=previous;
            inlinePane.Visible=true;
            inlineInput.Focus();
            return false;
        }
        void OpenBodyAt(Point location)
        {
            if(!BodyEditable||!CommitBody())return;
            var point=Mm(location);
            bodyPane.Visible=true;
            bodyInput.Focus();
            var anchor=pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==(int)page.Value).OrderBy(a=>Math.Pow(a.X-point.X,2)+Math.Pow(a.Y-point.Y,2)).FirstOrDefault();
            if(anchor!=null)
            {
                string canonical=DrawingAnchors.Canonical(BodyText);
                int index=canonical.Substring(0,Math.Min(canonical.Length,anchor.Index)).Replace("\n","\r\n").Length;
                bodyInput.Select(Math.Min(index,bodyInput.TextLength),1);
                bodyInput.ScrollToCaret();
            }
        }
        bool CommitBody()
        {
            if(inlinePane.Visible)return CommitInline();
            string candidate=WritingEditorText.Normalize(bodyInput.Text);
            if(candidate==BodyText)return true;
            try
            {
                if(!BodyEditable||ReflowText==null)throw new InvalidOperationException("当前入口不支持修改正文。");
                if(Result.Any(d=>d.Kind=="Body"&&(d.Locked||PaperScene.Parents(d,Result).Any(x=>x.Locked))))throw new InvalidOperationException("正文已锁定，请先在对象面板解锁。");
                var remapped=DrawingAnchors.Remap(BodyText,candidate,Result);
                var next=ReflowText(candidate,remapped);
                remapped=DrawingAnchors.Resolve(remapped,next);
                PageDrawing.Validate(remapped,sheet);
                Remember("修改正文");
                Result=remapped;
                pages=next;
                BodyText=candidate;
                selected=-1;
                selection.Clear();
                bodySelection.Clear();
                page.Maximum=Math.Max(pages.Count,Result.Count==0?1:Result.Max(d=>d.Page));
                canvas.Invalidate();
                return true;
            }
            catch(Exception e)
            {
                MessageBox.Show(this,e.Message,"正文未应用");
                return false;
            }
        }
        void BindSelected()
        {
            if(selected<0||!BodyEditable)return;
            try
            {
                var next=Copy(Result);
                foreach(int i in selection)
                {
                    var d=next[i];
                    var b=d.Bounds();
                    var anchor=pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==d.Page).OrderBy(a=>Math.Pow(a.X-b.Left,2)+Math.Pow(a.Y-b.Top,2)).FirstOrDefault();
                    if(anchor==null)throw new InvalidOperationException("当前页没有可绑定的正文。请把对象放在目标文字附近再绑定。");
                    DrawingAnchors.Bind(d,anchor);
                }
                Remember();
                Result=next;
                canvas.Invalidate();
            }
            catch(Exception e)
            {
                MessageBox.Show(this,e.Message,"绑定正文");
            }
        }
        void PickNode(Point at)
        {
            if(selected<0)
            {
                for(int i=Result.Count-1;i>=0;i--)
                {
                    var b=Result[i].Bounds();
                    b.Inflate(2,2);
                    if(Result[i].Page==(int)page.Value&&!PaperScene.IsGuide(Result[i])&&CanTouch(Result[i])&&b.Contains(start))
                    {
                        selected=i;
                        break;
                    }
                }
            }
            if(selected<0)return;
            if(!CanTouch(Result[selected])||PaperScene.IsGuide(Result[selected]))return;
            selection.Clear();
            selection.Add(selected);
            var d=Result[selected];
            if(d.Text!=null||d.Kind=="Reserve")
            {
                MessageBox.Show(this,"文字批注请先点击“转为笔画”；预留区请使用选择或移动工具。");
                return;
            }
            nodePath=-1;
            nodeIndex=-1;
            double closest=100;
            for(int p=0;p<d.Paths.Count;p++)for(int i=0;i<d.Paths[p].Length;i+=2)
            {
                var pixel=Pixel(d.Paths[p][i],d.Paths[p][i+1]);
                double distance=Math.Pow(pixel.X-at.X,2)+Math.Pow(pixel.Y-at.Y,2);
                if(distance<closest)
                {
                    closest=distance;
                    nodePath=p;
                    nodeIndex=i;
                }
            }
            if(nodePath>=0)
            {
                Remember();
                original=d.Clone();
                dragging=true;
                canvas.Capture=true;
            }
            canvas.Invalidate();
        }
        void DeleteNode()
        {
            if(selected<0||nodePath<0||nodeIndex<0||nodePath>=Result[selected].Paths.Count)return;
            Remember();
            var paths=Result[selected].Paths;
            if(paths[nodePath].Length<=4)paths.RemoveAt(nodePath);
            else
            {
                var points=paths[nodePath].ToList();
                points.RemoveRange(nodeIndex,2);
                paths[nodePath]=points.ToArray();
            }
            if(paths.Count==0)
            {
                Result.RemoveAt(selected);
                selected=-1;
                selection.Clear();
            }
            nodePath=nodeIndex=-1;
            canvas.Invalidate();
        }
        void Remember(string label=null)
        {
            undo.Add(new Snapshot
            {
                Drawings=Copy(Result),Text=BodyText,Label=label??("编辑 · "+tools.Text)
            }
            );
            if(undo.Count>50)undo.RemoveAt(0);
            redo.Clear();
        }
        void RefreshBody()
        {
            if(Reflow==null&&ReflowText==null)return;
            try
            {
                pages=ReflowText!=null?ReflowText(BodyText,Copy(Result)):Reflow(Copy(Result));
                Result=DrawingAnchors.Resolve(Result,pages);
                page.Maximum=Math.Max(pages.Count,Result.Count==0?1:Result.Max(d=>d.Page));
                canvas.Invalidate();
            }
            catch(Exception e)
            {
                MessageBox.Show(this,e.Message,"正文排版");
            }
        }
        void History(List<Snapshot> from,List<Snapshot> to)
        {
            inlinePane.Visible=false;
            if(from.Count==0)return;
            to.Add(new Snapshot
            {
                Drawings=Copy(Result),Text=BodyText,Label=from[from.Count-1].Label
            }
            );
            var snapshot=from[from.Count-1];
            Result=snapshot.Drawings;
            BodyText=snapshot.Text;
            from.RemoveAt(from.Count-1);
            RefreshBody();
            selected=-1;
            selection.Clear();
            bodySelection.Clear();
            canvas.Invalidate();
        }
        RectangleF Paper()
        {
            float scale=(float)Math.Min(Math.Max(1,canvas.Width-40)/sheet.WidthMm,Math.Max(1,canvas.Height-40)/sheet.HeightMm)*zoom;
            return new RectangleF((canvas.Width-(float)sheet.WidthMm*scale)/2+pan.X,(canvas.Height-(float)sheet.HeightMm*scale)/2+pan.Y,(float)sheet.WidthMm*scale,(float)sheet.HeightMm*scale);
        }
        PointF Pixel(double x,double y)
        {
            var r=Paper();
            return new PointF(r.Left+(float)(x/sheet.WidthMm)*r.Width,r.Top+(float)(y/sheet.HeightMm)*r.Height);
        }
        PointF Mm(Point p)
        {
            var r=Paper();
            return new PointF((float)Math.Max(0,Math.Min(sheet.WidthMm,(p.X-r.Left)*sheet.WidthMm/r.Width)),(float)Math.Max(0,Math.Min(sheet.HeightMm,(p.Y-r.Top)*sheet.HeightMm/r.Height)));
        }
        void PaintPaper(object sender,PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r=Paper();
            e.Graphics.FillRectangle(Brushes.White,r);
            e.Graphics.DrawRectangle(Pens.Gray,r.X,r.Y,r.Width,r.Height);
            int n=(int)page.Value;
            if(BodyVisible&&n<=pages.Count)foreach(var stroke in pages[n-1].Strokes)for(int i=1;i<stroke.Points.Count;i++)e.Graphics.DrawLine(Pens.Gray,Pixel(stroke.Points[i-1].X,-stroke.Points[i-1].Y),Pixel(stroke.Points[i].X,-stroke.Points[i].Y));
            foreach(var d in PaperScene.Ordered(Result).Where(d=>d.Page==n&&!PaperScene.IsContainer(d)&&d.Kind!="Slot"&&PaperScene.Visible(d,Result)))
            {
                if(d.HasAnchor)
                {
                    var b=d.Bounds();
                    var p=Pixel(b.Left,b.Top);
                    e.Graphics.DrawString("跟随正文",Font,Brushes.RoyalBlue,p.X,p.Y-Font.Height);
                }
                if(d.Kind=="Reserve")
                {
                    var b=d.Bounds();
                    var a=Pixel(b.Left,b.Top);
                    var z=Pixel(b.Right,b.Bottom);
                    using(var brush=new SolidBrush(Color.FromArgb(55,80,170,230)))e.Graphics.FillRectangle(brush,a.X,a.Y,z.X-a.X,z.Y-a.Y);
                    e.Graphics.DrawString("正文预留区（不输出边框）",Font,Brushes.SteelBlue,a);
                }
                foreach(var path in d.Paths)for(int i=2;i<path.Length;i+=2)e.Graphics.DrawLine(d.Kind=="Reserve"?Pens.SteelBlue:Pens.DarkRed,Pixel(path[i-2],path[i-1]),Pixel(path[i],path[i+1]));
            }
            if(lasso!=null&&lasso.Count>1)e.Graphics.DrawLines(Pens.DodgerBlue,lasso.Select(p=>Pixel(p.X,p.Y)).ToArray());
            foreach(int i in selection.Where(i=>i>=0&&i<Result.Count&&PaperScene.Visible(Result[i],Result)))
            {
                var b=Result[i].Bounds();
                var a=Pixel(b.Left,b.Top);
                var z=Pixel(b.Right,b.Bottom);
                e.Graphics.DrawRectangle(Pens.DodgerBlue,a.X-2,a.Y-2,z.X-a.X+4,z.Y-a.Y+4);
            }
            if(tools.Text=="编辑节点"&&selected>=0&&Result[selected].Text==null)foreach(var path in Result[selected].Paths)for(int i=0;i<path.Length;i+=2)
            {
                var point=Pixel(path[i],path[i+1]);
                e.Graphics.FillRectangle(Brushes.Blue,point.X-3,point.Y-3,6,6);
            }
            if(selected>=0)
            {
                var b=SelectionBounds();
                var a=Pixel(b.Left,b.Top);
                var z=Pixel(b.Right,b.Bottom);
                e.Graphics.DrawRectangle(Pens.DodgerBlue,a.X-3,a.Y-3,z.X-a.X+6,z.Y-a.Y+6);
                e.Graphics.FillRectangle(Brushes.DodgerBlue,z.X-4,z.Y-4,8,8);
            }
            PaintScene(e.Graphics);
        }
        void Down(object sender,MouseEventArgs e)
        {
            canvas.Focus();
            if(e.Button==MouseButtons.Right)
            {
                panning=true;
                panPixel=e.Location;
                canvas.Capture=true;
                return;
            }
            if(e.Button!=MouseButtons.Left||!Paper().Contains(e.Location))return;
            start=last=Mm(e.Location);
            if(!InActiveSection(start))return;
            string tool=tools.Text;
            var drawingLayer=Result.FirstOrDefault(d=>d.Id==activeLayer);
            if(tool!="选择"&&tool!="移动"&&tool!="正文改字"&&drawingLayer!=null&&!PaperScene.Editable(drawingLayer,Result))return;
            if(SelectBodyDown(e))return;
            if(tool=="正文改字")return;
            if(tool=="预留绘图区"&&!AllowReserve)
            {
                MessageBox.Show(this,"自动预留区适用于普通正文，多区域请在区域排版调整文字框。");
                return;
            }
            if(tool=="编辑节点")
            {
                PickNode(e.Location);
                return;
            }
            if(tool=="橡皮擦")
            {
                Remember();
                selection.Clear();
                selected=-1;
                dragging=true;
                EraseAt(start);
                canvas.Capture=true;
                return;
            }
            if(tool=="套索")
            {
                selection.Clear();
                selected=-1;
                lasso=new List<PointF>
                {
                    start
                }
                ;
                dragging=true;
                canvas.Capture=true;
                return;
            }
            selected=-1;
            if((tool=="选择"||tool=="移动"))
            {
                if((ModifierKeys&Keys.Control)==0&&selection.Count>0&&HitSelection(start)) selected=selection.First();
                foreach(var candidate in PaperScene.Ordered(Result).Reverse())
                {
                    if(selected>=0)break;
                    int i=Result.IndexOf(candidate);
                    if(Result[i].Page!=(int)page.Value||Result[i].Kind=="Slot"||Result[i].Kind=="Body"||!CanTouch(Result[i]))continue;
                    if(PaperScene.IsContainer(Result[i])&&Result[i].Kind!="Section")continue;
                    var b=Result[i].Bounds();
                    b.Inflate(2,2);
                    if(b.Contains(start))
                    {
                        selected=i;
                        break;
                    }
                }
                if(selected>=0)
                {
                    bodySelection.Clear();
                    if((ModifierKeys&Keys.Control)!=0){if(!selection.Add(selected))selection.Remove(selected);selected=selection.Count>0?selection.First():-1;canvas.Invalidate();return;}
                    if(!selection.Contains(selected))
                    {
                        selection.Clear();
                        selection.Add(selected);
                    }
                    var clicked=Result[selected];
                    if(!string.IsNullOrEmpty(clicked.GroupId)){int group=Result.FindIndex(x=>x.Id==clicked.GroupId);if(group>=0){selection.Clear();selection.Add(group);selected=group;}}
                    if(selection.Any(i=>!SceneUnlocked(Result[i])))return;
                    bool containerSelection=selection.Any(i=>PaperScene.IsContainer(Result[i]));
                    foreach(int i in SelectedScene().ToArray())if(containerSelection?!PaperScene.IsLocked(Result[i],Result):CanTouch(Result[i]))selection.Add(i);
                    selection.RemoveWhere(i=>containerSelection?PaperScene.IsLocked(Result[i],Result):!CanTouch(Result[i]));if(selection.Count==0){selected=-1;return;}
                    groupOriginal=selection.ToDictionary(i=>i,i=>Result[i].Clone());
                    var b=SelectionBounds();
                    var handle=Pixel(b.Right,b.Bottom);
                    resizing=Math.Abs(e.X-handle.X)<9&&Math.Abs(e.Y-handle.Y)<9;
                    moveRemembered=false;
                    original=Result[selected].Clone();
                    dragging=true;
                }
            }
            else if(tool=="文字批注")
            {
                pendingStyle=AnnotationDefaults.Clone();
                string text=Ask("");
                if(!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        var d=new PageDrawing
                        {
                            Page=(int)page.Value,Text=text,Paths=MakeTextPaths(text),TextStyle=pendingStyle?.Clone()
                        }
                        ;
                        Translate(d,start.X,start.Y);
                        AttachNewObject(d);
                        PageDrawing.Validate(Result.Concat(new[]{d}),sheet);
                        Remember();
                        Result.Add(d);
                        selected=Result.Count-1;
                        selection.Clear();
                        selection.Add(selected);
                        selection.Clear();
                        selection.Add(selected);
                    }
                    catch(Exception err)
                    {
                        MessageBox.Show(this,err.Message);
                    }
                }
            }
            else
            {
                Remember();
                Result.Add(new PageDrawing
                {
                    Page=(int)page.Value
                }
                );
                AttachNewObject(Result[Result.Count-1]);
                selected=Result.Count-1;
                selection.Clear();
                selection.Add(selected);
                dragging=true;
                free=new List<double>
                {
                    start.X,start.Y
                }
                ;
            }
            if(selected<0 && (tool=="选择"||tool=="移动"))selection.Clear();
            canvas.Capture=dragging;
            canvas.Invalidate();
        }
        static void Translate(PageDrawing d,double x,double y)
        {
            foreach(var p in d.Paths)for(int i=0;i<p.Length;i+=2)
            {
                p[i]+=x;
                p[i+1]+=y;
            }
        }
        void Move(object sender,MouseEventArgs e)
        {
            if(panning)
            {
                pan.X+=e.X-panPixel.X;
                pan.Y+=e.Y-panPixel.Y;
                panPixel=e.Location;
                canvas.Invalidate();
                return;
            }
            if(bodyDrag)
            {
                var point=Mm(e.Location);if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<1.5)return;
                bodyDrag=false;DetachBody(false);if(selected<0)return;bodyDetachedDrag=true;groupOriginal=selection.ToDictionary(i=>i,i=>Result[i].Clone());original=Result[selected].Clone();moveRemembered=true;resizing=false;dragging=true;
            }
            if(!dragging)return;
            var now=Mm(e.Location);
            if(!InActiveSection(now))return;
            if(tools.Text=="橡皮擦")
            {
                double length=Math.Sqrt((now.X-last.X)*(now.X-last.X)+(now.Y-last.Y)*(now.Y-last.Y));
                int steps=Math.Max(1,(int)Math.Ceiling(length));
                for(int i=1;i<=steps;i++)EraseAt(new PointF(last.X+(now.X-last.X)*i/steps,last.Y+(now.Y-last.Y)*i/steps));
                last=now;
                canvas.Invalidate();
                return;
            }
            if(tools.Text=="套索")
            {
                last=now;
                lasso.Add(now);
                canvas.Invalidate();
                return;
            }
            if(selected<0)return;
            last=now;
            var d=Result[selected];
            string tool=tools.Text;
            if(tool=="编辑节点")
            {
                if(nodePath>=0)
                {
                    d.Paths[nodePath][nodeIndex]=last.X;
                    d.Paths[nodePath][nodeIndex+1]=last.Y;
                }
                canvas.Invalidate();
                return;
            }
            if((tool=="选择"||tool=="移动"))
            {
                if(!moveRemembered){if(Math.Abs(last.X-start.X)+Math.Abs(last.Y-start.Y)<.2)return;Remember("移动 / 缩放对象");moveRemembered=true;}
                var bounds=GroupBounds(groupOriginal.Values);
                foreach(var entry in groupOriginal)
                {
                    var target=Result[entry.Key];
                    target.Paths=entry.Value.Paths.ConvertAll(p=>(double[])p.Clone());
                    if(resizing)
                    {
                        double sx=Math.Max(.01,last.X-bounds.Left)/Math.Max(.01,bounds.Width),sy=Math.Max(.01,last.Y-bounds.Top)/Math.Max(.01,bounds.Height);
                        foreach(var path in target.Paths)for(int i=0;i<path.Length;i+=2)
                        {
                            path[i]=bounds.Left+(path[i]-bounds.Left)*sx;
                            path[i+1]=bounds.Top+(path[i+1]-bounds.Top)*sy;
                        }
                    }
                    else Translate(target,last.X-start.X,last.Y-start.Y);
                    if(target.Kind=="Reserve")
                    {
                        var b=target.Bounds();
                        target.Paths=new List<double[]>
                        {
                            new[]
                            {
                                0.0,(double)b.Top,sheet.WidthMm,(double)b.Top,sheet.WidthMm,(double)b.Bottom,0.0,(double)b.Bottom,0.0,(double)b.Top
                            }
                        }
                        ;
                    }
                }
            }
            else if(tool=="画笔")
            {
                if(Math.Abs(last.X-free[free.Count-2])+Math.Abs(last.Y-free[free.Count-1])>.1)
                {
                    free.Add(last.X);
                    free.Add(last.Y);
                }
                d.Paths=new List<double[]>
                {
                    free.ToArray()
                }
                ;
            }
            else
            {
                double x=start.X,y=start.Y,X=last.X,Y=last.Y;
                d.Paths=new List<double[]>();
                if(tool=="页面分区")
                {
                    d.Kind="Section";d.Name="分区 "+(Result.Count(item=>item.Kind=="Section"));
                    d.SectionId=null;d.Paths.Add(new[]{Math.Min(x,X),Math.Min(y,Y),Math.Max(x,X),Math.Min(y,Y),Math.Max(x,X),Math.Max(y,Y),Math.Min(x,X),Math.Max(y,Y),Math.Min(x,X),Math.Min(y,Y)});
                }
                else if(tool=="预留绘图区")
                {
                    d.Kind="Reserve";
                    double top=Math.Min(y,Y),bottom=Math.Max(y,Y);
                    d.Paths.Add(new[]
                    {
                        0.0,top,sheet.WidthMm,top,sheet.WidthMm,bottom,0.0,bottom,0.0,top
                    }
                    );
                }
                else if(tool=="矩形")d.Paths.Add(new[]
                {
                    x,y,X,y,X,Y,x,Y,x,y
                }
                );
                else if(tool=="圆")
                {
                    var p=new List<double>();
                    for(int i=0;i<=64;i++)
                    {
                        double a=i*Math.PI/32;
                        p.Add((x+X)/2+Math.Abs(X-x)/2*Math.Cos(a));
                        p.Add((y+Y)/2+Math.Abs(Y-y)/2*Math.Sin(a));
                    }
                    d.Paths.Add(p.ToArray());
                }
                else
                {
                    d.Paths.Add(new[]
                    {
                        x,y,X,Y
                    }
                    );
                    if(tool=="箭头")
                    {
                        double a=Math.Atan2(Y-y,X-x),l=Math.Min(4,Math.Sqrt((X-x)*(X-x)+(Y-y)*(Y-y))/3);
                        d.Paths.Add(new[]
                        {
                            X-l*Math.Cos(a-.5),Y-l*Math.Sin(a-.5),X,Y,X-l*Math.Cos(a+.5),Y-l*Math.Sin(a+.5)
                        }
                        );
                    }
                }
            }
            canvas.Invalidate();
        }
        void Up(object sender,MouseEventArgs e)
        {
            if(bodyDrag){bodyDrag=false;canvas.Capture=false;return;}
            if(panning)
            {
                panning=false;
                canvas.Capture=false;
                return;
            }
            if(!dragging)return;
            if(tools.Text=="套索")
            {
                selection.Clear();
                if(lasso.Count>=3)for(int i=0;i<Result.Count;i++)if(Result[i].Page==(int)page.Value&&CanTouch(Result[i])&&!PaperScene.IsGuide(Result[i])&&Result[i].Paths.All(p=>Enumerable.Range(0,p.Length/2).All(j=>DrawingGeometry.Contains(lasso,new PointF((float)p[j*2],(float)p[j*2+1])))))selection.Add(i);
                selected=selection.Count==0?-1:selection.First();
                if(selected>=0)tools.SelectedItem="移动";
                lasso=null;
                dragging=false;
                canvas.Capture=false;
                canvas.Invalidate();
                return;
            }
            if(tools.Text=="橡皮擦")
            {
                dragging=false;
                canvas.Capture=false;
                canvas.Invalidate();
                return;
            }
            dragging=false;
            canvas.Capture=false;
            try
            {
                PageDrawing.Validate(Result,sheet);
                if(Result[selected].Paths.Count==0)throw new Exception();
            }
            catch
            {
                if(bodyDetachedDrag&&undo.Count>0)
                {
                    var previous=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);Result=previous.Drawings;BodyText=previous.Text;selected=-1;selection.Clear();bodySelection.Clear();bodyDetachedDrag=false;original=null;RefreshBody();return;
                }
                if(original!=null&&(tools.Text=="选择"||tools.Text=="移动"))
                {
                    foreach(var entry in groupOriginal)Result[entry.Key]=entry.Value;
                }
                else if(original!=null&&tools.Text=="编辑节点")Result[selected]=original;
                else Result.RemoveAt(selected);
                selected=-1;
                selection.Clear();
            }
            original=null;
            bodyDetachedDrag=false;
            if(selected>=0)foreach(int i in selection)if(Result[i].HasAnchor)
            {
                var anchor=pages.SelectMany(p=>p.Anchors).FirstOrDefault(a=>a.Index==Result[i].AnchorIndex);
                if(anchor!=null)DrawingAnchors.Bind(Result[i],anchor);
            }
            canvas.Invalidate();
        }
        List<double[]> MakeTextPaths(string text)
        {
            return StyledTextPaths==null?textPaths(text):StyledTextPaths(text,pendingStyle);
        }
        string Ask(string initial)
        {
            if(RequestText!=null)return RequestText(initial);
            using(var dialog=new Form
            {
                Text="文字批注 · 独立文字属性",Width=620,Height=390,MinimumSize=new Size(520,320),Font=Ui.UiTheme.SafeFont(9f),StartPosition=FormStartPosition.CenterParent
            }
            )
            {
                var input=new TextBox
                {
                    Name="annotationText",Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=initial
                }
                ;
                var header=new TableLayoutPanel
                {
                    Dock=DockStyle.Top,ColumnCount=4,RowCount=3,Height=110,Padding=new Padding(6)
                }
                ;
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
                var font=new ComboBox
                {
                    Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList
                }
                ;
                var paths=new List<string>();
                foreach(var item in AnnotationFonts)
                {
                    font.Items.Add(item.Key);
                    paths.Add(item.Value);
                }
                int chosen=paths.FindIndex(p=>string.Equals(p,pendingStyle.FontPath??"",StringComparison.OrdinalIgnoreCase));
                if(chosen<0)
                {
                    font.Items.Add("原字库："+System.IO.Path.GetFileName(pendingStyle.FontPath));
                    paths.Add(pendingStyle.FontPath??"");
                    chosen=paths.Count-1;
                }
                font.SelectedIndex=chosen;
                Func<double,double,NumericUpDown> number=(value,min)=>new NumericUpDown
                {
                    Dock=DockStyle.Fill,DecimalPlaces=2,Minimum=(decimal)min,Maximum=1000,Value=(decimal)Math.Max(min,Math.Min(1000,value))
                }
                ;
                var size=number(pendingStyle.SizeMm,.1);
                var letter=number(pendingStyle.LetterGapMm,0);
                var line=number(pendingStyle.LineGapMm,0);
                size.Name="annotationSizeMm";
                letter.Name="annotationLetterGapMm";
                line.Name="annotationLineGapMm";
                font.Name="annotationFont";
                header.Controls.Add(new Label
                {
                    Text="批注字库",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft
                }
                ,0,0);
                header.Controls.Add(font,1,0);
                header.SetColumnSpan(font,3);
                header.Controls.Add(new Label
                {
                    Text="字号 mm",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft
                }
                ,0,1);
                header.Controls.Add(size,1,1);
                header.Controls.Add(new Label
                {
                    Text="字距 mm",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft
                }
                ,2,1);
                header.Controls.Add(letter,3,1);
                header.Controls.Add(new Label
                {
                    Text="行距 mm",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft
                }
                ,0,2);
                header.Controls.Add(line,1,2);
                var buttons=new Ui.UiRow(Ui.UiTheme.RowHeight)
                {
                    Dock=DockStyle.Bottom
                }
                ;
                buttons.Place(Ui.UiTheme.MakeButton("确定",()=>
                {
                    pendingStyle=new AnnotationTextStyle
                    {
                        FontPath=paths[font.SelectedIndex],SizeMm=(double)size.Value,LetterGapMm=(double)letter.Value,LineGapMm=(double)line.Value
                    }
                    ;dialog.DialogResult=DialogResult.OK;
                }
                ,Ui.UiButtonKind.Primary),0);
                buttons.Place(Ui.UiTheme.MakeButton("取消",()=>dialog.DialogResult=DialogResult.Cancel),0);
                dialog.Controls.Add(input);
                dialog.Controls.Add(header);
                dialog.Controls.Add(buttons);
                return dialog.ShowDialog(this)==DialogResult.OK?input.Text:null;
            }
        }
        void EditText()
        {
            if(selected<0||Result[selected].Text==null||!CanTouch(Result[selected]))return;
            pendingStyle=(Result[selected].TextStyle??AnnotationDefaults).Clone();
            string text=Ask(Result[selected].Text);
            if(text==null)return;
            try
            {
                var b=Result[selected].Bounds();
                var d=Result[selected].Clone();
                d.Text=text;
                d.TextParts=null;
                d.Paths=MakeTextPaths(text);
                d.TextStyle=pendingStyle?.Clone();
                Translate(d,b.Left,b.Top);
                PageDrawing.Validate(Result.Select((item,index)=>index==selected?d:item),sheet);
                Remember();
                Result[selected]=d;
                canvas.Invalidate();
            }
            catch(Exception e)
            {
                MessageBox.Show(this,e.Message);
            }
        }
        sealed class Surface:Control
        {
            internal Surface()
            {
                SetStyle(ControlStyles.Selectable,true);
                TabStop=true;
                DoubleBuffered=true;
            }
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Invalidate();
            }
        }
    }
}
