using System;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
class RegionTemplateIntegrationTests {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(object f,string n){return f.GetType().GetField(n,flags).GetValue(f);}
 static object Call(object f,string n,params object[] args){return f.GetType().GetMethod(n,flags).Invoke(f,args);}
 static Button Find(Control c,string text){if(c is Button&&c.Text==text)return(Button)c;foreach(Control child in c.Controls){var b=Find(child,text);if(b!=null)return b;}return null;}
 static void Check(bool b,string n){if(!b)throw new Exception(n);}
 [STAThread] static int Main(string[] args){try{Run(args);Console.WriteLine("PASS actual template manager save/apply UI checks");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string[] args){
  var asm=Assembly.LoadFrom(args[0]);var type=asm.GetType("GrblPlotter.AutoWriter.MachineSetupForm",true);
  using(var form=(Form)Activator.CreateInstance(type,flags,null,new object[]{null,args[1]},null)){
   form.Show();((CheckBox)Get(form,"regionsEnabled")).Checked=true;
   var rt=asm.GetType("GrblPlotter.AutoWriter.Services.WritingRegion",true);var region=Activator.CreateInstance(rt);
   foreach(var pair in new object[][]{new object[]{"Text","工王"},new object[]{"X",10d},new object[]{"Y",10d},new object[]{"Width",60d},new object[]{"Height",40d},new object[]{"FontSize",6d}})rt.GetProperty((string)pair[0]).SetValue(region,pair[1],null);
   ((IList)Get(form,"regions")).Add(region);Call(form,"BuildWritingStrokes");((TextBox)Get(form,"writingText")).Text="old code";
   var profile=Get(form,"profile");int font=((ComboBox)Get(form,"fontSelector")).SelectedIndex;
   using(var timer=new Timer{Interval=40}){
    timer.Tick+=(s,e)=>{Form manager=null;foreach(Form candidate in Application.OpenForms)if(candidate.GetType().Name=="RegionTemplateForm"){manager=candidate;break;}if(manager==null)return;timer.Stop();
     var svc=Activator.CreateInstance(asm.GetType("GrblPlotter.AutoWriter.Services.RegionTemplateService"),new object[]{args[2]});manager.GetType().GetField("service",flags).SetValue(manager,svc);Call(manager,"RefreshList",new object[]{null});
     ((TextBox)Get(manager,"name")).Text="集成测试模板";Find(manager,"保存当前布局").PerformClick();
     var list=(ListBox)Get(manager,"list");Check(list.Items.Count==1,"manager saves current");Check(((CheckBox)Get(manager,"keep")).Checked,"preserve text default");
     var canvas=Get(manager,"preview");var previewRegions=(IList)Get(canvas,"Regions");Check((string)rt.GetProperty("Text").GetValue(previewRegions[0],null)=="","template preview contains empty boxes");
     rt.GetProperty("X").SetValue(region,30d,null);Find(manager,"应用选中模板").PerformClick();
    };timer.Start();Call(form,"ManageRegionTemplates");
   }
   var current=((IList)Get(form,"regions"))[0];Check((double)rt.GetProperty("X").GetValue(current,null)==10d,"restores original box");Check((string)rt.GetProperty("Text").GetValue(current,null)=="工王","text survives application");
   Check(((TextBox)Get(form,"writingText")).Text==""&&Get(form,"writingPages")==null,"old code and pages invalidated");Check(object.ReferenceEquals(profile,Get(form,"profile"))&&font==((ComboBox)Get(form,"fontSelector")).SelectedIndex,"font/profile untouched");
   Check(((NumericUpDown)Get(form,"pageSelector")).Value==1,"page reset");form.Close();
  }
 }
}
