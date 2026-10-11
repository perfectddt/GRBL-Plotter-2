using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
class LastSetupTests {
 static int checks; static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(Form f,string n){return f.GetType().GetField(n,flags).GetValue(f);}
 static void Assert(bool b,string n){if(!b)throw new Exception(n);checks++;}
 static Form Open(Type t,string path){return (Form)Activator.CreateInstance(t,flags,null,new object[]{null,path},null);}
 static void Save(Form f){f.GetType().GetMethod("OnFormClosing",flags).Invoke(f,new object[]{new FormClosingEventArgs(CloseReason.UserClosing,false)});}
 [STAThread] static int Main(string[] args){ try { return Run(args); } catch(Exception e) { while(e.InnerException!=null)e=e.InnerException; Console.WriteLine(e.GetType().Name+": "+e.Message);return 1;} } static int Run(string[] args){
  var a=Assembly.LoadFrom(args[0]);var t=a.GetType("GrblPlotter.AutoWriter.MachineSetupForm",true);string path=args[1];
  using(var f=Open(t,path)){
   Assert(((TextBox)Get(f,"writingInputText")).Text=="","first launch default");
   foreach(string n in new[]{"writingInputText","letterSpacingInput","lineSpacingInput","fontSizeInput","parkX","parkY"}) ((TextBox)Get(f,n)).Text=n=="writingInputText"?"你好\r\n123":"invalid-"+n;
   var fields=(Dictionary<string,TextBox>)Get(f,"overrides"); fields["draw"].Text="1000";fields["zfeed"].Text="";fields["travel"].Text="unfinished.";
   foreach(var pair in (Dictionary<string,TextBox>)Get(f,"pageFields")) pair.Value.Text="raw-"+pair.Key;
   ((CheckBox)Get(f,"pageEnabled")).Checked=false;((CheckBox)Get(f,"startAtOrigin")).Checked=true;((CheckBox)Get(f,"parkEnabled")).Checked=false;
   ((TabControl)Get(f,"setupTabs")).SelectedIndex=3;
   var p=Get(f,"profile");p.GetType().GetProperty("DisplayName").SetValue(p,"edited unsaved profile",null);
   Save(f);Assert(File.Exists(path),"saved on close");
  }
  using(var f=Open(t,path)){
   Assert(((TextBox)Get(f,"writingInputText")).Text=="你好\r\n123","multiline restored");
   foreach(string n in new[]{"letterSpacingInput","lineSpacingInput","fontSizeInput","parkX","parkY"}) Assert(((TextBox)Get(f,n)).Text=="invalid-"+n,"raw "+n);
   var fields=(Dictionary<string,TextBox>)Get(f,"overrides");Assert(fields["draw"].Text=="1000"&&fields["zfeed"].Text==""&&fields["travel"].Text=="unfinished.","overrides exact");
   foreach(var pair in (Dictionary<string,TextBox>)Get(f,"pageFields")) Assert(pair.Value.Text=="raw-"+pair.Key,"raw page");
   Assert(!((CheckBox)Get(f,"pageEnabled")).Checked&&((CheckBox)Get(f,"startAtOrigin")).Checked&&!((CheckBox)Get(f,"parkEnabled")).Checked,"checkboxes restored");
   Assert(((TabControl)Get(f,"setupTabs")).SelectedIndex==3,"tab restored");
   var p=Get(f,"profile");Assert((string)p.GetType().GetProperty("DisplayName").GetValue(p,null)=="edited unsaved profile","unsaved profile restored");
   Assert(((TextBox)Get(f,"writingText")).Text==""&&((TextBox)Get(f,"programPreview")).Text=="","no generated previews");Save(f);
  }
  var serviceType=a.GetType("GrblPlotter.AutoWriter.Services.MachineSetupStateService",true);
  var stateType=a.GetType("GrblPlotter.AutoWriter.Services.MachineSetupState",true);
  var service=Activator.CreateInstance(serviceType,new object[]{path});
  var loadArgs=new object[]{null}; var state=serviceType.GetMethod("Load").Invoke(service,loadArgs);
  string missingFont=Path.Combine(Path.GetDirectoryName(path),"missing-font.json");
  stateType.GetField("FontPath").SetValue(state,missingFont);serviceType.GetMethod("Save").Invoke(service,new object[]{state});
  using(var f=Open(t,path)){
   Assert(((ComboBox)Get(f,"fontSelector")).SelectedIndex>0,"missing font not silently substituted");
   Assert(((Label)Get(f,"status")).Text.Contains("字体缺失"),"missing font visible");
  }
  File.WriteAllText(path,"broken json");
  using(var f=Open(t,path)){Assert(((TextBox)Get(f,"writingInputText")).Text=="你好\r\n123","backup recovery");Assert(((Label)Get(f,"status")).Text.Contains("备份"),"recovery visible");}
  File.WriteAllText(path+".bak","broken backup");
  using(var f=Open(t,path)){Assert(((TextBox)Get(f,"writingInputText")).Text=="","failed restore default");Assert(((Label)Get(f,"status")).Text.Contains("无法读取"),"failed restore visible");}
  Console.WriteLine("PASS: "+checks+" last window state checks");return 0;
 }
}

