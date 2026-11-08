using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Models;
using GrblPlotter.AutoWriter.Services;
class PaperEditorTests
{
    static int checks;
    static BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool b,string n)
    {
        if(!b)throw new Exception(n);
        checks++;
    }
    static object Get(object o,string n)
    {
        return o.GetType().GetField(n,f).GetValue(o);
    }
    static object Call(object o,string n,params object[] a)
    {
        return o.GetType().GetMethod(n,f).Invoke(o,a);
    }
    static void Drag(Form form,double x,double y,double X,double Y)
    {
        var p=(PointF)Call(form,"Pixel",x,y);
        var q=(PointF)Call(form,"Pixel",X,Y);
        Call(form,"Down",null,new MouseEventArgs(MouseButtons.Left,1,(int)p.X,(int)p.Y,0));
        Call(form,"Move",null,new MouseEventArgs(MouseButtons.Left,0,(int)q.X,(int)q.Y,0));
        Call(form,"Up",null,new MouseEventArgs(MouseButtons.Left,1,(int)q.X,(int)q.Y,0));
        Application.DoEvents();
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            Console.WriteLine("PASS "+checks+" paper editor checks");
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
        var sheet=new PageLayoutOptions
        {
            WidthMm=100,HeightMm=100,LinesPerPage=2,FontHeightPercent=20,AutoWrap=true
        }
        ;
        var pages=TextLayoutService.LayoutPages("工\n王\n一",BuiltinGlyphLibrary.Resolve,1,1,12,1,sheet);
        var type=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.PaperEditor");
        var original=new List<PageDrawing>();
        Func<string,List<double[]>> text=s=>new List<double[]>
        {
            new double[]
            {
                0,0,5,0,5,5
            }
        }
        ;
        using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]
        {
            pages,sheet,original,text
        }
        ,null))
        {
            form.Show();
            Application.DoEvents();
            var tools=(ComboBox)Get(form,"tools");
            tools.SelectedItem="矩形";
            Drag(form,10,20,40,45);
            var items=(List<PageDrawing>)type.GetField("Result",f).GetValue(form);
            Check(items.Count==1&&items[0].Paths[0].Length==10,"rectangle path");
            Check(original.Count==0,"editor working copy isolates cancellation");
            tools.SelectedItem="选择";
            Drag(form,12,22,22,32);
            Check(items[0].Bounds().Left>19,"move object in millimetres");
            var b=items[0].Bounds();
            Drag(form,b.Right,b.Bottom,b.Right+8,b.Bottom+8);
            Check(items[0].Bounds().Width>35,"resize selection handle");
            ((NumericUpDown)Get(form,"page")).Value=2;
            tools.SelectedItem="箭头";
            Drag(form,20,20,45,35);
            Check(items.Count==2&&items[1].Page==2&&items[1].Paths.Count==2,"arrow and page binding");
            var delete=form.Controls.OfType<FlowLayoutPanel>().First(p=>p.Controls.OfType<Button>().Any(b=>(b.Name=="删除"||b.Text=="删除"))).Controls.OfType<Button>().Single(a=>(a.Name=="删除"||a.Text=="删除"));
            delete.PerformClick();
            Check(items.Count==1,"delete object");
            var toolbar=form.Controls.OfType<FlowLayoutPanel>().First(p=>p.Controls.OfType<Button>().Any(b=>(b.Name=="删除"||b.Text=="删除")));
            toolbar.Controls.OfType<Button>().Single(a=>(a.Name=="撤销"||a.Text=="撤销")).PerformClick();
            items=(List<PageDrawing>)type.GetField("Result",f).GetValue(form);
            Check(items.Count==2,"undo deletion");
            toolbar.Controls.OfType<Button>().Single(a=>(a.Name=="重做"||a.Text=="重做")).PerformClick();
            items=(List<PageDrawing>)type.GetField("Result",f).GetValue(form);
            Check(items.Count==1,"redo deletion");
            ((NumericUpDown)Get(form,"page")).Value=1;
            tools.SelectedItem="画笔";
            Drag(form,10,60,30,70);
            Check(items.Count==2&&items[1].Paths[0].Length>=4,"freehand stroke");
            tools.SelectedItem="圆";
            Drag(form,50,50,70,70);
            Check(items.Last().Paths[0].Length==130,"ellipse closed polyline");
            tools.SelectedItem="直线";
            Drag(form,10,80,30,80);
            Check(items.Last().Paths[0].Length==4,"line object");
            type.GetField("RequestText",f).SetValue(form,new Func<string,string>(old=>string.IsNullOrEmpty(old)?"工王":"王工"));
            tools.SelectedItem="文字批注";
            Drag(form,50,20,50,20);
            Check(items.Last().Text=="工王","text annotation insertion");
            string annotationId=items.Last().Id;
            toolbar.Controls.OfType<Button>().Single(button=>(button.Name=="修改批注"||button.Text=="修改批注")).PerformClick();
            Check(items.Last().Text=="王工" && items.Last().Id==annotationId,"annotation editing preserves object identity");
            items.Add(new PageDrawing
            {
                Page=1,Text="工",Paths=new List<double[]>
                {
                    new double[]
                    {
                        50,20,55,20,55,25
                    }
                }
            }
            );
            var before=pages[0].Strokes.Count;
            PageDrawing.Apply(pages,items,sheet);
            Check(pages[0].Strokes.Count>before,"drawings merge into output paths");
            Check(pages[0].Strokes.Last().Points[0].Y<0,"paper down maps to negative output Y");
            var profile=new MachineProfile
            {
                ProfileId="test",DisplayName="test",SchemaVersion=1,PenControlMode="Z",PenUpValue=0,PenDownValue=5,Speeds=new MachineSpeeds
                {
                    DrawingMmPerMin=1000,TravelMmPerMin=1800,ZMmPerMin=10000
                }
            }
            ;
            string code=WritingProgramService.Generate(profile,pages[0].Strokes,new WritingProgramOptions
            {
                RequireVerifiedRun=false
            }
            );
            Check(code.Contains("G1")&&code.Contains("Z5"),"drawing G-code generation");
            Check(pages[0].SourceText.Contains("工"), "annotation source text participates in PDF text layer");
            string drawingPdf=Path.Combine(folder,"with-drawings.pdf");
            WritingPdfService.Export(drawingPdf,pages,sheet);
            var without=TextLayoutService.LayoutPages("工\n王\n一",BuiltinGlyphLibrary.Resolve,1,1,12,1,sheet);
            string plainPdf=Path.Combine(folder,"without-drawings.pdf");
            WritingPdfService.Export(plainPdf,without,sheet);
            using(var a=UglyToad.PdfPig.PdfDocument.Open(drawingPdf))             using(var bpdf=UglyToad.PdfPig.PdfDocument.Open(plainPdf))                 Check(a.GetPage(1).Paths.Count>bpdf.GetPage(1).Paths.Count,"deleted drawings absent from regenerated PDF paths");
            string plainCode=WritingProgramService.Generate(profile,without[0].Strokes,new WritingProgramOptions
            {
                RequireVerifiedRun=false
            }
            );
            Check(code.Split('\n').Count(line=>line.Contains("Z5"))>plainCode.Split('\n').Count(line=>line.Contains("Z5")),"deleted drawings absent from regenerated G-code pen strokes");
            bool rejected=false;
            try
            {
                PageDrawing.Apply(new List<WritingLayoutPage>(),items,sheet);
            }
            catch
            {
                rejected=true;
            }
            Check(rejected,"missing page drawings cannot disappear silently");
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {
                form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(Path.Combine(folder,"paper-editor.png"));
            }
            var setupType=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm");
            using(var setup=(Form)Activator.CreateInstance(setupType,f,null,new object[]
            {
                null,Path.Combine(folder,"editor-state.json")
            }
            ,null))
            {
                setup.Show();
                Application.DoEvents();
                setupType.GetField("drawings",f).SetValue(setup,items);
                var project=(WritingProject)Call(setup,"CaptureProject");
                Check(project.Version==5,"styled annotation project gets version 5");
                string file=Path.Combine(folder,"drawing.awproject");
                WritingProjectService.Save(file,project);
                var loaded=WritingProjectService.Load(file);
                Check(loaded.Drawings.Count==items.Count&&loaded.Drawings[0].Paths[0].SequenceEqual(items[0].Paths[0]),"project drawing coordinates round trip");
                Call(setup,"SaveLastState");
                setupType.GetField("drawings",f).SetValue(setup,new List<PageDrawing>());
                Call(setup,"RestoreLastState");
                Check(((List<PageDrawing>)setupType.GetField("drawings",f).GetValue(setup)).Count==items.Count,"last-window state preserves drawings");
            }
        }
    }
}
