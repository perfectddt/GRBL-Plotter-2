using System;
using System.IO;
using System.Drawing;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
class RegionIntegrationTests {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(object f,string n){return f.GetType().GetField(n,flags).GetValue(f);}
 static object Call(object f,string n){return f.GetType().GetMethod(n,flags).Invoke(f,null);}
 static int checks;
 static void Check(bool b,string n){if(!b)throw new Exception(n);checks++;}
 static void Set(object o,string n,object v){o.GetType().GetProperty(n).SetValue(o,v,null);}
 static Form Open(Type t,string state){return (Form)Activator.CreateInstance(t,flags,null,new object[]{null,state},null);}
 static Button Find(Control c,string text){if(c is Button && c.Text==text)return (Button)c;foreach(Control child in c.Controls){var b=Find(child,text);if(b!=null)return b;}return null;}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception e){while(e.InnerException!=null)e=e.InnerException;Console.WriteLine(e.GetType().Name+": "+e.Message);return 1;}}
 static void Run(string[] args){
  var asm=Assembly.LoadFrom(args[0]);var t=asm.GetType("GrblPlotter.AutoWriter.MachineSetupForm",true);var rt=asm.GetType("GrblPlotter.AutoWriter.Services.WritingRegion",true);string state=args[1];
  using(var form=Open(t,state)){
   form.Show();((CheckBox)Get(form,"regionsEnabled")).Checked=true;
   Check(((CheckBox)Get(form,"pageEnabled")).Checked && !((CheckBox)Get(form,"startAtOrigin")).Checked,"region mode keeps page origin");
   Check(((NumericUpDown)Get(form,"pageSelector")).Enabled && ((NumericUpDown)Get(form,"regionPageSelector")).Enabled,"region pagination enabled");
   ((TabControl)Get(form,"setupTabs")).SelectedIndex=4;Application.DoEvents();
   var canvas=(Control)Get(form,"regionCanvas");var r=Activator.CreateInstance(rt);
   Set(r,"X",10d);Set(r,"Y",10d);Set(r,"Width",60d);Set(r,"Height",40d);Set(r,"Text","工\n王");Set(r,"FontSize",6d);Set(r,"LetterGap",1d);Set(r,"LineGap",2d);
   using(var timer=new Timer{Interval=40}){
    timer.Tick+=(s,e)=>{foreach(Form win in Application.OpenForms)if(win.GetType().Name=="RegionEditorForm"){timer.Stop();Find(win,"确定").PerformClick();break;}};
    timer.Start();((Delegate)Get(canvas,"RegionCreated")).DynamicInvoke(r);
   }
   Check(((IList)Get(form,"regions")).Count==1,"draw callback and properties accept creates region");
   var strokes=(IList)Call(form,"BuildWritingStrokes");Check(strokes.Count==7,"actual setup composes text only");Check(Get(canvas,"Strokes")!=null,"canvas displays composed strokes");
   var p=Call(form,"CaptureProject");Check((bool)p.GetType().GetProperty("RegionsEnabled").GetValue(p,null),"project captures mode");
   Check(((IList)p.GetType().GetProperty("Regions").GetValue(p,null)).Count==1,"project captures region data");
   ((TextBox)Get(form,"writingText")).Text="old";Call(form,"InvalidateWriting");Check(((TextBox)Get(form,"writingText")).Text=="" && Get(canvas,"Strokes")==null,"edit invalidates canvas and code");
   // Doubleclick invokes the production editor for the numbered region.
   using(var timer=new Timer{Interval=40}){
    timer.Tick+=(s,e)=>{foreach(Form win in Application.OpenForms)if(win.GetType().Name=="RegionEditorForm"){timer.Stop();((TextBox)Get(win,"text")).Text="一";Find(win,"确定").PerformClick();break;}};
    timer.Start();var pixel=(PointF)canvas.GetType().GetMethod("ToPixel",flags).Invoke(canvas,new object[]{20d,20d});
    canvas.GetType().GetMethod("OnMouseDoubleClick",flags).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Left,2,(int)pixel.X,(int)pixel.Y,0)});
   }
   Check(((IList)Call(form,"BuildWritingStrokes")).Count==1,"doubleclick edit updates strokes");
   var active=((IList)Get(form,"regions"))[0];Set(active,"Text","工\n王\n工");Set(active,"Height",7d);Set(active,"LineGap",0d);
   Check(((IList)Call(form,"BuildWritingStrokes")).Count==3,"current region page one only");
   Check(((IList)Get(form,"writingPages")).Count==3,"region UI tracks total");
   ((TextBox)Get(form,"writingText")).Text="old";((NumericUpDown)Get(form,"regionPageSelector")).Value=2;
   Check(((NumericUpDown)Get(form,"pageSelector")).Value==2 && Get(canvas,"Strokes")==null && ((TextBox)Get(form,"writingText")).Text=="","mirrored page switch invalidates strokes and code");
   Check(((IList)Call(form,"BuildWritingStrokes")).Count==4,"second page only");
   canvas.GetType().GetField("Selected",flags).SetValue(canvas,0);
   form.GetType().GetMethod("CopySelectedRegion",flags).Invoke(form,null);
   var copied=(IList)Get(form,"regions");Check(copied.Count==2,"copy adds one region");
   double copiedX=(double)rt.GetProperty("X").GetValue(copied[1],null);double sourceX=(double)rt.GetProperty("X").GetValue(copied[0],null);
   Check(copiedX>=sourceX+5-0.01,"copy steps down-right");Check((string)rt.GetProperty("Id").GetValue(copied[0],null)!=(string)rt.GetProperty("Id").GetValue(copied[1],null),"copy gets its own id");
   form.GetType().GetMethod("UndoRegions",flags).Invoke(form,null);Check(((IList)Get(form,"regions")).Count==1,"undo removes the copy and restores text");
   var live=((IList)Get(form,"regions"))[0];var snapshot=live.GetType().GetMethod("Clone").Invoke(live,null);
   var gesture=form.GetType().GetField("gestureBefore",flags);var snap=(IList)Activator.CreateInstance(gesture.FieldType);snap.Add(snapshot);
   form.GetType().GetField("gestureBefore",flags).SetValue(form,snap);
   rt.GetProperty("PageBehavior").SetValue(live,1,null);rt.GetProperty("Width").SetValue(live,4d,null);rt.GetProperty("Text").SetValue(live,"工工工工",null);
   form.GetType().GetMethod("AcceptRegionGesture",flags).Invoke(form,new object[]{true});
   var restored=((IList)Get(form,"regions"))[0];
   Check((double)rt.GetProperty("Width").GetValue(restored,null)>4,"shrink that cannot hold text restores the box");
   Check((string)rt.GetProperty("Text").GetValue(restored,null)!="工工工工","restored box keeps the previous text");
   form.Close();Check(File.Exists(state),"actual close saves state");
  }
  using(var form=Open(t,state)){
   Check(((CheckBox)Get(form,"regionsEnabled")).Checked && ((IList)Get(form,"regions")).Count==1,"reopen restores regions and mode");
   Check(((NumericUpDown)Get(form,"pageSelector")).Value==2 && ((IList)Call(form,"BuildWritingStrokes")).Count==4,"restored selected region page");
   ((CheckBox)Get(form,"regionsEnabled")).Checked=false;((TextBox)Get(form,"writingInputText")).Text="工王";
   Check(((IList)Call(form,"BuildWritingStrokes")).Count==7,"normal text mode preserved");
  }
  Console.WriteLine("PASS "+checks+" production region UI/create/doubleclick/generation/state checks");
 }
}

