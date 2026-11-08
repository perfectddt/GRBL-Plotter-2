using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Services;
class Stage2Tests
{
    static int checks;
    static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool b,string m)
    {
        if(!b)throw new Exception(m);
        checks++;
    }
    static object Get(object o,string name)
    {
        return o.GetType().GetField(name,flags).GetValue(o);
    }
    static object Call(object o,string name,params object[] a)
    {
        return o.GetType().GetMethod(name,flags).Invoke(o,a);
    }
    static void Event(Form f,string name,double x,double y)
    {
        var p=(PointF)Call(f,"Pixel",x,y);
        Call(f,name,null,new MouseEventArgs(MouseButtons.Left,1,(int)p.X,(int)p.Y,0));
    }
    static void Drag(Form f,double x,double y,double X,double Y)
    {
        Event(f,"Down",x,y);
        Event(f,"Move",X,Y);
        Event(f,"Up",X,Y);
        Application.DoEvents();
    }
    static List<PageDrawing> Items(Form f)
    {
        return (List<PageDrawing>)Get(f,"Result");
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            Console.WriteLine("PASS "+checks+" stage 2 checks");
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
        var cut=DrawingGeometry.Erase(new[]
        {
            new double[]
            {
                10,50,90,50
            }
        }
        ,new PointF(50,50),5);
        Check(cut.Count==2&&Math.Abs(cut[0].Last()-50)<1e-8&&Math.Abs(cut[0][2]-45)<1e-8&&Math.Abs(cut[1][0]-55)<1e-8,"eraser splits crossing segment at exact boundaries");
        Check(DrawingGeometry.Erase(new[]
        {
            new double[]
            {
                49,50,51,50
            }
        }
        ,new PointF(50,50),5).Count==0,"eraser removes completely enclosed line");
        Check(DrawingGeometry.Erase(new[]
        {
            new double[]
            {
                10,10,20,10
            }
        }
        ,new PointF(50,50),5).Count==1,"eraser leaves nonintersecting paths");
        var sheet=new PageLayoutOptions
        {
            WidthMm=100,HeightMm=100,LinesPerPage=5,FontHeightPercent=50,AutoWrap=true
        }
        ;
        string text=string.Join("\n",Enumerable.Repeat("工",6).ToArray());
        var plain=TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,0,0,12,1,sheet);
        Check(plain.Count==2&&plain[0].LineCount==5,"baseline pagination");
        sheet.ReservedAreas=new List<PageReservedArea>
        {
            new PageReservedArea
            {
                Page=1,TopMm=20,HeightMm=40
            }
        }
        ;
        var reserved=TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,0,0,12,1,sheet);
        Check(reserved.Count==2&&reserved[0].LineCount==3&&reserved[1].LineCount==3,"reserved band moves text and repaginates");
        Check(!reserved[0].Strokes.SelectMany(s=>s.Points).Any(p=>-p.Y>20+1e-8&&-p.Y<60-1e-8),"no text paths inside reserved band");
        var longer=TextLayoutService.LayoutPages(text+"\n工\n工",BuiltinGlyphLibrary.Resolve,0,0,12,1,sheet);
        Check(longer.Sum(p=>p.LineCount)==8,"changed text reflows without loss");
        sheet.ReservedAreas.Add(new PageReservedArea
        {
            Page=1,TopMm=35,HeightMm=30
        }
        );
        Check(TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,0,0,12,1,sheet).Sum(p=>p.LineCount)==6,"overlapping reserve areas terminate and preserve text");
        var reserve=new PageDrawing
        {
            Page=1,Kind="Reserve",Paths=new List<double[]>
            {
                new double[]
                {
                    0,20,100,20,100,60,0,60,0,20
                }
            }
        }
        ;
        int strokes=reserved[0].Strokes.Count;
        PageDrawing.Apply(reserved,new[]
        {
            reserve
        }
        ,sheet);
        Check(reserved[0].Strokes.Count==strokes,"reserved boundary not sent to machine");
        var editorType=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.PaperEditor");
        Func<string,List<double[]>> drawText=s=>new List<double[]>
        {
            new double[]
            {
                0,0,5,5
            }
        }
        ;
        using(var form=(Form)Activator.CreateInstance(editorType,flags,null,new object[]
        {
            plain,sheet,new List<PageDrawing>(),drawText
        }
        ,null))
        {
            form.Show();
            Application.DoEvents();
            var tools=(ComboBox)Get(form,"tools");
            tools.SelectedItem="直线";
            Drag(form,10,50,90,50);
            tools.SelectedItem="橡皮擦";
            Drag(form,50,40,50,60);
            Check(Items(form).Count==1&&Items(form)[0].Paths.Count==2,"dragged eraser clips actual editor paths");
            var bar=form.Controls.OfType<FlowLayoutPanel>().First(p=>p.Controls.OfType<Button>().Any(b=>(b.Name=="撤销"||b.Text=="撤销")));
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="撤销"||b.Text=="撤销")).PerformClick();
            Check(Items(form)[0].Paths.Count==1,"eraser one gesture undo");
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="重做"||b.Text=="重做")).PerformClick();
            Check(Items(form)[0].Paths.Count==2,"eraser redo");
            tools.SelectedItem="矩形";
            Drag(form,20,20,30,30);
            tools.SelectedItem="套索";
            Event(form,"Down",5,5);
            Event(form,"Move",95,5);
            Event(form,"Move",95,80);
            Event(form,"Move",5,80);
            Event(form,"Move",5,5);
            Event(form,"Up",5,5);
            Check(((HashSet<int>)Get(form,"selection")).Count==2,"lasso selects completely enclosed objects");
            tools.SelectedItem="选择";
            double old=Items(form)[1].Bounds().Left;
            Drag(form,25,25,30,30);
            Check(Items(form)[1].Bounds().Left>old+4,"lasso group moves together");
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="删除"||b.Text=="删除")).PerformClick();
            Check(Items(form).Count==0,"lasso batch delete");
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="撤销"||b.Text=="撤销")).PerformClick();
            Check(Items(form).Count==2,"batch delete undo restores objects");
            tools.SelectedItem="预留绘图区";
            Drag(form,20,35,70,65);
            Check(Items(form).Last().Kind=="Reserve"&&Math.Abs(Items(form).Last().Bounds().Width-100)<.01,"reserve tool creates full-width band");
            editorType.GetField("Reflow",flags).SetValue(form,new Func<List<PageDrawing>,IList<WritingLayoutPage>>(items=>
            {
                sheet.ReservedAreas=items.Where(d=>d.Kind=="Reserve").Select(d=>new PageReservedArea
                {
                    Page=d.Page,TopMm=d.Bounds().Top,HeightMm=d.Bounds().Height
                }
                ).ToList();return TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,0,0,12,1,sheet);
            }
            ));
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="更新正文排版"||b.Text=="更新正文排版")).PerformClick();
            Check(((IList<WritingLayoutPage>)Get(form,"pages"))[0].LineCount<5,"editor updates body after reserve insertion");
            using(var image=new Bitmap(form.Width,form.Height))
            {
                form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                image.Save(Path.Combine(folder,"stage2-editor.png"));
            }
            var setupType=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm");
            using(var setup=(Form)Activator.CreateInstance(setupType,flags,null,new object[]
            {
                null,Path.Combine(folder,Guid.NewGuid().ToString("N")+".json")
            }
            ,null))
            {
                setup.Show();
                Application.DoEvents();
                setupType.GetField("drawings",flags).SetValue(setup,Items(form));
                var project=(WritingProject)Call(setup,"CaptureProject");
                Check(project.Version==3,"reserved-area projects use version 3");
                string file=Path.Combine(folder,"reserved.awproject");
                WritingProjectService.Save(file,project);
                var reopened=WritingProjectService.Load(file);
                Check(reopened.Drawings.Any(d=>d.Kind=="Reserve")&&reopened.Drawings.Any(d=>d.Paths.Count==2),"reserve and erased paths persist");
            }
        }
    }
}
