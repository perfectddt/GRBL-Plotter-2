using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    internal sealed partial class PaperEditor
    {
        readonly TreeView objectTree=new TreeView{Dock=DockStyle.Fill,HideSelection=false};
        readonly ListBox historyList=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false};
        readonly ToolTip sceneTips=new ToolTip{AutoPopDelay=12000,InitialDelay=350};
        readonly HashSet<int> bodySelection=new HashSet<int>();
        readonly Timer sceneTimer=new Timer{Interval=250};
        bool syncingScene,showRulers=true,bodyDrag,bodyArmed,moveRemembered,bodyDetachedDrag;
        int armedBody=-1;
        string activeLayer,activeSection;
        string sceneStamp="";
        Label sceneStatus;
        static double[] BoxPath(RectangleF b) { return new[]{(double)b.Left,b.Top,b.Right,b.Top,b.Right,b.Bottom,b.Left,b.Bottom,b.Left,b.Top}; }
        PageDrawing BodyOwner {get{return Result.FirstOrDefault(d=>d.Kind=="Body"&&d.Page==(int)page.Value);}}
        bool BodyVisible {get{return BodyOwner==null||PaperScene.Visible(BodyOwner,Result);}}
        bool BodyUnlocked {get{return BodyEditable&&(BodyOwner==null||PaperScene.Editable(BodyOwner,Result));}}
        bool InActiveSection(PointF p)
        {
            var section=Result.FirstOrDefault(d=>d.Id==activeSection&&d.Page==(int)page.Value);
            return section==null||(PaperScene.Editable(section,Result)&&section.Bounds().Contains(p));
        }
        bool SceneUnlocked(PageDrawing d) {return !PaperScene.IsLocked(d,Result)&&(!PaperScene.IsContainer(d)||!Result.Any(x=>x.Id!=d.Id&&PaperScene.Contains(d,x,Result)&&PaperScene.IsLocked(x,Result)));}
        bool CanTouch(PageDrawing d) { return PaperScene.Visible(d,Result)&&SceneUnlocked(d)&&(string.IsNullOrEmpty(activeSection)||d.Id==activeSection||d.SectionId==activeSection||PaperScene.IsContainer(d)); }
        void InitializeScene(Control bar,Control toolBar,Control host,Label note)
        {
            Width=1280;
            note.Height=38;note.Text="单击选中文字 → 蓝框 → 再双击改字；拖动蓝框将文字独立，原文保留空位。Ctrl 多选；右键平移，滚轮缩放。\n分区双击进入，点击“整页”退出；隐藏内容不生成笔迹。应用并关闭后请重新生成程序。";
            var glyphs=new Dictionary<string,string>{{"选择","↖"},{"移动","✥"},{"正文改字","字"},{"画笔","✎"},{"直线","╱"},{"矩形","□"},{"圆","○"},{"箭头","➜"},{"文字批注","T"},{"编辑节点","◇"},{"套索","⬡"},{"橡皮擦","⌫"},{"预留绘图区","▧"}};
            foreach(Button b in toolBar.Controls.OfType<Button>())
            {
                string name=b.Text;b.Name=name;b.Text=glyphs.ContainsKey(name)?glyphs[name]:name;if(name=="选择")b.Text="➤";b.Width=38;b.Height=30;b.Font=Ui.UiTheme.SafeFont(12);sceneTips.SetToolTip(b,name);
            }
            tools.SelectedIndexChanged+=(s,e)=>{foreach(Button b in toolBar.Controls.OfType<Button>())Ui.UiTheme.StyleFlatButton(b,b.Name==tools.Text?Ui.UiButtonKind.Primary:Ui.UiButtonKind.Normal);};
            var sectionButton=Ui.UiTheme.MakeButton("页面分区",()=>tools.SelectedItem="页面分区",Ui.UiButtonKind.Normal);sectionButton.Width=90;sectionButton.AutoSize=false;sectionButton.Name="页面分区";toolBar.Controls.Add(sectionButton);sceneTips.SetToolTip(sectionButton,"页面分区：拖出矩形，双击分区进入编辑");tools.Items.Add("页面分区");
            Add(bar,"标尺 XY",()=>{showRulers=!showRulers;canvas.Invalidate();});
            Add(bar,"整页",()=>{activeSection=null;sceneStamp="";canvas.Invalidate();});
            var actionIcons=new Dictionary<string,string>{{"删除","×"},{"撤销","↶"},{"重做","↷"},{"修改批注","T✎"},{"编辑正文","¶"},{"跟随文字","⚓"},{"固定页位置","⌖"},{"文字环绕","≋"},{"转为笔画","T→"},{"适应纸面","⛶"},{"更新正文排版","⟳"},{"标尺 XY","XY"},{"整页","▤"}};
            foreach(Button b in bar.Controls.OfType<Button>())
            {string name=b.Text;b.Name=name;b.AccessibleName=name;b.Height=30;b.Width=actionIcons.ContainsKey(name)?42:100;b.Text=actionIcons.ContainsKey(name)?actionIcons[name]:name;if(name=="修改批注"||name=="转为笔画"){b.Text=name;b.Width=92;}if(actionIcons.ContainsKey(name))b.Font=Ui.UiTheme.SafeFont(12);sceneTips.SetToolTip(b,name);}
            // Two compact rows with explicit labels for document actions; drawing tools use icons.
            var tabs=new TabControl{Dock=DockStyle.Fill};var objects=new TabPage("对象 / 图层");var history=new TabPage("历史");tabs.TabPages.Add(objects);tabs.TabPages.Add(history);
            var actions=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=176,ColumnCount=3,RowCount=5};
            for(int c=0;c<3;c++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
            Action<string,Action> button=(name,action)=>{var b=Ui.UiTheme.MakeButton(name,action,Ui.UiButtonKind.Normal);b.Dock=DockStyle.Fill;b.Margin=new Padding(2);actions.Controls.Add(b);sceneTips.SetToolTip(b,name);};
            button("新建图层",()=>CreateContainer("Layer"));button("编组",GroupSelected);button("解组",UngroupSelected);
            button("显示 / 隐藏",()=>ToggleScene(false));button("锁定 / 解锁",()=>ToggleScene(true));button("重命名",RenameScene);
            button("上移一层",()=>ReorderScene(1));button("下移一层",()=>ReorderScene(-1));button("删除",DeleteScene);
            button("移入图层",()=>AssignScene("Layer"));button("移入分区",()=>AssignScene("Section"));button("退出分区",()=>{activeSection=null;sceneStamp="";canvas.Invalidate();});
            button("进入分区",EnterSection);button("选择成员",SelectMembers);button("脱离容器",DetachContainer);
            sceneStatus=new Label{Dock=DockStyle.Bottom,Height=46,Text="当前：整页 / 默认图层\n图层、分组和分区可显隐、锁定与排序。"};
            objects.Controls.Add(objectTree);objects.Controls.Add(actions);objects.Controls.Add(sceneStatus);
            var historyNote=new Label{Dock=DockStyle.Bottom,Height=42,Text="双击历史项回到该步骤；撤销后可重做。\n历史保留本次编辑的最近 50 步。"};history.Controls.Add(historyList);history.Controls.Add(historyNote);
            var side=new Panel{Dock=DockStyle.Right,Width=292,Padding=new Padding(4)};side.Controls.Add(tabs);host.Controls.Add(side);
            objectTree.AfterSelect+=(s,e)=>{if(!syncingScene)SelectTree(e.Node);};
            objectTree.NodeMouseDoubleClick+=(s,e)=>{var d=NodeDrawing(e.Node);if(d!=null&&d.Kind=="Section")EnterSection();};
            historyList.DoubleClick+=(s,e)=>JumpHistory(historyList.SelectedIndex);
            canvas.MouseUp+=(s,e)=>{sceneStamp="";RefreshScene();};
            page.ValueChanged+=(s,e)=>{activeSection=activeLayer=null;bodySelection.Clear();sceneStamp="";};
            KeyPreview=true;KeyDown+=(s,e)=>{
                if(inlinePane.Visible||bodyInput.Focused)return;
                if(e.Control&&e.KeyCode==Keys.Z){History(undo,redo);e.SuppressKeyPress=true;}
                else if(e.Control&&e.KeyCode==Keys.Y){History(redo,undo);e.SuppressKeyPress=true;}
                else if(e.KeyCode==Keys.Delete){DeleteScene();e.SuppressKeyPress=true;}
            };
            sceneTimer.Tick+=(s,e)=>RefreshScene();sceneTimer.Start();
            FormClosed+=(s,e)=>{sceneTimer.Stop();sceneTimer.Dispose();sceneTips.Dispose();};
            RefreshScene();
            InitializeObjectRows(tabs);
        }
        PageDrawing NodeDrawing(TreeNode node) {return node==null?null:Result.FirstOrDefault(d=>d.Id==(node.Tag as string));}
        IEnumerable<int> SelectedScene()
        {
            var chosen=new HashSet<int>(selection.Where(i=>i>=0&&i<Result.Count));
            foreach(int i in chosen.ToArray())if(PaperScene.IsContainer(Result[i]))for(int j=0;j<Result.Count;j++)if(PaperScene.Contains(Result[i],Result[j],Result))chosen.Add(j);
            return chosen;
        }
        void SelectTree(TreeNode node)
        {
            var d=NodeDrawing(node);if((ModifierKeys&Keys.Control)==0)selection.Clear();selected=-1;bodySelection.Clear();
            string tag=node.Tag as string;
            if(tag!=null&&tag.StartsWith("$glyph:")){int index;if(int.TryParse(tag.Substring(7),out index))bodySelection.Add(index);canvas.Invalidate();return;}
            if(d==null){canvas.Invalidate();return;}
            if(d.Kind=="Layer")activeLayer=d.Id;
            if(d.Kind!="Body"){selected=Result.IndexOf(d);selection.Add(selected);}
            canvas.Invalidate();
        }
        void RefreshScene()
        {
            foreach(var d in Result.Where(d=>string.IsNullOrWhiteSpace(d.Name)&&d.Kind!="Slot"))AutoName(d);
            foreach(var group in Result.Where(d=>d.Kind=="Group"))
            {var members=Result.Where(d=>d.GroupId==group.Id).ToArray();if(members.Length>0)group.Paths=new List<double[]>{BoxPath(GroupBounds(members))};}
            string stamp=string.Join("|",Result.Select(d=>d.Id+":"+d.Name+":"+d.Text+":"+d.Kind+":"+d.Hidden+":"+d.Locked+":"+d.LayerId+":"+d.GroupId+":"+d.SectionId))+":"+BodyText.GetHashCode()+":"+undo.Count+":"+redo.Count+":"+page.Value+":"+activeLayer+":"+activeSection;
            if(stamp==sceneStamp)return;sceneStamp=stamp;syncingScene=true;
            string selectedId=selected>=0&&selected<Result.Count?Result[selected].Id:objectTree.SelectedNode?.Tag as string;
            objectTree.BeginUpdate();objectTree.Nodes.Clear();var nodes=new Dictionary<string,TreeNode>();
            var root=objectTree.Nodes.Add("第 "+page.Value+" 页 · 正文与对象");
            var body=BodyOwner;var bodyNode=root.Nodes.Add((body!=null&&!PaperScene.Visible(body,Result)?"[隐] ":"")+(body!=null&&body.Locked?"[锁] ":"")+"正文（单击纸面选字）");bodyNode.Tag=body?.Id??"$body";
            string source=DrawingAnchors.Canonical(BodyText);
            foreach(var a in pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==(int)page.Value&&a.Index<source.Length&&!char.IsWhiteSpace(source[a.Index])))
                bodyNode.Nodes.Add(new TreeNode(source[a.Index]+" · 正文位置 "+(a.Index+1)){Tag="$glyph:"+a.Index});
            foreach(var d in Result.Where(d=>d.Page==(int)page.Value&&d.Kind!="Slot"&&d.Kind!="Body"&&d.Kind!="Group"))
            {
                var node=new TreeNode((PaperScene.Visible(d,Result)?"":"[隐] ")+(PaperScene.Editable(d,Result)?"":"[锁] ")+(d.Name??(d.Kind=="Layer"?"图层":d.Kind=="Group"?"分组":d.Kind=="Section"?"分区":d.Text!=null?"文字 · "+d.Text.Replace("\n"," "):"路径对象"))){Tag=d.Id};nodes[d.Id]=node;
            }
            foreach(var d in Result.Where(d=>nodes.ContainsKey(d.Id)))
            {
                string parent=!string.IsNullOrEmpty(d.SectionId)?d.SectionId:d.LayerId;
                TreeNode owner; if(parent!=null&&nodes.TryGetValue(parent,out owner))owner.Nodes.Add(nodes[d.Id]);else root.Nodes.Add(nodes[d.Id]);
            }
            root.ExpandAll();bodyNode.Collapse();if(selectedId!=null&&nodes.ContainsKey(selectedId))objectTree.SelectedNode=nodes[selectedId];objectTree.EndUpdate();
            historyList.Items.Clear();for(int i=0;i<undo.Count;i++)historyList.Items.Add("↶ "+(i+1)+" · "+undo[i].Label);historyList.Items.Add("● 当前状态");for(int i=redo.Count-1;i>=0;i--)historyList.Items.Add("↷ "+redo[i].Label);historyList.SelectedIndex=undo.Count;
            var section=Result.FirstOrDefault(d=>d.Id==activeSection);var layer=Result.FirstOrDefault(d=>d.Id==activeLayer);
            sceneStatus.Text="范围："+(section?.Name??"整页")+"\n绘制图层："+(layer?.Name??"默认图层")+" · 单位 mm";
            syncingScene=false;
        }
        void JumpHistory(int index)
        {
            if(index<0)return;int current=undo.Count;if(index<current)for(int i=0;i<current-index;i++)History(undo,redo);else for(int i=0;i<index-current&&redo.Count>0;i++)History(redo,undo);sceneStamp="";RefreshScene();
        }
        void CreateContainer(string kind)
        {
            Remember("新建图层");var d=new PageDrawing{Kind=kind,Page=(int)page.Value,Name="图层 "+(Result.Count(x=>x.Kind==kind)+1),Paths=new List<double[]>{BoxPath(new RectangleF(0,0,(float)sheet.WidthMm,(float)sheet.HeightMm))}};Result.Add(d);activeLayer=d.Id;selected=Result.Count-1;selection.Clear();selection.Add(selected);sceneStamp="";canvas.Invalidate();
        }
        void GroupSelected()
        {
            var members=selection.Where(i=>i>=0&&i<Result.Count&&CanTouch(Result[i])&&Result[i].Kind!="Group"&&Result[i].Kind!="Body"&&Result[i].Kind!="Slot").ToArray();if(members.Length<2)return;
            Remember("编组");var group=new PageDrawing{Kind="Group",Page=(int)page.Value,Name="分组 "+(Result.Count(d=>d.Kind=="Group")+1),Paths=new List<double[]>{BoxPath(GroupBounds(members.Select(i=>Result[i])))}};

            Result.Add(group);foreach(int i in members)Result[i].GroupId=group.Id;selected=Result.Count-1;selection.Clear();selection.Add(selected);canvas.Invalidate();
        }
        void UngroupSelected()
        {
            var group=selected>=0?Result[selected]:null;if(group==null||group.Kind!="Group"||!PaperScene.Editable(group,Result))return;
            Remember("解组");foreach(var d in Result.Where(d=>d.GroupId==group.Id))d.GroupId=null;Result.Remove(group);selection.Clear();selected=-1;canvas.Invalidate();
        }
        PageDrawing EnsureBodyOwner()
        {
            var body=BodyOwner;if(body==null){body=new PageDrawing{Kind="Body",Page=(int)page.Value,Name="正文",Paths=new List<double[]>{BoxPath(new RectangleF(0,0,(float)sheet.WidthMm,(float)sheet.HeightMm))}};Result.Add(body);}return body;
        }
        void ToggleScene(bool locked)
        {
            if(bodySelection.Count>0)
            {
                DetachBody(false);foreach(int i in selection){if(locked)Result[i].Locked=true;else Result[i].Hidden=true;}sceneStamp="";canvas.Invalidate();return;
            }
            if((string)objectTree.SelectedNode?.Tag=="$body"||NodeDrawing(objectTree.SelectedNode)?.Kind=="Body"){Remember(locked?"切换正文锁定":"切换正文显示");var b=EnsureBodyOwner();if(locked)b.Locked=!b.Locked;else b.Hidden=!b.Hidden;}
            else if(selected>=0){var d=Result[selected];Remember(locked?"切换锁定":"切换显示");if(locked)d.Locked=!d.Locked;else d.Hidden=!d.Hidden;}
            sceneStamp="";canvas.Invalidate();
        }
        void RenameScene()
        {
            if(selected<0||!PaperScene.Editable(Result[selected],Result))return;var d=Result[selected];
            using(var dialog=new Form{Text="重命名",Width=350,Height=145,StartPosition=FormStartPosition.CenterParent})
            {var input=new TextBox{Dock=DockStyle.Top,Text=d.Name??"对象"};var ok=new Button{Text="确定",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};dialog.Controls.Add(input);dialog.Controls.Add(ok);dialog.AcceptButton=ok;if(dialog.ShowDialog(this)==DialogResult.OK&&!string.IsNullOrWhiteSpace(input.Text)){Remember("重命名");d.Name=input.Text.Trim();}}
        }
        void SelectMembers()
        {
            if(selected<0)return;var d=Result[selected];selection.Clear();for(int i=0;i<Result.Count;i++)if(!PaperScene.IsContainer(Result[i])&&CanTouch(Result[i])&&PaperScene.Contains(d,Result[i],Result))selection.Add(i);selected=selection.Count>0?selection.First():-1;canvas.Invalidate();
        }
        void ReorderScene(int direction)
        {
            if(selected<0||!SceneUnlocked(Result[selected]))return;var moving=SelectedScene().Select(i=>Result[i]).ToList();var d=Result[selected];
            var others=Result.Where(x=>x.Page==d.Page&&!moving.Contains(x)&&x.Kind!="Slot"&&x.LayerId==d.LayerId&&x.GroupId==d.GroupId&&x.SectionId==d.SectionId).ToList();
            int at=Result.IndexOf(d);var neighbor=direction>0?others.FirstOrDefault(x=>Result.IndexOf(x)>at):others.LastOrDefault(x=>Result.IndexOf(x)<at);if(neighbor==null)return;
            Remember(direction>0?"上移一层":"下移一层");Result.RemoveAll(x=>moving.Contains(x));int target=Result.IndexOf(neighbor)+(direction>0?1:0);Result.InsertRange(target,moving);selected=Result.IndexOf(d);selection.Clear();selection.Add(selected);sceneStamp="";canvas.Invalidate();
        }
        void AssignScene(string kind)
        {
            if(selection.Count==0)return;var choices=Result.Where(d=>d.Kind==kind&&d.Page==(int)page.Value&&PaperScene.Editable(d,Result)).ToList();if(choices.Count==0){MessageBox.Show(this,kind=="Layer"?"请先新建图层。":"请先拖出页面分区。");return;}
            using(var dialog=new Form{Text=kind=="Layer"?"移入图层":"移入分区",Width=350,Height=145,StartPosition=FormStartPosition.CenterParent})
            {var list=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};foreach(var d in choices)list.Items.Add(d.Name??d.Kind);list.SelectedIndex=0;var ok=new Button{Text="确定",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};dialog.Controls.Add(list);dialog.Controls.Add(ok);dialog.AcceptButton=ok;if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                var target=choices[list.SelectedIndex];var copy=Copy(Result);foreach(int i in SelectedScene())if(copy[i].Id!=target.Id&&PaperScene.Editable(Result[i],Result)){if(kind=="Layer")copy[i].LayerId=target.Id;else copy[i].SectionId=target.Id;}
                try{PageDrawing.Validate(copy,sheet);Remember("移入"+kind);Result=copy;}catch(Exception ex){MessageBox.Show(this,ex.Message);}
            }canvas.Invalidate();
        }
        void DetachContainer()
        {
            if(selected<0)return;Remember("脱离容器");foreach(int i in SelectedScene())if(CanTouch(Result[i])){Result[i].LayerId=Result[i].GroupId=Result[i].SectionId=null;}canvas.Invalidate();
        }
        void EnterSection()
        {
            if(selected<0||Result[selected].Kind!="Section"||!PaperScene.Editable(Result[selected],Result))return;activeSection=Result[selected].Id;bodySelection.Clear();selection.Clear();selected=-1;tools.SelectedItem="选择";sceneStamp="";canvas.Invalidate();
        }
        void DeleteScene()
        {
            if(bodySelection.Count>0){DetachBody(true);return;}
            if(selected<0||!SceneUnlocked(Result[selected])||SelectedScene().Any(i=>PaperScene.IsLocked(Result[i],Result)))return;var remove=SelectedScene().Where(i=>Result[i].Kind!="Slot").OrderByDescending(i=>i).ToArray();if(remove.Length==0)return;
            Remember("删除对象");foreach(int i in remove)Result.RemoveAt(i);selected=-1;selection.Clear();sceneStamp="";RefreshBody();canvas.Invalidate();
        }
        void AttachNewObject(PageDrawing d)
        {
            AutoName(d);
            if(d.Kind=="Slot")return;var layer=Result.FirstOrDefault(x=>x.Id==activeLayer&&x.Page==d.Page);if(layer!=null&&PaperScene.Editable(layer,Result))d.LayerId=layer.Id;
            if(d.Kind!="Section"&&d.Kind!="Reserve")d.SectionId=activeSection;
        }
        void AutoName(PageDrawing d)
        {
            if(!string.IsNullOrWhiteSpace(d.Name))return;
            string type=d.Kind=="Reserve"?"正文预留区":d.Kind=="Layer"?"图层":d.Kind=="Group"?"分组":d.Kind=="Section"?"页面分区":d.Kind=="Body"?"正文":d.Text!=null?"文字批注":new[]{"直线","矩形","圆","箭头","画笔","预留绘图区","页面分区"}.Contains(tools.Text)?tools.Text=="预留绘图区"?"正文预留区":tools.Text:d.Paths.Count>0&&d.Paths[0].Length>100?"圆":d.Paths.Count>0&&d.Paths[0].Length==10?"矩形":"笔迹";
            int n=1;while(Result.Any(x=>x.Id!=d.Id&&x.Name==type+n))n++;d.Name=type+n;
        }
        LayoutTextAnchor HitBody(PointF p)
        {
            if(!BodyVisible)return null;string text=DrawingAnchors.Canonical(BodyText);
            return pages.SelectMany(x=>x.Anchors).Where(a=>a.Page==(int)page.Value&&a.Index<text.Length&&!char.IsWhiteSpace(text[a.Index])&&new RectangleF((float)a.X,(float)a.Y,(float)a.Width,(float)a.Height).Contains(p)).LastOrDefault();
        }
        bool SelectBodyDown(MouseEventArgs e)
        {
            bool tool=tools.Text=="选择"||tools.Text=="移动"||tools.Text=="正文改字";if(!tool)return false;
            var hit=HitBody(start);
            if(selection.Count>0&&selection.All(i=>i>=0&&i<Result.Count&&CanTouch(Result[i]))&&HitSelection(start))return false;
            // Independent objects have priority over the source text beneath them.
            if(Result.Any(d=>d.Page==(int)page.Value&&!PaperScene.IsGuide(d)&&CanTouch(d)&&d.Bounds().Contains(start)))return false;
            if(hit==null){bodyArmed=false;bodySelection.Clear();return false;}
            if(!BodyUnlocked)return true;
            bool wasSelected=bodySelection.Contains(hit.Index);
            if(e.Clicks>1){armedBody=hit.Index;return true;}
            bodyArmed=wasSelected;armedBody=hit.Index;
            if((ModifierKeys&Keys.Control)==0&&!wasSelected)bodySelection.Clear();
            if((ModifierKeys&Keys.Control)!=0&&wasSelected)bodySelection.Remove(hit.Index);else bodySelection.Add(hit.Index);
            selected=-1;selection.Clear();bodyDrag=bodySelection.Count>0;canvas.Capture=bodyDrag;canvas.Invalidate();return true;
        }
        void SceneDoubleClick(MouseEventArgs e)
        {
            if(tools.Text!="选择"&&tools.Text!="移动"&&tools.Text!="正文改字")return;
            if(bodyArmed&&bodySelection.Contains(armedBody)&&BodyUnlocked){bodyDrag=false;canvas.Capture=false;BeginInline(e.Location);bodyArmed=false;return;}
            if(selected>=0&&CanTouch(Result[selected])){if(Result[selected].Kind=="Section")EnterSection();else if(Result[selected].Text!=null)EditText();}
        }
        void DetachBody(bool delete)
        {
            if(!BodyUnlocked||bodySelection.Count==0)return;
            var anchors=pages.SelectMany(x=>x.Anchors).Where(a=>a.Page==(int)page.Value&&bodySelection.Contains(a.Index)).OrderBy(a=>a.Index).ToList();if(anchors.Count==0)return;
            string canonical=DrawingAnchors.Canonical(BodyText);var chars=canonical.ToCharArray();var next=Copy(Result);var detached=new List<PageDrawing>();
            try
            {
            foreach(var a in anchors)
            {
                var box=new RectangleF((float)a.X,(float)a.Y,(float)a.Width,(float)a.Height);string text=canonical.Substring(a.Index,1);
                var slot=new PageDrawing{Kind="Slot",Page=a.Page,SourceStart=a.Index,Text=text,Paths=new List<double[]>{BoxPath(box)}};next.Add(slot);chars[a.Index]=' ';
                if(!delete)
                {
                    pendingStyle=AnnotationDefaults.Clone();bool exact=a.Paths!=null&&a.Paths.Count>0;var d=new PageDrawing{Page=a.Page,Text=text,Name="独立正文 · "+text,Paths=exact?a.Paths.ConvertAll(path=>(double[])path.Clone()):MakeTextPaths(text),TextStyle=pendingStyle.Clone()};if(!exact)Translate(d,a.X,a.Y);AttachNewObject(d);next.Add(d);detached.Add(d);
                }
            }
            string candidate=WritingEditorText.Normalize(new string(chars));
                var layout=ReflowText(candidate,next);PageDrawing.Validate(next,sheet);Remember(delete?"删除正文并保留空位":"正文转独立文字");Result=next;pages=layout;BodyText=candidate;bodySelection.Clear();selection.Clear();foreach(var d in detached)selection.Add(Result.IndexOf(d));selected=selection.Count>0?selection.First():-1;tools.SelectedItem="移动";canvas.Invalidate();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"正文未移动");}
        }
        void PaintScene(Graphics g)
        {
            foreach(var a in pages.SelectMany(p=>p.Anchors).Where(a=>a.Page==(int)page.Value&&bodySelection.Contains(a.Index)))
            {var p=Pixel(a.X,a.Y);var q=Pixel(a.X+a.Width,a.Y+a.Height);using(var fill=new SolidBrush(Color.FromArgb(25,30,130,240)))g.FillRectangle(fill,p.X,p.Y,q.X-p.X,q.Y-p.Y);g.DrawRectangle(Pens.DodgerBlue,p.X,p.Y,q.X-p.X,q.Y-p.Y);}
            foreach(var d in Result.Where(d=>d.Page==(int)page.Value&&d.Kind=="Section"&&PaperScene.Visible(d,Result)))
            {var b=d.Bounds();var a=Pixel(b.Left,b.Top);var z=Pixel(b.Right,b.Bottom);using(var pen=new Pen(d.Id==activeSection?Color.RoyalBlue:Color.Teal,1.5f)){pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;g.DrawRectangle(pen,a.X,a.Y,z.X-a.X,z.Y-a.Y);}g.DrawString(d.Name??"分区",Font,Brushes.Teal,a);}
            if(!showRulers)return;
            g.FillRectangle(Brushes.WhiteSmoke,0,0,canvas.Width,22);g.FillRectangle(Brushes.WhiteSmoke,0,22,32,canvas.Height-22);g.DrawString("mm",Font,Brushes.DimGray,2,2);
            var paper=Paper();double scale=paper.Width/sheet.WidthMm;int step=scale<2?20:scale<5?10:5;
            for(int x=0;x<=sheet.WidthMm;x+=step){var p=Pixel(x,0);if(p.X<32||p.X>canvas.Width)continue;g.DrawLine(Pens.Gray,p.X,16,p.X,22);g.DrawString(x.ToString(),Font,Brushes.DimGray,p.X+2,1);}
            for(int y=0;y<=sheet.HeightMm;y+=step){var p=Pixel(0,y);if(p.Y<22||p.Y>canvas.Height)continue;g.DrawLine(Pens.Gray,26,p.Y,32,p.Y);g.DrawString(y.ToString(),Font,Brushes.DimGray,1,p.Y+1);}
        }
    }
}
