using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
class RegionTests {
 static int checks;
 static void Check(bool b,string n){if(!b)throw new Exception(n);checks++;}
 static void Near(double a,double b,string n){Check(Math.Abs(a-b)<0.0001,n);}
 static void Throws(Action a,string n){try{a();}catch{checks++;return;}throw new Exception(n);}
 static void Mouse(RegionCanvas c,string method,Point p){MouseButton(c,method,p,MouseButtons.Left);}
 static void MouseButton(RegionCanvas c,string method,Point p,MouseButtons button){typeof(RegionCanvas).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{new MouseEventArgs(button,1,p.X,p.Y,0)});}
 static List<WritingRegion> Rs(WritingRegion a){return new List<WritingRegion>{a};}
 static WritingRegion R(){return new WritingRegion{X=10,Y=20,Width=40,Height=30,Left=2,Right=3,Top=4,Bottom=5,Text="工\n王",FontSize=6,LetterGap=1,LineGap=2};}
 [STAThread] static int Main(string[] args){try{Run(args);return 0;}catch(Exception error){Console.WriteLine(error.GetType().Name+": "+error.Message);return 1;}}
 static void Run(string[] args){
  var sheet=new PageLayoutOptions{WidthMm=210,HeightMm=297,LeftMm=10,RightMm=10,TopMm=10,BottomMm=10,AutoWrap=true};
  var r=R();var left=RegionLayoutService.Layout(Rs(r),sheet,BuiltinGlyphLibrary.Resolve);
  var local=TextLayoutService.LayoutPage(r.Text,BuiltinGlyphLibrary.Resolve,r.LetterGap,r.LineGap,12,.6,r.Options());
  Check(left.Count==7,"glyph count preserved, no rectangle strokes");
  for(int i=0;i<left.Count;i++)for(int j=0;j<left[i].Points.Count;j++){Near(left[i].Points[j].X,local[i].Points[j].X+10,"translated X");Near(left[i].Points[j].Y,local[i].Points[j].Y-20,"translated Y");}
  r.Alignment=1;var center=RegionLayoutService.Layout(Rs(r),sheet,BuiltinGlyphLibrary.Resolve);r.Alignment=2;var right=RegionLayoutService.Layout(Rs(r),sheet,BuiltinGlyphLibrary.Resolve);
  Check(center[0].Points[0].X>left[0].Points[0].X && right[0].Points[0].X>center[0].Points[0].X,"alignment per row");
  var second=R();second.X=60;second.Y=50;second.Text="一";second.FontSize=3;
  var both=RegionLayoutService.Layout(new List<WritingRegion>{r,second},sheet,BuiltinGlyphLibrary.Resolve);
  Check(both.Count==8,"independent texts composed creation order");var secondLocal=TextLayoutService.LayoutPage(second.Text,BuiltinGlyphLibrary.Resolve,1,2,12,.3,second.Options());Near(both[7].Points[0].X,60+secondLocal[0].Points[0].X,"second margins X");
  var profile=new MachineProfile{ProfileId="offline",DisplayName="offline",PenControlMode="Z",PenUpValue=0,PenDownValue=5,AxisMapping=new CoordinateMapping{WorkOrigin="G54",XDirection=1,YDirection=-1},UsableBoundsWorkMm=new CoordinateBounds{MinX=0,MaxX=210,MinY=0,MaxY=297,MinZ=-1,MaxZ=6},Speeds=new MachineSpeeds{DrawingMmPerMin=1000,TravelMmPerMin=1800,ZMmPerMin=300},Delays=new MachineDelays{PenUpMs=0,PenDownMs=0},Capabilities=new MachineCapabilities{RequiresContinuousPenMotion=true}};
  string code=WritingProgramService.Generate(profile,both,new WritingProgramOptions{RequireVerifiedRun=false});int draws=0,expectedDraws=0;
  foreach(var stroke in both)expectedDraws+=stroke.Points.Count-1;
  foreach(string line in code.Split('\n'))if(line.StartsWith("G1 X") && line.TrimEnd().EndsWith("F1000"))draws++;
  Check(draws==expectedDraws,"Gcode has only glyph draw moves, never region frame");
  var bad=R();bad.Height=7;Throws(()=>RegionLayoutService.Layout(new List<WritingRegion>{r,bad},sheet,BuiltinGlyphLibrary.Resolve),"overflow rejects whole generation");
  bad=R();bad.X=209;Throws(()=>bad.Validate(sheet),"outside physical sheet");bad=R();bad.Left=40;Throws(()=>bad.Validate(sheet),"margins cannot consume width");
  Check(RegionLayoutService.Overlaps(new List<WritingRegion>{r,r.Clone()}),"overlap warning");Check(!RegionLayoutService.Overlaps(new List<WritingRegion>{r,second}),"separate regions");
  var blank=R();blank.Text="";Check(RegionLayoutService.Layout(new List<WritingRegion>{blank,second},sheet,BuiltinGlyphLibrary.Resolve).Count==1,"empty placeholder skipped");Throws(()=>RegionLayoutService.Layout(Rs(blank),sheet,BuiltinGlyphLibrary.Resolve),"all empty no sample");
  var flow=R();flow.Text="工\n王\n工";flow.Height=7;flow.Top=flow.Bottom=0;flow.LineGap=0;
  var shortFlow=flow.Clone();shortFlow.X=70;shortFlow.Text="王";
  var repeat=shortFlow.Clone();repeat.X=120;repeat.Text="一";repeat.PageBehavior=1;
  var first=repeat.Clone();first.Y=70;first.PageBehavior=2;
  var last=repeat.Clone();last.Y=120;last.PageBehavior=3;
  var pages=RegionLayoutService.LayoutPages(new List<WritingRegion>{flow,shortFlow,repeat,first,last},sheet,BuiltinGlyphLibrary.Resolve);
  Check(pages.Count==3,"longest independent flow determines page count");
  Check(pages[0].Strokes.Count==9 && pages[1].Strokes.Count==5 && pages[2].Strokes.Count==5,"flow no duplication, repeat first last exact counts");
  for(int i=0;i<3;i++)Check(pages[i].PageNumber==i+1,"ordered page numbering");
  Check(pages[2].Strokes[4].Points[0].Y <= -120,"last-only on computed final page");
  var onlyFlow=RegionLayoutService.LayoutPages(Rs(flow),sheet,BuiltinGlyphLibrary.Resolve);
  int flowCount=0;foreach(var page in onlyFlow)flowCount+=page.Strokes.Count;Check(flowCount==10,"all input glyph strokes retained across pages");
  foreach(int mode in new[]{1,2,3}){var staticOverflow=flow.Clone();staticOverflow.PageBehavior=mode;Throws(()=>RegionLayoutService.LayoutPages(Rs(staticOverflow),sheet,BuiltinGlyphLibrary.Resolve),"static overflow rejected instead of paginated");}
  var invalidMode=flow.Clone();invalidMode.PageBehavior=4;Throws(()=>invalidMode.Validate(sheet),"bad behavior rejected");
  Check(RegionLayoutService.LayoutPages(new List<WritingRegion>{blank,repeat},sheet,BuiltinGlyphLibrary.Resolve).Count==1,"blank flow with repeat creates one page");
  var rows=flow.Clone();rows.Text="工工工工工工";rows.Width=13;
  var wrapped=RegionLayoutService.LayoutPages(Rs(rows),sheet,BuiltinGlyphLibrary.Resolve);int wrappedCount=0;foreach(var page in wrapped)wrappedCount+=page.Strokes.Count;Check(wrapped.Count==6 && wrappedCount==18,"automatic wraps preserve every glyph exactly once");
  var project=new WritingProject{Version=1,Text="工",FontPath="",FontSha256="",PageEnabled=true,Page=sheet,FontSizeMm=6,LetterSpacingMm=1,LineSpacingMm=0,RegionsEnabled=true,Regions=new List<WritingRegion>{r,last}};
  string path=Path.Combine(args[0],"regions.awproject");WritingProjectService.Save(path,project);var restored=WritingProjectService.Load(path);Check(restored.RegionsEnabled && restored.Regions.Count==2 && restored.Regions[1].FontSize==6 && restored.Regions[1].PageBehavior==3,"project round trip");
  project.RegionsEnabled=false;project.Regions=null;WritingProjectService.Save(path,project);
  string legacy=File.ReadAllText(path).Replace("\"RegionsEnabled\":false,","").Replace("\"Regions\":null,","");File.WriteAllText(path,legacy);
  restored=WritingProjectService.Load(path);Check(!restored.RegionsEnabled && restored.Regions==null,"older project omitted fields accepted");
  var stateService=new MachineSetupStateService(Path.Combine(args[0],"state.json"));var state=new MachineSetupState{Profile=new MachineProfile(),Regions=new List<WritingRegion>{r}};stateService.Save(state);string warning;Check(stateService.Load(out warning).Regions.Count==1,"state regions round trip");
  using(var host=new Form{Size=new Size(700,800)})using(var canvas=new RegionCanvas{Sheet=sheet,DrawEnabled=true}) {
   host.Controls.Add(canvas);host.Show();Application.DoEvents();
   foreach(var size in new[]{new Size(700,800),new Size(1000,500)}){host.Size=size;Application.DoEvents();var px=canvas.ToPixel(36.5,92);var mm=canvas.ToPage(px,false);Near(mm.X,36.5,"resize X conversion");Near(mm.Y,92,"resize Y conversion");}
   var clamp=canvas.ToPage(new PointF(-500,2000),true);Near(clamp.X,0,"clamp left");Near(clamp.Y,297,"clamp bottom");
   WritingRegion drawn=null;canvas.RegionCreated=v=>drawn=v;
   var a=Point.Round(canvas.ToPixel(15,30));var b=Point.Round(canvas.ToPixel(100,80));Mouse(canvas,"OnMouseDown",a);Mouse(canvas,"OnMouseMove",b);Mouse(canvas,"OnMouseUp",b);
   Check(drawn!=null && drawn.X>=0 && drawn.Y>=0 && drawn.X+drawn.Width<=210.001,"drag creates inside rectangle");
   drawn=null;Mouse(canvas,"OnMouseDown",new Point(0,0));Mouse(canvas,"OnMouseUp",b);Check(drawn==null,"outside page cannot create");
   var tiny=Point.Round(canvas.ToPixel(16,30));Mouse(canvas,"OnMouseDown",a);Mouse(canvas,"OnMouseUp",tiny);Check(drawn==null,"tiny drag ignored");
   canvas.Regions=Rs(r);var hit=Point.Round(canvas.ToPixel(20,25));Check(canvas.HitTest(hit)==0,"numbered hit test");int edit=-1;canvas.EditRequested=i=>edit=i;Mouse(canvas,"OnMouseDoubleClick",hit);Check(edit==0,"doubleclick edits selected");
   canvas.Zoom=2;canvas.Pan=new PointF(35,-20);
   foreach(var size in new[]{new Size(700,800),new Size(1100,640)}){host.Size=size;Application.DoEvents();var px=canvas.ToPixel(36.5,92);var mm=canvas.ToPage(px,false);Near(mm.X,36.5,"zoomed window X");Near(mm.Y,92,"zoomed window Y");}
   var anchor=Point.Round(canvas.ToPixel(40,50));var beforeMm=canvas.ToPage(anchor,false);
   typeof(RegionCanvas).GetMethod("OnMouseWheel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.None,0,anchor.X,anchor.Y,120)});
   var afterMm=canvas.ToPage(anchor,false);Near(afterMm.X,beforeMm.X,"wheel keeps cursor mm x");Near(afterMm.Y,beforeMm.Y,"wheel keeps cursor mm y");Check(canvas.Zoom>2,"wheel zooms without changing sheet");
   float panX=canvas.Pan.X,panY=canvas.Pan.Y;
   MouseButton(canvas,"OnMouseDown",new Point(80,90),MouseButtons.Right);MouseButton(canvas,"OnMouseMove",new Point(110,70),MouseButtons.Right);MouseButton(canvas,"OnMouseUp",new Point(110,70),MouseButtons.Right);
   Near(canvas.Pan.X,panX+30,"right drag pans x");Near(canvas.Pan.Y,panY-20,"right drag pans y");
   canvas.Zoom=1;canvas.Pan=PointF.Empty;host.Size=new Size(700,800);Application.DoEvents();
   var moving=R();double ox=moving.X,oy=moving.Y,ow=moving.Width;canvas.Regions=new List<WritingRegion>{moving};canvas.Selected=0;
   bool started=false,finishedChanged=false;canvas.GestureStarted=()=>started=true;canvas.GestureFinished=changed=>finishedChanged=changed;
   var from=Point.Round(canvas.ToPixel(ox+6,oy+6));var to=Point.Round(canvas.ToPixel(ox+16,oy+11));
   var grab=canvas.ToPage(from,false);var now=canvas.ToPage(to,false);
   Mouse(canvas,"OnMouseDown",from);Mouse(canvas,"OnMouseMove",to);Mouse(canvas,"OnMouseUp",to);
   Near(moving.X,ox+now.X-grab.X,"drag move mm x");Near(moving.Y,oy+now.Y-grab.Y,"drag move mm y");Check(started&&finishedChanged&&Math.Abs(moving.Width-ow)<0.0001,"move keeps size and reports gesture");
   canvas.Zoom=2.4f;canvas.Pan=new PointF(-15,22);host.Size=new Size(960,700);Application.DoEvents();
   ox=moving.X;oy=moving.Y;from=Point.Round(canvas.ToPixel(ox+3,oy+3));to=Point.Round(canvas.ToPixel(ox+14,oy+8));
   grab=canvas.ToPage(from,false);now=canvas.ToPage(to,false);
   Mouse(canvas,"OnMouseDown",from);Mouse(canvas,"OnMouseMove",to);Mouse(canvas,"OnMouseUp",to);
   Near(moving.X,Math.Max(0,Math.Min(sheet.WidthMm-moving.Width,ox+now.X-grab.X)),"zoomed resized window move x");
   Near(moving.Y,Math.Max(0,Math.Min(sheet.HeightMm-moving.Height,oy+now.Y-grab.Y)),"zoomed resized window move y");
   canvas.Zoom=1;canvas.Pan=PointF.Empty;host.Size=new Size(700,800);Application.DoEvents();
   moving.X=10;moving.Y=20;moving.Width=40;moving.Height=30;canvas.Selected=0;
   var corner=Point.Round(canvas.ToPixel(moving.X,moving.Y));var inward=Point.Round(canvas.ToPixel(18,26));var cornerMm=canvas.ToPage(inward,false);
   Mouse(canvas,"OnMouseDown",corner);Mouse(canvas,"OnMouseMove",inward);Mouse(canvas,"OnMouseUp",inward);
   Near(moving.X,cornerMm.X,"corner resize x");Near(moving.Y,cornerMm.Y,"corner resize y");Check(moving.Width<40&&moving.Height<30,"corner resize shrinks");
   canvas.SnapToGrid=true;moving.X=10.4;moving.Y=20.6;from=Point.Round(canvas.ToPixel(moving.X+5,moving.Y+5));to=new Point(from.X+12,from.Y+9);
   Mouse(canvas,"OnMouseDown",from);Mouse(canvas,"OnMouseMove",to);Mouse(canvas,"OnMouseUp",to);
   Check(Math.Abs(moving.X-Math.Round(moving.X))<0.001&&Math.Abs(moving.Y-Math.Round(moving.Y))<0.001,"optional snap lands on whole millimetres");
   canvas.SnapToGrid=false;canvas.Warnings=new[]{0};canvas.ShowGrid=false;canvas.Invalidate();
   var history=new RegionHistory();var edits=new List<WritingRegion>{R()};edits[0].Text="工";edits[0].Id="keep";history.Remember(edits);edits[0].Text="王";edits[0].X=44;
   var back=history.Undo(edits);Check(back[0].Text=="工"&&back[0].X==10&&back[0].Id=="keep","undo restores text and layout");
   var forward=history.Redo(back);Check(forward[0].Text=="王"&&forward[0].X==44,"redo restores text and layout");
   var missing=R();missing.Text="字";missing.PageBehavior=1;var twin=missing.Clone();
   string described=RegionLayoutService.Describe(new List<WritingRegion>{missing,twin},sheet,BuiltinGlyphLibrary.Resolve);
   Check(described.Contains("区域 1")&&described.Contains("字")&&described.Contains("重叠")&&described.Contains("红线"),"preview reports overlap, missing glyph and ink");
   var clipped=R();clipped.PageBehavior=1;clipped.Width=8;clipped.Text="工工";double keptWidth=clipped.Width;
   Throws(()=>RegionLayoutService.EnsureWritable(clipped,sheet,BuiltinGlyphLibrary.Resolve),"shrinking a static box refuses instead of clipping");
   Near(clipped.Width,keptWidth,"refused shrink does not change the box");
   var blankText=R();blankText.Text="  \n";RegionLayoutService.EnsureWritable(blankText,sheet,BuiltinGlyphLibrary.Resolve);checks++;
   using(var editor=new RegionEditorForm(r,sheet,1))using(var timer=new Timer{Interval=40}) {
    timer.Tick+=(s,e)=>{timer.Stop();foreach(Control c in editor.Controls)if(c is FlowLayoutPanel)foreach(Control btt in c.Controls)if(btt.Text=="确定")((Button)btt).PerformClick();};timer.Start();Check(editor.ShowDialog(host)==DialogResult.OK,"properties dialog accept lifecycle");Check(!object.ReferenceEquals(editor.Value,r),"properties clone avoids cancel mutation");
   }
   canvas.Strokes=left;using(var bmp=new Bitmap(canvas.Width,canvas.Height)){canvas.DrawToBitmap(bmp,canvas.ClientRectangle);bmp.Save(Path.Combine(args[0],"region-canvas.png"));}
   host.Close();
  }
  Console.WriteLine("PASS "+checks+" region geometry, alignment, overflow, persistence and canvas/editor checks");
 }
}




