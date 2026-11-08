using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Services;
class SceneTests
{
    static int count;static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
    static object Get(object o,string name){return o.GetType().GetField(name,flags).GetValue(o);}
    static void Set(object o,string name,object value){o.GetType().GetField(name,flags).SetValue(o,value);}
    static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,flags).Invoke(o,args);}
    static List<PageDrawing> Items(object o){return (List<PageDrawing>)Get(o,"Result");}
    static string Text(object o){return (string)o.GetType().GetProperty("BodyText",flags).GetValue(o,null);}
    static PageLayoutOptions Sheet(List<PageDrawing> items,string text){return new PageLayoutOptions{WidthMm=120,HeightMm=140,LeftMm=10,TopMm=10,LinesPerPage=6,FontHeightPercent=50,AutoWrap=true,BlankGlyphs=PaperScene.Slots(items,DrawingAnchors.Canonical(text))};}
    static IList<WritingLayoutPage> Layout(string text,List<PageDrawing> items){return TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,1,1,12,1,Sheet(items,text));}
    static PageDrawing Draw(float x,float y){return new PageDrawing{Page=1,Paths=new List<double[]>{new[]{(double)x,y,x+10,y+10}}};}
    static Point At(object o,double x,double y){return Point.Round((PointF)Call(o,"Pixel",x,y));}
    static void Mouse(object o,string method,Point p,int clicks=1){Call(o,method,null,new MouseEventArgs(MouseButtons.Left,clicks,p.X,p.Y,0));}
    static void Pick(object o,int index){Set(o,"selected",index);var selection=(HashSet<int>)Get(o,"selection");selection.Clear();selection.Add(index);}
    [STAThread]static int Main(string[] args)
    {
        try{Run(args[0]);Console.WriteLine("PASS "+count+" scene management checks");return 0;}
        catch(Exception e){while(e.InnerException!=null)e=e.InnerException;Console.Error.WriteLine(e);return 1;}
    }
    static void Run(string folder)
    {
        Directory.CreateDirectory(folder);var type=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.PaperEditor");string source="工王\n王工";
        Func<string,List<double[]>> glyph=s=>new List<double[]>{new double[]{0,0,8,0,8,10}};
        using(var form=(Form)Activator.CreateInstance(type,flags,null,new object[]{Layout(source,new List<PageDrawing>()),Sheet(new List<PageDrawing>(),source),new List<PageDrawing>(),glyph},null))
        {
            type.GetProperty("BodyText",flags).SetValue(form,source,null);Set(form,"ReflowText",(Func<string,List<PageDrawing>,IList<WritingLayoutPage>>)Layout);
            form.Show();Application.DoEvents();var tool=(ComboBox)Get(form,"tools");var canvas=(Control)Get(form,"canvas");var input=(TextBox)Get(form,"inlineInput");
            var p=At(form,12,12);Mouse(form,"Down",p,1);Mouse(form,"Up",p);Mouse(form,"Down",p,2);Call(form,"SceneDoubleClick",new MouseEventArgs(MouseButtons.Left,2,p.X,p.Y,0));Mouse(form,"Up",p,2);
            Check(!((Panel)Get(form,"inlinePane")).Visible,"direct double click must not edit");
            Check(((HashSet<int>)Get(form,"bodySelection")).Contains(0),"single click selects exact body glyph");
            Mouse(form,"Down",p);Mouse(form,"Up",p);Mouse(form,"Down",p,2);Call(form,"SceneDoubleClick",new MouseEventArgs(MouseButtons.Left,2,p.X,p.Y,0));Mouse(form,"Up",p,2);
            Check(((Panel)Get(form,"inlinePane")).Visible&&input.Text=="工","second double click edits selected glyph only");
            input.Text="王";Call(form,"CommitInline");Check(DrawingAnchors.Canonical(Text(form))=="王王\n王工","editing selected glyph keeps other source characters");
            Mouse(form,"Down",p);Mouse(form,"Up",p);Mouse(form,"Down",p);var moved=At(form,42,52);Mouse(form,"Move",moved);Mouse(form,"Up",moved);
            var result=Items(form);Check(DrawingAnchors.Canonical(Text(form))==" 王\n王工","moving body leaves a real source space");Check(result.Count(d=>d.Kind=="Slot")==1&&result.Any(d=>d.Text=="王"&&d.Kind==null),"body move makes independent text plus retained slot");
            var baseline=Layout("王王\n王工",new List<PageDrawing>());var after=Layout(Text(form),result);Check(Math.Abs(after[0].Anchors[1].X-baseline[0].Anchors[1].X)<1e-6,"placeholder preserves next glyph x position");
            var detached=result.First(d=>d.Kind==null);Check(detached.Bounds().Left>35&&detached.Bounds().Top>45,"detached glyph follows drag");
            int independentIndex=result.IndexOf(detached);Pick(form,independentIndex);Call(form,"DeleteScene");Check(Items(form).All(d=>d.Kind!="Group"&&d.Kind!=null),"delete detached glyph leaves its empty slot");Check(Layout(Text(form),Items(form))[0].Anchors[1].X==after[0].Anchors[1].X,"deleting detached object retains original width");
            var undo=Get(form,"undo");var redo=Get(form,"redo");Call(form,"History",undo,redo);Check(Items(form).Any(d=>d.Kind==null),"undo restores independent text");
            Call(form,"CreateContainer","Layer");result=Items(form);var layer=result.Last();var first=Draw(20,80);var second=Draw(40,80);first.LayerId=second.LayerId=layer.Id;result.Add(first);result.Add(second);Pick(form,result.IndexOf(first));((HashSet<int>)Get(form,"selection")).Add(result.IndexOf(second));Call(form,"GroupSelected");
            var group=result.Last();Check(group.Kind=="Group"&&first.GroupId==group.Id&&second.GroupId==group.Id,"persistent group contains selected objects");
            group.Locked=true;Check(!PaperScene.Editable(first,result)&&!PaperScene.Editable(second,result),"group lock inherited by every member");int before=result.Count;Pick(form,result.IndexOf(group));Call(form,"DeleteScene");Check(Items(form).Count==before,"locked group cannot be deleted");group.Locked=false;group.Hidden=true;Check(!PaperScene.Visible(first,result),"group visibility inherited");
            var output=Layout(Text(form),result);int bodyStrokes=output[0].Strokes.Count;PageDrawing.Apply(output,result,Sheet(result,Text(form)));Check(output[0].Strokes.Count==bodyStrokes+Items(form).First(d=>d.Text=="王"&&d.Kind==null).Paths.Count,"hidden group absent from output");group.Hidden=false;
            layer.Locked=true;Check(PaperScene.Editable(group,result)&&!PaperScene.Editable(first,result),"independent group keeps member layer lock");layer.Locked=false;layer.Hidden=true;Check(!PaperScene.Visible(first,result),"hidden layer hides nested group");layer.Hidden=false;
            Pick(form,result.IndexOf(group));Call(form,"UngroupSelected");Check(result.All(d=>d.Kind!="Group")&&first.GroupId==null&&second.GroupId==null,"ungroup preserves members");
            Pick(form,result.IndexOf(first));Call(form,"ReorderScene",1);Check(result.IndexOf(first)>result.IndexOf(second),"raise object changes stacking and output order");
            var section=Draw(10,70);section.Kind="Section";section.Name="测试分区";section.Paths=new List<double[]>{new double[]{10,70,80,70,80,115,10,115,10,70}};result.Add(section);first.SectionId=section.Id;PageDrawing.Validate(result,Sheet(result,Text(form)));Check(true,"contained section accepts object");
            var bad=first.Clone();bad.Paths=new List<double[]>{new double[]{90,80,100,90}};bool rejected=false;try{PageDrawing.Validate(result.Select(d=>d.Id==first.Id?bad:d),Sheet(result,Text(form)));}catch(ArgumentException){rejected=true;}Check(rejected,"section rejects object outside bounds");
            Pick(form,result.IndexOf(section));Call(form,"EnterSection");Check((string)Get(form,"activeSection")==section.Id,"enter section changes editing scope");Check(!(bool)Call(form,"InActiveSection",new PointF(100,100)),"section blocks edits outside bounds");
            var newObject=Draw(30,90);Call(form,"AttachNewObject",newObject);Check(newObject.SectionId==section.Id&&newObject.LayerId==layer.Id,"new section objects inherit active section and layer");
            section.Hidden=true;Check(!PaperScene.Visible(first,result),"hidden section hides its member");section.Hidden=false;section.Locked=true;Check(!PaperScene.Editable(first,result),"locked section locks its member");section.Locked=false;
            var cloned=first.Clone();Check(cloned.SectionId==first.SectionId&&cloned.LayerId==first.LayerId,"scene metadata deep clone retains container identity");
            var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<PageDrawing>));using(var stream=new MemoryStream()){serializer.WriteObject(stream,result);stream.Position=0;var restored=(List<PageDrawing>)serializer.ReadObject(stream);PageDrawing.Validate(restored,Sheet(restored,Text(form)));Check(restored.Any(d=>d.Kind=="Section")&&restored.Any(d=>d.Kind=="Slot")&&restored.First(d=>d.Id==first.Id).SectionId==section.Id,"scene save and reload retains section and placeholder");}
            var shifted=DrawingAnchors.Remap(Text(form),"工"+Text(form),result);Check(shifted.First(d=>d.Kind=="Slot").SourceStart==1,"source insertion remaps retained blank slot");
            var project=new WritingProject{Version=6,Text=Text(form),AnchorSourceText=Text(form),Drawings=result,FontPath="",FontSha256="",PageEnabled=true,Page=Sheet(result,Text(form)),FontSizeMm=10};
            string projectPath=Path.Combine(folder,"scene.awproject");WritingProjectService.Save(projectPath,project);var loaded=WritingProjectService.Load(projectPath);Check(loaded.Version==6&&loaded.Drawings.Any(d=>d.Kind=="Slot"),"actual awproject v6 saves and reloads scene");
            var bodyOwner=(PageDrawing)Call(form,"EnsureBodyOwner");bodyOwner.Hidden=true;var bodyHidden=Layout(Text(form),result);int visibleObjects=result.Where(d=>!PaperScene.IsGuide(d)&&PaperScene.Visible(d,result)).Sum(d=>d.Paths.Count);PageDrawing.Apply(bodyHidden,result,Sheet(result,Text(form)));Check(bodyHidden[0].Strokes.Count==visibleObjects,"hidden body omitted from actual output");bodyOwner.Hidden=false;bodyOwner.Locked=true;Check(!(bool)type.GetProperty("BodyUnlocked",flags).GetValue(form,null),"source body lock disables inline editing");bodyOwner.Locked=false;
            var allSlot=Draw(0,0);allSlot.Kind="Slot";allSlot.SourceStart=0;allSlot.Text="工王";var allBlank=Layout("  ",new List<PageDrawing>{allSlot});Check(allBlank[0].Strokes.Count==0&&allBlank[0].Anchors.Count==2,"detaching all source text keeps a valid blank body page");
            Call(form,"RefreshScene");Check(((TreeView)Get(form,"objectTree")).Nodes.Count>0,"object panel populated");Check(((ListBox)Get(form,"historyList")).Items.Count>1,"history shows actions");
            int current=((System.Collections.ICollection)Get(form,"undo")).Count;Call(form,"JumpHistory",current-1);Check(((System.Collections.ICollection)Get(form,"redo")).Count>0,"history jump creates redo step");Call(form,"JumpHistory",current);Check(((System.Collections.ICollection)Get(form,"redo")).Count==0,"history jump forward restores current state");
            Set(form,"activeSection",null);Call(form,"RefreshScene");using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(folder,"paper-scene.png"));}
            var mixed=Layout("工王一",new List<PageDrawing>());Items(form).Clear();type.GetProperty("BodyText",flags).SetValue(form,"工王一",null);Set(form,"pages",mixed);Set(form,"selected",-1);((HashSet<int>)Get(form,"selection")).Clear();var bodyPick=(HashSet<int>)Get(form,"bodySelection");bodyPick.Clear();bodyPick.UnionWith(new[]{0,1,2});var exact=mixed[0].Anchors.SelectMany(a=>a.Paths).SelectMany(path=>path).ToArray();Call(form,"MergeTextObjects");Check(Items(form).Last().Paths.SelectMany(path=>path).SequenceEqual(exact),"merging source glyphs preserves every original coordinate including short horizontal glyph");var letters=Items(form);var a=Draw(20,30);a.Text="工";var b=Draw(35,30);b.Text="王";letters.Add(a);letters.Add(b);Pick(form,letters.IndexOf(a));((HashSet<int>)Get(form,"selection")).Add(letters.IndexOf(b));Call(form,"MergeTextObjects");Check(Items(form).Last().Text=="工王"&&Items(form).Last().TextParts.Count==2,"merge retains editable text and glyph parts");Call(form,"SplitTextObjects");Check(((HashSet<int>)Get(form,"selection")).Count==2,"merged text splits back to two glyphs");Call(form,"SplitTextObjects");Check(((HashSet<int>)Get(form,"selection")).All(i=>Items(form)[i].Text==null),"single glyphs split into pen paths");Check((bool)Get(form,"showRulers"),"xy rulers enabled by default");form.Close();
        }
    }
}
