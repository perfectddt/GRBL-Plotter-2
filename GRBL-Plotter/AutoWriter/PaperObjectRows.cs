using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace GrblPlotter.AutoWriter
{
    internal sealed partial class PaperEditor
    {
        void InitializeObjectRows(TabControl tabs)
        {
            tabs.Multiline=true;
            foreach(var panel in Controls.OfType<FlowLayoutPanel>())
            {
                panel.Padding=new Padding(6,24,6,8);
                Func<Control,string> category=c=>{
                    string name=c.Name;
                    if(new[]{"选择","移动","正文改字","套索"}.Contains(name))return "选择与定位";
                    if(new[]{"画笔","直线","矩形","圆","箭头","橡皮擦","编辑节点"}.Contains(name))return "绘图与擦除";
                    if(new[]{"文字批注","修改批注","转为笔画","合并文字","编辑正文"}.Contains(name))return "文字";
                    if(new[]{"删除","撤销","重做"}.Contains(name))return "历史与删除";
                    if(new[]{"跟随文字","固定页位置","文字环绕","更新正文排版"}.Contains(name))return "正文排版";
                    if(new[]{"应用并关闭","取消"}.Contains(name))return "完成编辑";
                    if(new[]{"适应纸面","标尺 XY","整页"}.Contains(name))return "画布视图";
                    return "页面与分区";
                };
                var ordered=panel.Controls.Cast<Control>().ToArray().GroupBy(category).ToArray();
                panel.Controls.Clear();
                foreach(var group in ordered){bool first=true;foreach(var control in group){control.Margin=new Padding(first?12:2,0,2,0);panel.Controls.Add(control);first=false;}}
                panel.Paint+=(sender,args)=>{
                    int index=0;
                    foreach(var group in panel.Controls.Cast<Control>().GroupBy(category))
                    {
                        foreach(var row in group.GroupBy(c=>c.Top))
                        {
                            int left=row.Min(c=>c.Left)-6,right=row.Max(c=>c.Right)+5,top=row.Key-20;
                            var box=new Rectangle(left,top,right-left,row.Max(c=>c.Height)+25);
                            Color[] fills={Color.FromArgb(226,237,250),Color.FromArgb(228,243,235),Color.FromArgb(243,232,248),Color.FromArgb(250,238,220)};
                            using(var brush=new SolidBrush(fills[index%fills.Length]))args.Graphics.FillRectangle(brush,box);
                            using(var pen=new Pen(Color.FromArgb(195,205,218)))args.Graphics.DrawRectangle(pen,box);
                            TextRenderer.DrawText(args.Graphics,group.Key,panel.Font,new Point(left+6,top+2),Color.FromArgb(65,78,95));
                        }
                        index++;
                    }
                };
                panel.Layout+=(sender,args)=>panel.Invalidate();
                foreach(var b in panel.Controls.OfType<Button>())
                {
                    if(b.Name=="修改批注"||b.Name=="转为笔画"||b.Name=="合并文字"){b.Text=b.Name=="修改批注"?"改":b.Name=="合并文字"?"并":"拆";b.Width=38;}
                    if(b.Name=="橡皮擦")
                    {
                        var eraser=new Bitmap(22,22);using(var g=Graphics.FromImage(eraser)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;Point[] points={new Point(3,13),new Point(13,3),new Point(20,10),new Point(10,20),new Point(7,20)};g.FillPolygon(Brushes.LightCoral,points);g.DrawPolygon(Pens.DimGray,points);g.DrawLine(Pens.DimGray,7,9,15,17);g.DrawLine(Pens.DimGray,5,21,21,21);}b.Text="";b.Image=eraser;FormClosed+=(sender,args)=>eraser.Dispose();
                    }
                }                var select=panel.Controls.OfType<Button>().FirstOrDefault(b=>b.Name=="选择");if(select!=null){var icon=new Bitmap(20,20);using(var g=Graphics.FromImage(icon)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.FillPolygon(Brushes.Black,new[]{new Point(4,2),new Point(4,17),new Point(8,13),new Point(12,19),new Point(15,17),new Point(11,11),new Point(17,11)});}select.Text="";select.Image=icon;FormClosed+=(s,e)=>icon.Dispose();}
            }
            var actions=objectTree.Parent.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if(actions!=null)
            {
                var keep=actions.Controls.OfType<Button>().Where(b=>!new[]{"编组","解组","显示 / 隐藏","锁定 / 解锁","重命名","上移一层","下移一层","删除","选择成员"}.Contains(b.Text)).ToArray();
                actions.Controls.Clear();actions.RowCount=3;actions.Height=108;foreach(var b in keep)actions.Controls.Add(b);
                foreach(var b in keep)if(b.Text=="脱离容器"){b.Text="移回默认纸面";sceneTips.SetToolTip(b,"解除所属图层、分区和分组；保留纸面位置");}
            }
            var groups=new TreeView{Dock=DockStyle.Fill,HideSelection=false};
            var groupTab=new TabPage("分组");groupTab.Controls.Add(groups);tabs.TabPages.Add(groupTab);
            var groupActions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36};
            groupActions.Controls.Add(Ui.UiTheme.MakeButton("编组",GroupSelected,Ui.UiButtonKind.Normal));groupActions.Controls.Add(Ui.UiTheme.MakeButton("解组",UngroupSelected,Ui.UiButtonKind.Normal));groupTab.Controls.Add(groupActions);
            Action refresh=()=>{
                groups.Nodes.Clear();foreach(var g in Result.Where(x=>x.Kind=="Group"&&x.Page==(int)page.Value))
                {var node=groups.Nodes.Add(g.Name??"分组");node.Tag=g.Id;foreach(var d in Result.Where(x=>x.GroupId==g.Id))node.Nodes.Add(new TreeNode(d.Name??(d.Kind=="Layer"?"图层":d.Text??"对象")){Tag=d.Id});node.Expand();}
                var ungrouped=groups.Nodes.Add("未分组");foreach(var d in Result.Where(x=>x.Page==(int)page.Value&&x.GroupId==null&&x.Kind!="Group"&&x.Kind!="Slot"&&x.Kind!="Body"))ungrouped.Nodes.Add(new TreeNode(d.Name??d.Text??"对象"){Tag=d.Id});ungrouped.Expand();
            };
            tabs.SelectedIndexChanged+=(s,e)=>refresh();
            groups.AfterSelect+=(s,e)=>SelectTree(e.Node);
            foreach(var tree in new[]{objectTree,groups})
            {
                int[] previousSelection=new int[0];
                tree.MouseDown+=(s,e)=>previousSelection=selection.ToArray();
                tree.DrawMode=TreeViewDrawMode.OwnerDrawText;tree.ItemHeight=27;tree.LabelEdit=true;tree.AllowDrop=true;
                tree.DrawNode+=(s,e)=>{
                    var d=NodeDrawing(e.Node);var box=new Rectangle(e.Bounds.X,e.Bounds.Y,Math.Max(1,tree.ClientSize.Width-e.Bounds.X),tree.ItemHeight);
                    bool selectedNode=d!=null?selection.Contains(Result.IndexOf(d)):tree.SelectedNode==e.Node;e.Graphics.FillRectangle(selectedNode?SystemBrushes.Highlight:SystemBrushes.Window,box);
                    Color color=d==null?Color.Gray:d.Kind=="Layer"?Color.RoyalBlue:d.Kind=="Group"?Color.DarkViolet:d.Kind=="Section"?Color.Teal:Color.FromArgb(110,60,35);
                    var textBox=box;textBox.Width=Math.Max(1,textBox.Width-(d==null?0:92));TextRenderer.DrawText(e.Graphics,e.Node.Text,tree.Font,textBox,selectedNode?Color.White:color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    if(d!=null){string[] icons={"↑","↓","",d.Hidden?"⊘":"◉"};for(int i=0;i<4;i++)TextRenderer.DrawText(e.Graphics,icons[i],tree.Font,new Rectangle(tree.ClientSize.Width-92+i*23,e.Bounds.Y,23,tree.ItemHeight),selectedNode?Color.White:color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                        using(var pen=new Pen(selectedNode?Color.White:color,1.4f)){float x=tree.ClientSize.Width-41,y=e.Bounds.Y+7;e.Graphics.DrawRectangle(pen,x,y+6,10,8);if(d.Locked)e.Graphics.DrawArc(pen,x+2,y,6,10,180,180);else e.Graphics.DrawArc(pen,x+5,y-1,7,11,180,180);e.Graphics.DrawLine(pen,x+5,y+9,x+5,y+12);}
                    }
                };
                tree.NodeMouseClick+=(s,e)=>{
                    var d=NodeDrawing(e.Node);if(d==null||e.X<tree.ClientSize.Width-92)return;SelectTree(e.Node);int cell=(e.X-(tree.ClientSize.Width-92))/23;
                    if(cell<2)ReorderScene(cell==0?1:-1);else{Remember(cell==2?"切换锁定":"切换显示");if(cell==2)d.Locked=!d.Locked;else d.Hidden=!d.Hidden;canvas.Invalidate();}
                    sceneStamp="";RefreshScene();if(tree==groups)refresh();tree.Invalidate();
                };
                tree.MouseMove+=(s,e)=>{if(e.X>=tree.ClientSize.Width-92){string[] tips={"上移一层","下移一层","锁定 / 解锁","显示 / 隐藏"};sceneTips.SetToolTip(tree,tips[Math.Min(3,(e.X-(tree.ClientSize.Width-92))/23)]);}else sceneTips.SetToolTip(tree,"双击名称重命名；拖到图层或分组以添加成员，Ctrl 单击多选");};
                tree.NodeMouseDoubleClick+=(s,e)=>{if(e.X<tree.ClientSize.Width-92&&NodeDrawing(e.Node)!=null)e.Node.BeginEdit();};
                tree.AfterLabelEdit+=(s,e)=>{var d=NodeDrawing(e.Node);if(d==null||e.Label==null)return;if(!SceneUnlocked(d)||string.IsNullOrWhiteSpace(e.Label)){e.CancelEdit=true;return;}Remember("重命名");d.Name=e.Label.Trim();sceneStamp="";};
                tree.ItemDrag+=(s,e)=>{var node=e.Item as TreeNode;if(NodeDrawing(node)!=null)tree.DoDragDrop(node.Tag,DragDropEffects.Move);};
                tree.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(typeof(string))?DragDropEffects.Move:DragDropEffects.None;
                tree.DragDrop+=(s,e)=>{
                    var target=NodeDrawing(tree.GetNodeAt(tree.PointToClient(new Point(e.X,e.Y))));var source=Result.FirstOrDefault(x=>x.Id==(e.Data.GetData(typeof(string)) as string));
                    if(target==null||source==null||target.Id==source.Id||!SceneUnlocked(source)||!SceneUnlocked(target))return;
                    var copy=Copy(Result);var item=copy.First(x=>x.Id==source.Id);
                    if(target.Kind=="Layer")item.LayerId=target.Id;else if(target.Kind=="Group")item.GroupId=target.Id;else return;
                    try{PageDrawing.Validate(copy,sheet);Remember("拖动对象归属");Result=copy;sceneStamp="";RefreshScene();refresh();canvas.Invalidate();}catch(ArgumentException ex){MessageBox.Show(this,ex.Message);}
                };
                tree.NodeMouseClick+=(s,e)=>{if((ModifierKeys&Keys.Control)==0)return;var d=NodeDrawing(e.Node);if(d!=null){foreach(int old in previousSelection)selection.Add(old);int i=Result.IndexOf(d);selection.Add(i);selected=i;canvas.Invalidate();}};
            }
        }
    }
}
