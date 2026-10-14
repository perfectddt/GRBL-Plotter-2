using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GrblPlotter.AutoWriter.Services;
namespace GrblPlotter.AutoWriter {
 internal sealed class RegionTemplateForm:Form {
  readonly RegionTemplateService service=new RegionTemplateService(RegionTemplateService.DefaultDirectory);
  readonly ListBox list=new ListBox{Dock=DockStyle.Left,Width=320,HorizontalScrollbar=true};
  readonly TextBox name=new TextBox{Width=240};
  readonly CheckBox keep=new CheckBox{Text="保留现有区域文字（按区域编号对应；多余旧区域保留）",Checked=true,AutoSize=true};
  readonly RegionCanvas preview=new RegionCanvas{Dock=DockStyle.Fill,DrawEnabled=false};
  readonly Label note=new Label{Dock=DockStyle.Bottom,Height=64};
  readonly PageLayoutOptions sheet; readonly IList<WritingRegion> current;
  public RegionTemplate SelectedTemplate{get;private set;}
  public bool KeepText{get{return keep.Checked;}}
  public RegionTemplateForm(PageLayoutOptions page,IList<WritingRegion> regions){
   sheet=page;current=regions;Text="区域模板";Width=940;Height=600;StartPosition=FormStartPosition.CenterParent;
   var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6)};bar.Controls.Add(new Label{Text="名称",AutoSize=true});bar.Controls.Add(name);
   Add(bar,"保存当前布局",()=>{var t=RegionTemplate.Create(name.Text,sheet,current);service.Save(t);RefreshList(t.Id);});
   Add(bar,"重命名",()=>{var t=Selection();var old=t.Name;try{t.Name=name.Text;t.Validate();service.Save(t);}catch{t.Name=old;throw;}RefreshList(t.Id);});
   Add(bar,"另存副本",()=>{var t=service.Copy(Selection(),name.Text);RefreshList(t.Id);});
   Add(bar,"导入",()=>{using(var d=new OpenFileDialog{Filter="区域模板|*.awtemplate"})if(d.ShowDialog(this)==DialogResult.OK){var t=RegionTemplateService.Read(d.FileName);var copy=service.Copy(t,t.Name);RefreshList(copy.Id);}});
   Add(bar,"导出",()=>{var t=Selection();using(var d=new SaveFileDialog{Filter="区域模板|*.awtemplate",FileName="区域布局.awtemplate"})if(d.ShowDialog(this)==DialogResult.OK)RegionTemplateService.Write(d.FileName,t);});
   var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(6)};bottom.Controls.Add(keep);
   Add(bottom,"应用选中模板",()=>{var t=Selection();t.Apply(current,keep.Checked);if(!keep.Checked&&MessageBox.Show(this,"将清空当前区域文字。建议先保存写字项目。确定应用？","应用模板",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;SelectedTemplate=t;DialogResult=DialogResult.OK;Close();});
   list.SelectedIndexChanged+=(s,e)=>{if(list.SelectedItem==null)return;var t=Selection();name.Text=t.Name;preview.Sheet=t.Sheet;preview.Regions=t.Apply(new List<WritingRegion>(),false);preview.Invalidate();};
   Controls.Add(preview);Controls.Add(list);Controls.Add(note);Controls.Add(bottom);Controls.Add(bar);RefreshList(null);
   Ui.UiTheme.ApplyDialogSkin(this,true);
  }
  RegionTemplate Selection(){var t=list.SelectedItem as RegionTemplate;if(t==null)throw new InvalidOperationException("请选择一个模板。");return t;}
  void Add(Control bar,string text,Action action){var button=new Button{Text=text,AutoSize=true};button.Click+=(s,e)=>{try{action();}catch(Exception error){MessageBox.Show(this,error.Message,"模板操作未完成");}};bar.Controls.Add(button);}
  void RefreshList(string id){string warning;var items=service.List(out warning);list.Items.Clear();foreach(var t in items){list.Items.Add(t);if(t.Id==id)list.SelectedItem=t;}note.Text=warning.Length>0?"以下模板无法读取："+warning:"模板仅保存纸张与区域布局，不含文字、字体或机器参数。应用后请重新生成预览。";}
 }
}
