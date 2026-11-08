using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Services;
class Stage3Tests
{
    static int count;
    static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool b,string s)
    {
        if(!b)throw new Exception(s);
        count++;
    }
    static object Get(object o,string n)
    {
        return o.GetType().GetField(n,f).GetValue(o);
    }
    static object Call(object o,string n,params object[] a)
    {
        return o.GetType().GetMethod(n,f).Invoke(o,a);
    }
    static void E(Form form,string method,double x,double y)
    {
        var p=(PointF)Call(form,"Pixel",x,y);
        Call(form,method,null,new MouseEventArgs(MouseButtons.Left,1,(int)p.X,(int)p.Y,0));
    }
    static void Drag(Form form,double x,double y,double X,double Y)
    {
        E(form,"Down",x,y);
        E(form,"Move",X,Y);
        E(form,"Up",X,Y);
    }
    static PageLayoutOptions Sheet()
    {
        return new PageLayoutOptions
        {
            WidthMm=100,HeightMm=120,LinesPerPage=6,FontHeightPercent=50,AutoWrap=true
        }
        ;
    }
    static IList<WritingLayoutPage> Layout(string text,List<PageDrawing> items)
    {
        var s=Sheet();
        s.WrapAreas=items.Where(d=>d.Kind=="Wrap").Select(d=>
        {
            var b=d.Bounds();return new TextWrapArea
            {
                Page=d.Page,Left=b.Left,Top=b.Top,Width=b.Width,Height=b.Height
            }
            ;
        }
        ).ToList();
        return TextLayoutService.LayoutPages(text,BuiltinGlyphLibrary.Resolve,1,1,12,1,s);
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            Console.WriteLine("PASS "+count+" stage 3 checks");
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
        string body=string.Join("\n",Enumerable.Repeat("王",6).ToArray());
        var pages=Layout(body,new List<PageDrawing>());
        Check(pages[0].Anchors.Count==6,"source indices retained through text layout");
        var d=new PageDrawing
        {
            Page=1,Paths=new List<double[]>
            {
                new double[]
                {
                    20,105,30,105
                }
            }
        }
        ;
        DrawingAnchors.Bind(d,pages[0].Anchors.Last());
        string changed="工\n工\n"+body;
        var mapped=DrawingAnchors.Remap(body,changed,new[]
        {
            d
        }
        );
        var next=Layout(changed,mapped);
        var resolved=DrawingAnchors.Resolve(mapped,next);
        Check(resolved[0].Page==2&&Math.Abs(resolved[0].Bounds().Top-25)<.01,"annotation follows text across pages");
        Check(d.Page==1&&d.AnchorIndex==10,"anchor layout does not mutate source project");
        bool refused=false;
        try
        {
            DrawingAnchors.Remap(body,"工",new[]
            {
                d
            }
            );
        }
        catch
        {
            refused=true;
        }
        Check(refused,"deleted anchor cannot silently bind elsewhere");
        var wrap=new PageDrawing
        {
            Page=1,Kind="Wrap",Paths=new List<double[]>
            {
                new double[]
                {
                    0,0,40,0,40,60,0,60,0,0
                }
            }
        }
        ;
        string longText=string.Concat(Enumerable.Repeat("工王",35));
        var wrapped=Layout(longText,new List<PageDrawing>
        {
            wrap
        }
        );
        Check(wrapped.Sum(p=>p.Anchors.Count)==longText.Length,"wrapped text preserves all characters");
        Check(!wrapped[0].Strokes.SelectMany(s=>s.Points).Any(p=>p.X<41-1e-6&&-p.Y<61-1e-6),"text paths avoid wrapped illustration");
        Check(wrapped[0].Anchors.Any(a=>a.Y<60&&a.X>=41),"text fills available side of illustration");
        Check(wrapped[0].Anchors.Any(a=>a.Y>=80&&a.X<1),"text returns to full width below illustration");
        var type=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.PaperEditor");
        Func<string,List<double[]>> glyph=t=>new List<double[]>
        {
            new double[]
            {
                0,0,8,0,8,8
            }
        }
        ;
        using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]
        {
            pages,Sheet(),new List<PageDrawing>
            {
                d
            }
            ,glyph
        }
        ,null))
        {
            type.GetProperty("BodyText",f).SetValue(form,body,null);
            type.GetField("ReflowText",f).SetValue(form,new Func<string,List<PageDrawing>,IList<WritingLayoutPage>>(Layout));
            form.Show();
            Application.DoEvents();
            ((TextBox)Get(form,"bodyInput")).Text=changed;
            Check((bool)Call(form,"CommitBody"),"body can be edited inside paper editor");
            var current=(List<PageDrawing>)Get(form,"Result");
            Check(current[0].Page==2,"paper editor commits anchor position with changed body");
            var bar=form.Controls.OfType<FlowLayoutPanel>().First(p=>p.Controls.OfType<Button>().Any(b=>(b.Name=="撤销"||b.Text=="撤销")));
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="撤销"||b.Text=="撤销")).PerformClick();
            Check(DrawingAnchors.Canonical((string)type.GetProperty("BodyText",f).GetValue(form,null))==body&&((List<PageDrawing>)Get(form,"Result"))[0].Page==1,"undo restores text and anchor together");
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="重做"||b.Text=="重做")).PerformClick();
            Check(((List<PageDrawing>)Get(form,"Result"))[0].Page==2,"redo reapplies text and anchor together");
            ((NumericUpDown)Get(form,"page")).Value=1;
            var tools=(ComboBox)Get(form,"tools");
            tools.SelectedItem="矩形";
            Drag(form,10,10,25,25);
            Drag(form,40,10,55,25);
            tools.SelectedItem="套索";
            E(form,"Down",5,5);
            E(form,"Move",65,5);
            E(form,"Move",65,35);
            E(form,"Move",5,35);
            E(form,"Move",5,5);
            E(form,"Up",5,5);
            Check(tools.Text=="移动","lasso automatically activates explicit move tool");
            current=(List<PageDrawing>)Get(form,"Result");
            double old=current[1].Bounds().Top;
            Drag(form,32,20,32,30);
            Check(current[1].Bounds().Top>old+9&&current[2].Bounds().Top>old+9,"group can move by dragging empty space within its bounds");
            tools.SelectedItem="选择";
            Drag(form,12,22,12,22);
            tools.SelectedItem="编辑节点";
            var target=current[1].Paths[0];
            double oldX=target[2];
            Drag(form,target[2],target[3],target[2]+4,target[3]+3);
            Check(current[1].Paths[0][2]>oldX+3,"path node can be dragged");
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="删除"||b.Text=="删除")).PerformClick();
            Check(current[1].Paths[0].Length==8,"selected path node can be deleted");
            type.GetField("RequestText",f).SetValue(form,new Func<string,string>(s=>"工"));
            tools.SelectedItem="文字批注";
            Drag(form,70,70,70,70);
            bar.Controls.OfType<Button>().Single(b=>(b.Name=="转为笔画"||b.Text=="转为笔画")).PerformClick();
            Check(current.Last().Text==null&&tools.Text=="编辑节点","text annotation converts to editable pen paths");
            var caret=(PointF)Call(form,"Pixel",5.0,5.0);
            Call(form,"OpenBodyAt",new Point((int)caret.X,(int)caret.Y));
            Check(((Panel)Get(form,"bodyPane")).Visible,"double-click target opens inline body editor");
            using(var image=new Bitmap(form.Width,form.Height))
            {
                form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                image.Save(Path.Combine(folder,"stage3-editor.png"));
            }
        }
        var setupType=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm");
        using(var setup=(Form)Activator.CreateInstance(setupType,f,null,new object[]
        {
            null,Path.Combine(folder,Guid.NewGuid().ToString("N")+".json")
        }
        ,null))
        {
            setup.Show();
            Application.DoEvents();
            var fields=(Dictionary<string,TextBox>)Get(setup,"pageFields");
            foreach(string key in new[]
            {
                "左","右","上","下"
            }
            )fields[key].Text="0";
            fields["宽"].Text="100";
            fields["高"].Text="120";
            fields["行数"].Text="6";
            fields["字高比例"].Text="50";
            fields["旋转"].Text="0";
            ((TextBox)Get(setup,"writingInputText")).Text=body;
            setupType.GetField("drawings",f).SetValue(setup,new List<PageDrawing>
            {
                d
            }
            );
            setupType.GetField("anchorSourceText",f).SetValue(setup,body);
            var project=(WritingProject)Call(setup,"CaptureProject");
            Check(project.Version==4,"anchored projects use version 4");
            string path=Path.Combine(folder,"anchored.awproject");
            WritingProjectService.Save(path,project);
            var loaded=WritingProjectService.Load(path);
            Check(loaded.Drawings[0].HasAnchor&&loaded.Drawings[0].AnchorIndex==10&&DrawingAnchors.Canonical(loaded.AnchorSourceText)==body,"anchor identity and source text persist");
            ((TextBox)Get(setup,"writingInputText")).Text=changed;
            var output=(IList<WritingLayoutPage>)Call(setup,"LayoutAllPages");
            Check(Math.Abs(output[1].Strokes.Last().Points[0].Y+25)<.01,"main generation follows anchored drawing to new page");
            var savedAgain=(WritingProject)Call(setup,"CaptureProject");
            Check(savedAgain.Drawings[0].AnchorIndex==14,"saving after external body edit updates source anchor index");
            setupType.GetField("drawings",f).SetValue(setup,new List<PageDrawing>
            {
                wrap
            }
            );
            var wrapProject=(WritingProject)Call(setup,"CaptureProject");
            Check(wrapProject.Version==4&&wrapProject.Page.WrapAreas.Count==1,"wrapped illustration participates in project persistence");
        }
    }
}
