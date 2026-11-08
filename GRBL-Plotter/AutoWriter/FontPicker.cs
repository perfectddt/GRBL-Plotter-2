using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter
{
    internal sealed class FontPicker:Form
    {
        readonly ListBox available=new ListBox{Dock=DockStyle.Fill,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=90,IntegralHeight=false};
        readonly ListBox chosen=new ListBox{SelectionMode=SelectionMode.MultiExtended,Dock=DockStyle.Fill,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=90};
        readonly TabControl selectedViews=new TabControl{Dock=DockStyle.Top,Height=27};
        readonly List<FontMixSource> visibleSelected=new List<FontMixSource>();
        FontDeformation deformation=new FontDeformation();bool deformationConfigured;
        Random lastPreviewRandom;
        readonly Dictionary<FontMixSource,Random> sourcePreviewRandoms=new Dictionary<FontMixSource,Random>();
        int SelectedIndex {get{return chosen.SelectedIndex<0||chosen.SelectedIndex>=visibleSelected.Count?-1:selected.IndexOf(visibleSelected[chosen.SelectedIndices.Cast<int>().Last()]);}}
        readonly ComboBox role=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly TextBox sampleInput=new TextBox{Dock=DockStyle.Top,Text="永和国工王一"};
        readonly TextBox search=new TextBox{Dock=DockStyle.Fill};
        readonly NumericUpDown weight=new NumericUpDown{Minimum=1,Maximum=1000000,Value=1,Dock=DockStyle.Fill};
        readonly Panel preview=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};
        readonly List<GlyphFontPackInfo> fonts=new List<GlyphFontPackInfo>();
        readonly List<FontMixSource> selected=new List<FontMixSource>();
        readonly Dictionary<string,Image> thumbnails=new Dictionary<string,Image>();
        readonly Label detail=new Label{Dock=DockStyle.Bottom,Height=70};
        GlyphFontPack previewPack;string previewError="";bool syncing;
        internal string SelectedPath;
        internal Action<string> ReportError;
        internal FontPicker(string current):this(current,null){}
        internal FontPicker(string current,string legacySupplement)
        {
            Text="字体 · 本地字库与混写";Width=1180;Height=690;MinimumSize=new Size(1000,640);StartPosition=FormStartPosition.CenterParent;Font=Ui.UiTheme.SafeFont(9f);
            foreach(string path in GlyphFontPackCatalog.DiscoverPackPaths().Where(p=>!p.EndsWith(".mix.fontpack.json",StringComparison.OrdinalIgnoreCase)))
                try{fonts.Add(GlyphFontPack.Peek(path));}catch(Exception){}
            if(!string.IsNullOrEmpty(current))
            {
                if(current.EndsWith(".mix.fontpack.json",StringComparison.OrdinalIgnoreCase))try{using(var stream=File.OpenRead(current)){var data=(GlyphFontPackFile)new DataContractJsonSerializer(typeof(GlyphFontPackFile)).ReadObject(stream);selected.AddRange(data.MixSources);deformation=data.Deformation??new FontDeformation();deformationConfigured=data.Deformation!=null;}}catch(Exception ex){previewError=ex.Message;}
                else selected.Add(new FontMixSource{Path=current,Weight=1,Sha256=WritingProjectService.Fingerprint(current)});
            }
            if(!string.IsNullOrEmpty(legacySupplement)&&!selected.Any(s=>string.Equals(s.Path,legacySupplement,StringComparison.OrdinalIgnoreCase)))selected.Add(new FontMixSource{Path=legacySupplement,Sha256=WritingProjectService.Fingerprint(legacySupplement),Weight=1,Fallback=true});
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(10)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,22));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            var left=new Panel{Dock=DockStyle.Fill,Padding=new Padding(4)};var filter=new TableLayoutPanel{Dock=DockStyle.Top,Height=36,ColumnCount=2};filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));filter.Controls.Add(search);filter.Controls.Add(Button("清除",()=>search.Clear()));left.Controls.Add(available);left.Controls.Add(filter);left.Controls.Add(new Label{Text="本地字体 · 搜索名称 · 双击添加",Dock=DockStyle.Top,Height=28});
            role.Items.AddRange(new object[]{"权重混写（主字库）","缺字补充（不参与抽选）"});role.SelectedIndex=0;
            var middle=new Panel{Dock=DockStyle.Fill,Padding=new Padding(8)};var commands=new TableLayoutPanel{Dock=DockStyle.Top,Height=390,ColumnCount=1,RowCount=11};commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<11;i++)commands.RowStyles.Add(new RowStyle(SizeType.Percent,100f/11));commands.Controls.Add(new Label{Text="已选字体用途",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft});commands.Controls.Add(role);commands.Controls.Add(new Label{Text="主字库权重",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft});commands.Controls.Add(weight);commands.Controls.Add(Button("添加主字库 →",AddSelected));commands.Controls.Add(Button("添加补充字库 →",()=>AddToSelection(true)));commands.Controls.Add(Button("← 移除",RemoveSelected));commands.Controls.Add(Button("全局变形…",EditDeformation));commands.Controls.Add(Button("所选字体变形…",EditSourceDeformation));commands.Controls.Add(Button("确定选择",Confirm));commands.Controls.Add(Button("取消",()=>{DialogResult=DialogResult.Cancel;if(!Modal)Hide();}));middle.Controls.Add(commands);middle.Controls.Add(new Label{Text="主字库：按权重抽选。\n权重越大，选中概率越高。\n\n补充字库：只有全部主字库\n都缺字时才使用，不参与抽选。\n多个补充包按列表顺序兜底。\n\n先点右侧字体，再改用途/权重。",Dock=DockStyle.Bottom,Height=155});            var right=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};right.RowStyles.Add(new RowStyle(SizeType.Percent,50));right.RowStyles.Add(new RowStyle(SizeType.Percent,50));right.RowStyles.Add(new RowStyle(SizeType.Absolute,70));var picks=new Panel{Dock=DockStyle.Fill};picks.Controls.Add(chosen);selectedViews.TabPages.Add("全部");selectedViews.TabPages.Add("主字库");selectedViews.TabPages.Add("补充字库");picks.Controls.Add(selectedViews);selectedViews.SelectedIndexChanged+=(s,e)=>RefreshChosen();picks.Controls.Add(new Label{Text="已选字体 · Ctrl / Shift 多选后批量变形",Dock=DockStyle.Top,Height=28});right.Controls.Add(picks,0,0);var previewHost=new Panel{Dock=DockStyle.Fill};previewHost.Controls.Add(preview);previewHost.Controls.Add(sampleInput);previewHost.Controls.Add(new Label{Text="组合笔迹预览 · 可修改示例文字",Dock=DockStyle.Top,Height=25});right.Controls.Add(previewHost,0,1);right.Controls.Add(detail,0,2);layout.Controls.Add(left,0,0);layout.Controls.Add(middle,1,0);layout.Controls.Add(right,2,0);Controls.Add(layout);
            search.TextChanged+=(s,e)=>Filter();available.SelectedIndexChanged+=(s,e)=>LoadPreview((available.SelectedItem as GlyphFontPackInfo)?.Path);available.DoubleClick+=(s,e)=>AddSelected();available.DrawItem+=DrawCard;chosen.DrawItem+=DrawCard;sampleInput.TextChanged+=(s,e)=>preview.Invalidate();
            chosen.SelectedIndexChanged+=(s,e)=>{if(syncing||SelectedIndex<0)return;syncing=true;weight.Value=(decimal)selected[SelectedIndex].Weight;role.SelectedIndex=selected[SelectedIndex].Fallback?1:0;weight.Enabled=!selected[SelectedIndex].Fallback;syncing=false;LoadPreview(selected[SelectedIndex].Path);};
            weight.ValueChanged+=(s,e)=>{if(syncing||SelectedIndex<0)return;int index=SelectedIndex;selected[index].Weight=(double)weight.Value;chosen.Items[chosen.SelectedIndex]=Caption(selected[index]);chosen.Invalidate();preview.Invalidate();};
            role.SelectedIndexChanged+=(s,e)=>{if(syncing||SelectedIndex<0)return;int i=SelectedIndex;selected[i].Fallback=role.SelectedIndex==1;weight.Enabled=!selected[i].Fallback;RefreshChosen();};
            preview.Paint+=DrawPreview;Filter();RefreshChosen();Disposed+=(s,e)=>{foreach(var image in thumbnails.Values)image.Dispose();};
        }
        static Button Button(string text,Action action){var button=Ui.UiTheme.MakeButton(text,action,Ui.UiButtonKind.Normal);button.Dock=DockStyle.Fill;return button;}
        static string Caption(FontMixSource item){return GlyphFontPack.Peek(item.Path).Name+(item.Fallback?"（缺字补充）":"（权重："+item.Weight+"）");}
        void Filter(){available.BeginUpdate();available.Items.Clear();foreach(var f in fonts.Where(f=>f.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0))available.Items.Add(f);available.EndUpdate();}
        void RefreshChosen(){syncing=true;chosen.Items.Clear();visibleSelected.Clear();foreach(var item in selected.Where(x=>selectedViews.SelectedIndex<=0||(selectedViews.SelectedIndex==1?!x.Fallback:x.Fallback))){visibleSelected.Add(item);chosen.Items.Add(Caption(item));}syncing=false;if(chosen.Items.Count>0)chosen.SelectedIndex=chosen.Items.Count-1;preview.Invalidate();}
        void AddSelected(){AddToSelection(false);}
        void AddToSelection(bool fallback){var f=available.SelectedItem as GlyphFontPackInfo;if(f==null||selected.Any(x=>x.Path==f.Path))return;selected.Add(new FontMixSource{Path=f.Path,Sha256=WritingProjectService.Fingerprint(f.Path),Weight=1,Fallback=fallback});RefreshChosen();}
        void RemoveSelected(){if(SelectedIndex<0)return;selected.RemoveAt(SelectedIndex);RefreshChosen();}
        void LoadPreview(string path){if(path==null)return;try{previewPack=GlyphFontPack.LoadFromFile(path);previewError="";detail.Text="字库："+previewPack.Name+"\n字数："+previewPack.GlyphCount+"\n"+previewPack.License;}catch(Exception ex){previewPack=null;previewError=ex.Message;detail.Text=previewError;}preview.Invalidate();}
        void DrawCard(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0)return;e.DrawBackground();bool picked=sender==chosen;var item=picked?visibleSelected[e.Index]:null;var info=picked?GlyphFontPack.Peek(item.Path):(GlyphFontPackInfo)available.Items[e.Index];
            var image=Thumbnail(info.Path);e.Graphics.DrawImage(image,e.Bounds.Left+3,e.Bounds.Top+3,100,82);
            int left=e.Bounds.Left+110,width=Math.Max(1,e.Bounds.Width-115);
            TextRenderer.DrawText(e.Graphics,info.Name,Font,new Rectangle(left,e.Bounds.Top+8,width,38),e.ForeColor,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics,info.GlyphCount+" 字",Font,new Rectangle(left,e.Bounds.Top+45,width,18),e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,picked?(item.Fallback?"缺字补充 · 不参与权重":"主字库 · 权重："+item.Weight)+(item.Deformation!=null&&item.Deformation.Enabled?" · 独立变形":""):"本地字库",Font,new Rectangle(left,e.Bounds.Top+66,width,20),e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            e.Graphics.DrawLine(Pens.LightGray,e.Bounds.Left,e.Bounds.Bottom-1,e.Bounds.Right,e.Bounds.Bottom-1);e.DrawFocusRectangle();
        }
        Image Thumbnail(string path)
        {
            Image image;if(thumbnails.TryGetValue(path,out image))return image;
            string png=path+".preview.png";if(File.Exists(png))try{using(var source=Image.FromFile(png))image=new Bitmap(source);thumbnails[path]=image;return image;}catch(Exception){}
            var bitmap=new Bitmap(110,90);using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                try{var pack=GlyphFontPack.LoadFromFile(path);var sample="永和国工王一Aa01".Where(pack.Contains).Take(4).ToArray();if(sample.Length==0)sample=pack.Characters.OrderBy(c=>c).Take(4).ToArray();int j=0;foreach(char c in sample){DrawGlyph(g,pack.Resolve(c),4.0,3+(j%2)*53,2+(j/2)*43,3.3);j++;}}
                catch(Exception){g.DrawString("预览不可用",Font,Brushes.Gray,4,32);}
            }thumbnails[path]=bitmap;return bitmap;
        }
        static void DrawGlyph(Graphics graphics,BuiltinGlyph glyph,double sx,double x,double y,double sy)
        {
            foreach(var stroke in glyph.Strokes)for(int i=1;i<stroke.Points.Count;i++){var a=stroke.Points[i-1];var b=stroke.Points[i];graphics.DrawLine(Pens.Black,(float)(x+a.X*sx),(float)(y+(12-a.Y)*sy),(float)(x+b.X*sx),(float)(y+(12-b.Y)*sy));}
        }
        BuiltinGlyph PreviewGlyph(char symbol,Random random)
        {
            if(!object.ReferenceEquals(lastPreviewRandom,random)){lastPreviewRandom=random;sourcePreviewRandoms.Clear();}
            var choices=selected.Where(s=>!s.Fallback).Select(s=>new{Source=s,Pack=GlyphFontPack.LoadFromFile(s.Path)}).Where(s=>s.Pack.Contains(symbol)).ToList();
            if(choices.Count>0){double pick=random.NextDouble()*choices.Sum(s=>s.Source.Weight);foreach(var choice in choices){pick-=choice.Source.Weight;if(pick<0)return ApplySourcePreview(choice.Source,choice.Pack.Resolve(symbol),random);}return ApplySourcePreview(choices.Last().Source,choices.Last().Pack.Resolve(symbol),random);}
            foreach(var source in selected.Where(s=>s.Fallback)){var pack=GlyphFontPack.LoadFromFile(source.Path);if(pack.Contains(symbol))return ApplySourcePreview(source,pack.Resolve(symbol),random);}
            if(selected.Count==0&&previewPack!=null&&previewPack.Contains(symbol))return previewPack.Resolve(symbol);
            return BuiltinGlyphLibrary.Resolve(symbol);
        }
        void DrawPreview(object sender,PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;int index=0;var random=new Random(20261108);var deformRandom=new Random(deformation.Seed);string text=sampleInput.Text.Replace("\r","").Replace("\n","");double cursor=0;int columns=3,rows=Math.Max(1,(text.Length+columns-1)/columns);double scale=Math.Min(preview.Width/(columns*13.0+1),preview.Height/(rows*12.0+1));
            foreach(char c in text){if(index%columns==0)cursor=0;try{var glyph=deformation.Apply(PreviewGlyph(c,random),deformRandom);DrawGlyph(e.Graphics,glyph,scale,cursor*scale,(index/columns)*12*scale,scale);cursor+=glyph.AdvanceMm+glyph.SpacingScale;}catch(Exception){e.Graphics.DrawString(c+" 缺字",Font,Brushes.Firebrick,(float)(cursor*scale),(float)((index/columns)*12*scale));cursor+=13;}index++;}
        }
        internal static string LocalDirectory()
        {
            string exe=AppDomain.CurrentDomain.BaseDirectory;foreach(var path in new[]{Path.GetFullPath(Path.Combine(exe,"..","..","..","artifacts","local-fonts")),Path.GetFullPath(Path.Combine(exe,"..","..","local-fonts"))})if(Directory.Exists(path))return path;
            return Path.Combine(exe,"Fonts","Local");
        }
        BuiltinGlyph ApplySourcePreview(FontMixSource source,BuiltinGlyph glyph,Random random){if(source.Deformation==null)return glyph;Random rng;if(!sourcePreviewRandoms.TryGetValue(source,out rng))sourcePreviewRandoms[source]=rng=new Random(source.Deformation.Seed);return source.Deformation.Apply(glyph,rng);}
        void EditSourceDeformation()
        {
            var targets=chosen.SelectedIndices.Cast<int>().Select(i=>visibleSelected[i]).ToArray();
            if(targets.Length==0){MessageBox.Show(this,"请先选择右侧字库，可按 Ctrl / Shift 多选。");return;}
            var source=targets[0];using(var dialog=new FontDeformationForm(source.Deformation??new FontDeformation(),(c,r)=>GlyphFontPack.LoadFromFile(source.Path).ResolveWithFallback(c)))
            {dialog.Text=targets.Length==1?"所选字体变形 · "+GlyphFontPack.Peek(source.Path).Name:"批量字体变形 · "+targets.Length+" 个字库（统一替换所选项参数）";if(dialog.ShowDialog(this)==DialogResult.OK)ApplySourceDeformation(targets,dialog.Result);}
        }
        void ApplySourceDeformation(FontMixSource[] targets,FontDeformation settings)
        {settings.Validate();foreach(var target in targets)target.Deformation=settings.Clone();chosen.Invalidate();preview.Invalidate();}        void EditDeformation(){using(var dialog=new FontDeformationForm(deformation,PreviewGlyph))if(dialog.ShowDialog(this)==DialogResult.OK){deformation=dialog.Result;deformationConfigured=true;preview.Invalidate();}}
        void Confirm()
        {
            if(selected.Count==0||selected.All(x=>x.Fallback)){if(ReportError!=null)ReportError("请至少添加一个权重主字库。");else MessageBox.Show(this,"请至少添加一个权重主字库。");return;}
            try
            {
                if(selected.Count==1&&!deformationConfigured&&selected[0].Deformation==null)SelectedPath=selected[0].Path;
                else
                {
                    var data=new GlyphFontPackFile{SchemaVersion=1,Name="混写 · "+selected.Count+" 字库",Source="本机权重混写，固定种子 20261108",License="各源字体原有许可继续适用",EmSizeMm=12,AdvanceCjkMm=12,AdvanceSpaceMm=6,MixSources=selected,Deformation=deformation.Clone()};
                    string dir=LocalDirectory();Directory.CreateDirectory(dir);SelectedPath=Path.Combine(dir,"selection-"+Guid.NewGuid().ToString("N")+".mix.fontpack.json");using(var stream=File.Create(SelectedPath))new DataContractJsonSerializer(typeof(GlyphFontPackFile)).WriteObject(stream,data);GlyphFontPack.LoadFromFile(SelectedPath);
                }
                DialogResult=DialogResult.OK;if(!Modal)Hide();
            }catch(Exception ex){if(ReportError!=null)ReportError(ex.Message);else MessageBox.Show(this,ex.Message,"字体选择失败");}
        }
    }
}
