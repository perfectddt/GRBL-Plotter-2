using System;
using System.IO;
using System.Collections.Generic;
using GrblPlotter.AutoWriter.Services;
class RegionTemplateTests {
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
 static void Throws(Action action,string name){try{action();}catch{checks++;return;}throw new Exception(name);}
 static int Main(string[] args){try{Run(args[0]);Console.WriteLine(checks+" template checks passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
 static void Run(string directory){
  var sheet=new PageLayoutOptions{WidthMm=210,HeightMm=297,LeftMm=10,RightMm=10,TopMm=10,BottomMm=10,AutoWrap=true};
  var r=new WritingRegion{Text="SECRET_SOURCE_TEXT",X=10,Y=10,Width=50,Height=30,FontSize=6,LetterGap=1,LineGap=2,PageBehavior=3};
  var template=RegionTemplate.Create("标题/正文",sheet,new List<WritingRegion>{r});
  Check(template.Boxes[0].Region().Text=="","capture excludes text");Check(template.Boxes[0].PageBehavior==3,"pagination preserved");
  var service=new RegionTemplateService(directory);service.Save(template);string file=Path.Combine(directory,template.Id+".awtemplate");string json=File.ReadAllText(file);
  Check(!json.Contains("SECRET_SOURCE_TEXT")&&!json.Contains("Text")&&!json.Contains("Profile")&&!json.Contains("FontPath"),"no content or machine/font in template");
  var loaded=RegionTemplateService.Read(file);Check(loaded.Name==template.Name,"title roundtrip accepts path-like name without filesystem traversal");
  var extra=r.Clone();extra.X=80;extra.Text="EXTRA_CONTENT";var old=new List<WritingRegion>{r,extra};
  var kept=loaded.Apply(old,true);Check(kept.Count==2&&kept[0].Text==r.Text&&kept[1].Text==extra.Text,"all text preserved, extra retained");Check(!object.ReferenceEquals(kept[0],r)&&!object.ReferenceEquals(kept[1],extra),"source immutable");
  var cleared=loaded.Apply(old,false);Check(cleared.Count==1&&cleared[0].Text=="","explicit layout only");
  var tagged=r.Clone();tagged.Id="stable-a";tagged.Text="AAA";var other=extra.Clone();other.Id="stable-b";other.Text="BBB";
  var idTemplate=RegionTemplate.Create("编号对应",sheet,new List<WritingRegion>{tagged,other});
  var reordered=new List<WritingRegion>{other.Clone(),tagged.Clone()};
  var byId=idTemplate.Apply(reordered,true);
  Check(byId.Count==2&&byId[0].Id=="stable-a"&&byId[0].Text=="AAA"&&byId[1].Text=="BBB","stable id keeps text after reorder");
  Check(idTemplate.DescribeKeep(reordered)=="模板1←区域2，模板2←区域1","template correspondence follows id");
  var notStolen=idTemplate.Apply(new List<WritingRegion>{other.Clone()},true);
  Check(notStolen.Count==2&&notStolen[0].Text==""&&notStolen[1].Id=="stable-b"&&notStolen[1].Text=="BBB","different id is not attached by index");
  var plainA=r.Clone();plainA.Id=null;var plainB=extra.Clone();plainB.Id=null;
  var indexTemplate=RegionTemplate.Create("旧模板",sheet,new List<WritingRegion>{plainA,plainB});
  var indexKept=indexTemplate.Apply(new List<WritingRegion>{plainA,plainB},true);
  Check(indexKept[0].Text==plainA.Text&&indexKept[1].Text==plainB.Text,"missing id still matches by order");
  Check(indexTemplate.DescribeKeep(new List<WritingRegion>{plainA,plainB})=="模板1←区域1，模板2←区域2","index correspondence remains");
  template.Name="重命名";service.Save(template);Check(File.Exists(file+".bak"),"atomic rename retains backup");Check(RegionTemplateService.Read(file).Name=="重命名","rename persisted");
  var copy=service.Copy(template,"副本");Check(copy.Id!=template.Id,"copy gets new stable id");
  string warning;Check(service.List(out warning).Count==2&&warning=="","list templates");
  template.Version=2;Throws(()=>service.Save(template),"future version rejected");template.Version=1;
  template.Id="../outside";Throws(()=>service.Save(template),"id traversal refused");template.Id=Guid.NewGuid().ToString("N");
  template.Boxes[0].Geometry[0]=double.NaN;Throws(()=>service.Save(template),"nonfinite rejected");template.Boxes[0].Geometry[0]=10;
  File.WriteAllText(Path.Combine(directory,"broken.awtemplate"),"{}");Check(service.List(out warning).Count==2&&warning.Contains("broken.awtemplate"),"corrupt file visible skipped");
  loaded.Sheet.WidthMm=70;Throws(()=>loaded.Apply(old,true),"incompatible extra region rejected before apply");Check(old.Count==2&&old[1].Text=="EXTRA_CONTENT","reject preserves original");
  loaded.Sheet.WidthMm=210;loaded.Boxes[0].Geometry=new double[0];Throws(()=>loaded.Validate(),"bad geometry rejected");
 }
}
