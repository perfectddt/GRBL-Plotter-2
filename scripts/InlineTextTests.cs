using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Services;
class InlineTextTests
{
    static int n;
    static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool b,string s)
    {
        if(!b)throw new Exception(s);
        n++;
    }
    static object Get(object o,string name)
    {
        return o.GetType().GetField(name,f).GetValue(o);
    }
    static object Call(object o,string name,params object[] a)
    {
        return o.GetType().GetMethod(name,f).Invoke(o,a);
    }
    static IEnumerable<Control> All(Control c)
    {
        foreach(Control child in c.Controls)
        {
            yield return child;
            foreach(var x in All(child))yield return x;
        }
    }
    static IList<WritingLayoutPage> Layout(string s,List<PageDrawing> d)
    {
        return TextLayoutService.LayoutPages(s,BuiltinGlyphLibrary.Resolve,1,1,12,1,new PageLayoutOptions
        {
            WidthMm=100,HeightMm=120,LinesPerPage=6,FontHeightPercent=50,AutoWrap=true
        }
        );
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            Console.WriteLine("PASS "+n+" inline text and annotation checks");
            return 0;
        }
        catch(Exception e)
        {
            while(e.InnerException!=null)e=e.InnerException;
            Console.Error.WriteLine(e.Message+"\n"+e.StackTrace);
            return 1;
        }
    }
    static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var asm=typeof(PageDrawing).Assembly;
        var setupType=asm.GetType("GrblPlotter.AutoWriter.MachineSetupForm");
        using(var setup=(Form)Activator.CreateInstance(setupType,f,null,new object[]
        {
            null,Path.Combine(folder,Guid.NewGuid().ToString("N")+".json")
        }
        ,null))
        {
            setup.Show();
            Application.DoEvents();
            var type=asm.GetType("GrblPlotter.AutoWriter.PaperEditor");
            string original="工王\n王工";
            var pages=Layout(original,new List<PageDrawing>());
            Func<string,List<double[]>> fallback=s=>new List<double[]>
            {
                new double[]
                {
                    0,0,5,5
                }
            }
            ;
            using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]
            {
                pages,new PageLayoutOptions
                {
                    WidthMm=100,HeightMm=120
                }
                ,new List<PageDrawing>(),fallback
            }
            ,null))
            {
                type.GetProperty("BodyText",f).SetValue(form,original,null);
                type.GetField("ReflowText",f).SetValue(form,new Func<string,List<PageDrawing>,IList<WritingLayoutPage>>(Layout));
                type.GetField("StyledTextPaths",f).SetValue(form,new Func<string,AnnotationTextStyle,List<double[]>>((text,style)=>(List<double[]>)Call(setup,"BuildAnnotationPaths",text,style)));
                type.GetField("AnnotationFonts",f).SetValue(form,Call(setup,"AnnotationFontChoices"));
                form.Show();
                Application.DoEvents();
                var at=(PointF)Call(form,"Pixel",1.0,1.0);
                Call(form,"BeginInline",new Point((int)at.X,(int)at.Y));
                Check(((Panel)Get(form,"inlinePane")).Visible,"inline editor opens over existing body row");
                var input=(TextBox)Get(form,"inlineInput");
                Check(input.Text=="工王","inline editor loads source text of clicked row");
                input.Text="工工王";
                Check((bool)Call(form,"CommitInline"),"inline insertion commits and reflows");
                Check(DrawingAnchors.Canonical((string)type.GetProperty("BodyText",f).GetValue(form,null))=="工工王\n王工","insertion preserves other rows");
                at=(PointF)Call(form,"Pixel",1.0,1.0);
                Call(form,"BeginInline",new Point((int)at.X,(int)at.Y));
                input.Text="王";
                Call(form,"CommitInline");
                Check(DrawingAnchors.Canonical((string)type.GetProperty("BodyText",f).GetValue(form,null))=="王\n王工","inline deletion preserves remaining text");
                var bar=form.Controls.OfType<FlowLayoutPanel>().First(p=>p.Controls.OfType<Button>().Any(b=>(b.Name=="撤销"||b.Text=="撤销")));
                bar.Controls.OfType<Button>().Single(b=>(b.Name=="撤销"||b.Text=="撤销")).PerformClick();
                Check(DrawingAnchors.Canonical((string)type.GetProperty("BodyText",f).GetValue(form,null))=="工工王\n王工","inline edit participates in undo");
                at=(PointF)Call(form,"Pixel",1.0,1.0);
                Call(form,"BeginInline",new Point((int)at.X,(int)at.Y));
                input.Text="王王王";
                ((Panel)Get(form,"inlinePane")).Visible=false;
                Check(DrawingAnchors.Canonical((string)type.GetProperty("BodyText",f).GetValue(form,null))=="工工王\n王工","canceling inline edit leaves source unchanged");
                var tools=(ComboBox)Get(form,"tools");
                tools.SelectedItem="文字批注";
                bool dialogSeen=false;
                using(var timer=new Timer
                {
                    Interval=150
                }
                )
                {
                    timer.Tick+=(s,e)=>
                    {
                        var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Text=="文字批注 · 独立文字属性");
                        if(dialog==null)return;
                        timer.Stop();
                        dialogSeen=true;
                        ((TextBox)dialog.Controls.Find("annotationText",true)[0]).Text="工王";
                        ((NumericUpDown)dialog.Controls.Find("annotationSizeMm",true)[0]).Value=8;
                        ((NumericUpDown)dialog.Controls.Find("annotationLetterGapMm",true)[0]).Value=2;
                        ((NumericUpDown)dialog.Controls.Find("annotationLineGapMm",true)[0]).Value=3;
                        using(var bitmap=new Bitmap(dialog.Width,dialog.Height))
                        {
                            dialog.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                            bitmap.Save(Path.Combine(folder,"annotation-properties.png"));
                        }
                        All(dialog).OfType<Button>().Single(b=>b.Text=="确定").PerformClick();
                    }
                    ;
                    timer.Start();
                    at=(PointF)Call(form,"Pixel",30.0,60.0);
                    Call(form,"Down",null,new MouseEventArgs(MouseButtons.Left,1,(int)at.X,(int)at.Y,0));
                }
                Check(dialogSeen,"annotation property dialog available");
                var items=(List<PageDrawing>)Get(form,"Result");
                var annotation=items.Single();
                Check(annotation.TextStyle.SizeMm==8&&annotation.TextStyle.LetterGapMm==2&&annotation.TextStyle.LineGapMm==3,"independent annotation attributes captured");
                Check(Math.Abs(annotation.Bounds().Height-8)<1e-5,"annotation millimetre size changes actual generated geometry");
                var style=annotation.TextStyle.Clone();
                style.SizeMm=4;
                var small=(List<double[]>)Call(setup,"BuildAnnotationPaths","工王",style);
                Check(Math.Abs(new PageDrawing
                {
                    Page=1,Paths=small
                }
                .Bounds().Height-4)<1e-5,"style resizing changes pen paths");
                Check(annotation.TextStyle.SizeMm==8,"cloned style does not mutate existing annotation");
                setupType.GetField("drawings",f).SetValue(setup,items);
                var project=(WritingProject)Call(setup,"CaptureProject");
                Check(project.Version==5,"styled project format protected from older readers");
                string path=Path.Combine(folder,"styled.awproject");
                WritingProjectService.Save(path,project);
                var loaded=WritingProjectService.Load(path);
                Check(loaded.Drawings[0].TextStyle.SizeMm==8&&loaded.Drawings[0].Paths[0].SequenceEqual(annotation.Paths[0]),"annotation style and geometry save and reopen");
                DrawingAnchors.Bind(annotation,((IList<WritingLayoutPage>)Get(form,"pages"))[0].Anchors[0]);
                type.GetField("RequestText",f).SetValue(form,new Func<string,string>(old=>
                {
                    ((AnnotationTextStyle)Get(form,"pendingStyle")).SizeMm=12;return "王工";
                }
                ));
                Call(form,"EditText");
                var changedAnnotation=((List<PageDrawing>)Get(form,"Result")).Single();
                Check(changedAnnotation.HasAnchor&&changedAnnotation.TextStyle.SizeMm==12,"editing annotation keeps body anchor and applies new style");
                Check(Math.Abs(changedAnnotation.Bounds().Height-12)<1e-5,"annotation style edit regenerates existing object geometry");
                at=(PointF)Call(form,"Pixel",1.0,1.0);
                Call(form,"BeginInline",new Point((int)at.X,(int)at.Y));
                using(var bitmap=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                    bitmap.Save(Path.Combine(folder,"inline-text.png"));
                }
            }
        }
    }
}
