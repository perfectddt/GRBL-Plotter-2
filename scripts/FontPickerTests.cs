using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using GrblPlotter.AutoWriter;
using GrblPlotter.AutoWriter.Services;
class FontPickerTests
{
    static int checks;static BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool result,string reason){if(!result)throw new Exception(reason);checks++;}
    static object Get(object o,string key){return o.GetType().GetField(key,f).GetValue(o);}
    [STAThread]static int Main(string[] args)
    {
        try{Run(args[0]);Console.WriteLine("PASS "+checks+" font import and picker checks");return 0;}catch(Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;Console.Error.WriteLine(ex);return 1;}
    }
    static void Run(string output)
    {
        Directory.CreateDirectory(output);string root=Path.GetFullPath(Path.Combine(output,"..","local-fonts"));var paths=Directory.GetFiles(root,"*.fontpack.json").Where(p=>!p.EndsWith(".mix.fontpack.json")).ToArray();Check(paths.Length==70,"all seventy local fonts converted");
        foreach(string path in Environment.GetEnvironmentVariable("AUTOWRITER_FONT_QUICK")=="1"?paths.Take(1):paths)
        {
            Console.WriteLine("validate "+Path.GetFileName(path));var pack=GlyphFontPack.LoadFromFile(path);Check(pack.GlyphCount>0,"font loads: "+path);Check(pack.Source.Contains("Local GFont:"),"font retains source provenance: "+path);
            ((IDictionary)typeof(GlyphFontPack).GetField("Cache",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Clear();
        }
        Check(GlyphFontPackCatalog.DiscoverPackPaths().Any(p=>p==paths[0]),"imported local library discovered");
        var type=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.FontPicker");
        using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]{paths[0]},null))
        {
            type.GetField("ReportError",f).SetValue(form,(Action<string>)(message=>{throw new Exception(message);}));form.Show();Application.DoEvents();var selected=(List<FontMixSource>)Get(form,"selected");selected.Add(new FontMixSource{Path=paths[1],Weight=3,Sha256=WritingProjectService.Fingerprint(paths[1])});
            type.GetMethod("RefreshChosen",f).Invoke(form,null);Check(((ListBox)Get(form,"chosen")).Items.Count==2,"two selected fonts shown");
            ((TextBox)Get(form,"search")).Text="a-font-name-that-does-not-exist";Check(((ListBox)Get(form,"available")).Items.Count==0,"search filters cards");((TextBox)Get(form,"search")).Clear();Check(((ListBox)Get(form,"available")).Items.Count>=70,"clear search restores catalog");
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"font-picker.png"));}
            Console.WriteLine("confirm mix");type.GetMethod("Confirm",f).Invoke(form,null);Console.WriteLine("mix confirmed");string mixPath=(string)Get(form,"SelectedPath");Check(File.Exists(mixPath),"mixed selection persisted");var mixed=GlyphFontPack.LoadFromFile(mixPath);var a=GlyphFontPack.LoadFromFile(paths[0]).Resolve('工');var b=GlyphFontPack.LoadFromFile(paths[1]).Resolve('工');var resolver=mixed.ToResolver();var repeat=mixed.ToResolver();int aCount=0,bCount=0;
            for(int i=0;i<2000;i++){var g=resolver('工');Check(object.ReferenceEquals(g,repeat('工')),"mix seeded sequence deterministic");if(object.ReferenceEquals(g,a))aCount++;if(object.ReferenceEquals(g,b))bCount++;}
            Check(aCount>350&&aCount<650&&bCount>1350&&bCount<1650,"weight 1:3 affects actual glyph selection");
            using(var reopened=(Form)Activator.CreateInstance(type,f,null,new object[]{mixPath},null)){var choices=(List<FontMixSource>)Get(reopened,"selected");Check(choices.Count==2&&choices[1].Weight==3,"reopening restores sources and weights");}
            using(var modal=(Form)Activator.CreateInstance(type,f,null,new object[]{mixPath},null))using(var timer=new Timer{Interval=150})
            {timer.Tick+=(s,e)=>{timer.Stop();type.GetMethod("Confirm",f).Invoke(modal,null);};timer.Start();Check(modal.ShowDialog()==DialogResult.OK,"actual modal confirmation closes successfully");File.Delete((string)Get(modal,"SelectedPath"));}
            var sheet=new PageLayoutOptions{WidthMm=183,HeightMm=253,LeftMm=10,TopMm=10,RightMm=10,BottomMm=10,AutoWrap=true,LinesPerPage=28,FontHeightPercent=95};var pages=TextLayoutService.LayoutPages("工王一永工王一永",mixed.ToResolver(),1,2,12,1,sheet);Check(pages[0].Strokes.Count>0,"mixed font produces actual paginated paths");
            File.Delete(mixPath);
        }
        TestFallback(output,type);
        TestDeformation(output,type);
    }
    static void TestFallback(string output,Type type)
    {
        string primary=Path.Combine(output,"primary.fontpack.json"),backup=Path.Combine(output,"backup.fontpack.json");
        WritePack(primary,"主字库","工");WritePack(backup,"补充字库","工甲");
        var a=GlyphFontPack.LoadFromFile(primary);var b=GlyphFontPack.LoadFromFile(backup);
        using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]{primary,backup},null))
        {
            type.GetField("ReportError",f).SetValue(form,(Action<string>)(message=>{throw new Exception(message);}));form.Show();Application.DoEvents();
            var selected=(List<FontMixSource>)Get(form,"selected");Check(selected.Count==2&&selected[1].Fallback,"existing supplemental font appears as fallback");
            var chosen=(ListBox)Get(form,"chosen");chosen.ClearSelected();chosen.SelectedIndex=0;((NumericUpDown)Get(form,"weight")).Value=7;
            Check(chosen.Items[0].ToString().Contains("权重：7"),"weight change reflected in selected font caption");
            chosen.ClearSelected();chosen.SelectedIndex=1;Check(!((NumericUpDown)Get(form,"weight")).Enabled,"supplemental font has no random weight");
            var views=(TabControl)Get(form,"selectedViews");views.SelectedIndex=1;Check(chosen.Items.Count==1&&((NumericUpDown)Get(form,"weight")).Value==7,"primary view preserves selected font mapping");views.SelectedIndex=2;Check(chosen.Items.Count==1&&!((NumericUpDown)Get(form,"weight")).Enabled,"supplement view maps to supplement object");views.SelectedIndex=0;Check(chosen.Items.Count==2&&selected.Count==2,"switching views does not remove hidden selections");
            using(var image=(Image)type.GetMethod("Thumbnail",f).Invoke(form,new object[]{primary}))Check(image.Width>0,"font without preview PNG gets real generated thumbnail");
            // The thumbnail belongs to the form, so remove the disposed test image from its cache.
            ((IDictionary)Get(form,"thumbnails")).Remove(primary);
            ((TextBox)Get(form,"sampleInput")).Text="工甲工甲";Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"fallback-picker.png"));}
            type.GetMethod("Confirm",f).Invoke(form,null);string mixedPath=(string)Get(form,"SelectedPath");var mixed=GlyphFontPack.LoadFromFile(mixedPath);var resolver=mixed.ToResolver();
            for(int i=0;i<100;i++)Check(object.ReferenceEquals(resolver('工'),a.Resolve('工')),"supplement does not replace a primary glyph");
            Check(object.ReferenceEquals(resolver('甲'),b.Resolve('甲')),"missing primary glyph resolves through supplement");
            var oldResolver=a.WithFallback(b).ToResolver();Check(object.ReferenceEquals(oldResolver('工'),a.Resolve('工'))&&object.ReferenceEquals(oldResolver('甲'),b.Resolve('甲')),"legacy supplement path also fills only missing glyphs");
            using(var restored=(Form)Activator.CreateInstance(type,f,null,new object[]{mixedPath},null)){var items=(List<FontMixSource>)Get(restored,"selected");Check(items[0].Weight==7&&items[1].Fallback,"weights and supplement roles survive reopening");}
            selected[0].Fallback=true;bool rejected=false;type.GetField("ReportError",f).SetValue(form,(Action<string>)(message=>rejected=true));type.GetMethod("Confirm",f).Invoke(form,null);Check(rejected,"all-supplement selection rejected");File.Delete(mixedPath);
        }
    }
    static void WritePack(string path,string name,string characters)
    {
        string glyphs=string.Join(",",characters.Select(c=>"{\"c\":\""+c+"\",\"strokes\":[[1,1,11,11]]}"));
        File.WriteAllText(path,"{\"schemaVersion\":1,\"name\":\""+name+"\",\"source\":\"test\",\"license\":\"test\",\"emSizeMm\":12,\"bodySizeMm\":10,\"baselineMm\":1,\"advanceCjkMm\":12,\"advanceSpaceMm\":6,\"glyphs\":["+glyphs+"]}");
    }
    static string pathsForPreview(string output){return Directory.GetFiles(Path.Combine(output,"..","local-fonts"),"*.fontpack.json").First(p=>!p.EndsWith(".mix.fontpack.json"));}
    static double Width(BuiltinGlyph glyph){var x=glyph.Strokes.SelectMany(s=>s.Points).Select(p=>p.X).ToArray();return x.Max()-x.Min();}
    static void TestDeformation(string output,Type type)
    {
        var original=BuiltinGlyphLibrary.Resolve('工');Check(object.ReferenceEquals(new FontDeformation().Apply(original,new Random(1)),original),"disabled deformation leaves exact glyph unchanged");
        var settings=new FontDeformation{Enabled=true,SizeMin=50,SizeMax=50};var smaller=settings.Apply(original,new Random(1));Check(Math.Abs(Width(smaller)/Width(original)-.5)<1e-8,"size deformation changes actual path coordinates");Check(Width(original)>Width(smaller),"deformation never mutates source glyph");
        settings=FontDeformation.Recommended();var first=new Random(settings.Seed);var second=new Random(settings.Seed);for(int i=0;i<20;i++){var a=settings.Apply(original,first);var b=settings.Apply(original,second);Check(a.Strokes.SelectMany(s=>s.Points).Select(p=>p.X).SequenceEqual(b.Strokes.SelectMany(s=>s.Points).Select(p=>p.X)),"deformation seed reproduces exact geometry");}
        settings.SizeMin=110;settings.SizeMax=90;bool invalid=false;try{settings.Validate();}catch(ArgumentException){invalid=true;}Check(invalid,"reversed deformation range rejected");
        string primary=Path.Combine(output,"primary.fontpack.json"),backup=Path.Combine(output,"backup.fontpack.json");
        using(var form=(Form)Activator.CreateInstance(type,f,null,new object[]{primary,backup},null))
        {
            type.GetField("ReportError",f).SetValue(form,(Action<string>)(message=>{throw new Exception(message);}));var sources=(List<FontMixSource>)Get(form,"selected");sources[0].Deformation=new FontDeformation{Enabled=true,SizeMin=50,SizeMax=50};
            form.Show();Application.DoEvents();type.GetMethod("Confirm",f).Invoke(form,null);string sourceOnly=(string)Get(form,"SelectedPath");var resolver=GlyphFontPack.LoadFromFile(sourceOnly).ToResolver();Check(Math.Abs(Width(resolver('工'))-5)<1e-8&&Math.Abs(Width(resolver('甲'))-10)<1e-8,"per-font deformation does not alter a different fallback font");
            var global=new FontDeformation{Enabled=true,HorizontalMin=80,HorizontalMax=80,SpacingMin=50,SpacingMax=50};type.GetField("deformation",f).SetValue(form,global);type.GetField("deformationConfigured",f).SetValue(form,true);type.GetMethod("Confirm",f).Invoke(form,null);string path=(string)Get(form,"SelectedPath");var mixed=GlyphFontPack.LoadFromFile(path);var both=mixed.ToResolver();Check(Math.Abs(Width(both('工'))-4)<1e-8&&Math.Abs(Width(both('甲'))-8)<1e-8,"per-font transformation is followed by global transformation");
            var preview=(BuiltinGlyph)type.GetMethod("PreviewGlyph",f).Invoke(form,new object[]{'工',new Random(20261108)});Check(Math.Abs(Width(global.Apply(preview,new Random(global.Seed)))-4)<1e-8,"combined preview uses same deformation scopes");
            var sheet=new PageLayoutOptions{WidthMm=100,HeightMm=100,AutoWrap=true};var layout=TextLayoutService.LayoutPages("工工",mixed.ToResolver(),4,2,12,1,sheet);double expected=mixed.ToResolver()('工').AdvanceMm+2;Check(Math.Abs(layout[0].Anchors[1].X-expected)<1e-8,"spacing percentage changes actual layout advance");
            using(var reopened=(Form)Activator.CreateInstance(type,f,null,new object[]{path},null)){var saved=(List<FontMixSource>)Get(reopened,"selected");Check(saved[0].Deformation.SizeMin==50&&((FontDeformation)Get(reopened,"deformation")).HorizontalMin==80,"both deformation scopes survive reopening");}
            var chosen=(ListBox)Get(form,"chosen");chosen.ClearSelected();chosen.SetSelected(0,true);chosen.SetSelected(1,true);Check(chosen.SelectedIndices.Count==2,"selected-font list supports multiple selection");
            type.GetMethod("ApplySourceDeformation",f).Invoke(form,new object[]{sources.ToArray(),FontDeformation.Recommended()});Check(sources.All(s=>s.Deformation.Enabled&&s.Deformation.SizeMin==95),"batch deformation updates all targeted fonts");Check(!object.ReferenceEquals(sources[0].Deformation,sources[1].Deformation),"batch settings are independently cloned per font");
            var dialogType=typeof(PageDrawing).Assembly.GetType("GrblPlotter.AutoWriter.FontDeformationForm");Func<char,Random,BuiltinGlyph> font=(c,r)=>GlyphFontPack.LoadFromFile(pathsForPreview(output)).ResolveWithFallback(c);
            using(var dialog=(Form)Activator.CreateInstance(dialogType,f,null,new object[]{FontDeformation.Recommended(),font},null)){dialog.Show();Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(output,"font-deformation.png"));}dialog.Close();}
            File.Delete(sourceOnly);File.Delete(path);
        }
    }
}
