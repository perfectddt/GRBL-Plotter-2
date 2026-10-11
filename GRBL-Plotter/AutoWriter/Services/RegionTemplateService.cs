using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace GrblPlotter.AutoWriter.Services {
 [DataContract] public sealed class RegionTemplateBox {
  [DataMember(IsRequired=true)] public double[] Geometry;
  [DataMember(IsRequired=true)] public int Alignment;
  [DataMember(IsRequired=true)] public int PageBehavior;
  [DataMember(IsRequired=false)] public string RegionId;
  public static RegionTemplateBox From(WritingRegion r) { return new RegionTemplateBox { Geometry=new[]{r.X,r.Y,r.Width,r.Height,r.Left,r.Right,r.Top,r.Bottom,r.FontSize,r.LetterGap,r.LineGap},Alignment=r.Alignment,PageBehavior=r.PageBehavior,RegionId=r.Id }; }
  public WritingRegion Region() { if(Geometry==null||Geometry.Length!=11)throw new InvalidDataException("模板区域参数不完整。"); return new WritingRegion {Text="",Id=RegionId,X=Geometry[0],Y=Geometry[1],Width=Geometry[2],Height=Geometry[3],Left=Geometry[4],Right=Geometry[5],Top=Geometry[6],Bottom=Geometry[7],FontSize=Geometry[8],LetterGap=Geometry[9],LineGap=Geometry[10],Alignment=Alignment,PageBehavior=PageBehavior}; }
 }
 [DataContract] public sealed class RegionTemplate {
  [DataMember(IsRequired=true)] public int Version=1;
  [DataMember(IsRequired=true)] public string Id;
  [DataMember(IsRequired=true)] public string Name;
  [DataMember(IsRequired=true)] public PageLayoutOptions Sheet;
  [DataMember(IsRequired=true)] public List<RegionTemplateBox> Boxes;
  public void Validate() { Guid id; if(Version!=1||!Guid.TryParseExact(Id,"N",out id))throw new InvalidDataException("模板版本或标识不支持。"); if(string.IsNullOrWhiteSpace(Name)||Name.Length>100||Name.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new InvalidDataException("模板名称应为1到100个字符。"); if(Sheet==null||Boxes==null||Boxes.Count==0||Boxes.Count>1000)throw new InvalidDataException("模板纸张或区域不完整。"); Sheet.Validate(); foreach(var b in Boxes){if(b==null)throw new InvalidDataException("模板区域为空。");b.Region().Validate(Sheet);} }
  public override string ToString(){return Name+"（"+Boxes.Count+"个区域，"+Sheet.WidthMm+"×"+Sheet.HeightMm+"mm）";}
  public static RegionTemplate Create(string name,PageLayoutOptions sheet,IList<WritingRegion> regions){ var t=new RegionTemplate{Id=Guid.NewGuid().ToString("N"),Name=name,Sheet=new PageLayoutOptions{WidthMm=sheet.WidthMm,HeightMm=sheet.HeightMm,LeftMm=sheet.LeftMm,RightMm=sheet.RightMm,TopMm=sheet.TopMm,BottomMm=sheet.BottomMm,AutoWrap=sheet.AutoWrap,Alignment=sheet.Alignment},Boxes=new List<RegionTemplateBox>()};foreach(var r in regions){r.Validate(sheet);t.Boxes.Add(RegionTemplateBox.From(r));}t.Validate();return t; }
  public List<WritingRegion> Apply(IList<WritingRegion> current,bool keep){Validate();var used=new List<int>();var output=new List<WritingRegion>();for(int i=0;i<Boxes.Count;i++){var r=Boxes[i].Region();int match=Find(current,i,keep,used);if(match>=0){r.Text=current[match].Text;r.FontPath=current[match].FontPath;r.FontSha256=current[match].FontSha256;used.Add(match);}output.Add(r);}if(keep&&current!=null)for(int i=0;i<current.Count;i++)if(!used.Contains(i)){var r=current[i].Clone();r.Validate(Sheet);output.Add(r);}return output;}
  public string DescribeKeep(IList<WritingRegion> current){Validate();var used=new List<int>();var parts=new List<string>();for(int i=0;i<Boxes.Count;i++){int match=Find(current,i,true,used);if(match>=0)used.Add(match);parts.Add("模板"+(i+1)+(match<0?"（无对应文字）":"←区域"+(match+1)));}return string.Join("，",parts.ToArray());}
  int Find(IList<WritingRegion> current,int boxIndex,bool keep,List<int> used){if(!keep||current==null)return -1;string id=Boxes[boxIndex].RegionId;if(!string.IsNullOrEmpty(id)){for(int i=0;i<current.Count;i++)if(!used.Contains(i)&&current[i]!=null&&current[i].Id==id)return i;if(boxIndex<current.Count&&!used.Contains(boxIndex)&&(current[boxIndex]==null||string.IsNullOrEmpty(current[boxIndex].Id)))return boxIndex;return -1;}if(boxIndex<current.Count&&!used.Contains(boxIndex))return boxIndex;return -1;}
 }
 public sealed class RegionTemplateService {
  public static string DefaultDirectory {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GRBL_Plotter","AutoWriter","RegionTemplates");}}
  readonly string directory;
  public RegionTemplateService(string folder){directory=Path.GetFullPath(folder);}
  public List<RegionTemplate> List(out string warning){warning="";var list=new List<RegionTemplate>();if(!Directory.Exists(directory))return list;foreach(var file in Directory.GetFiles(directory,"*.awtemplate")){try{list.Add(Read(file));}catch(Exception e){warning+=Path.GetFileName(file)+"："+e.Message+Environment.NewLine;}}list.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.CurrentCulture));return list;}
  public static RegionTemplate Read(string path){using(var stream=File.OpenRead(path)){var t=(RegionTemplate)new DataContractJsonSerializer(typeof(RegionTemplate)).ReadObject(stream);if(t==null)throw new InvalidDataException("模板为空。");t.Validate();return t;}}
  public static void Write(string path,RegionTemplate t){t.Validate();path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp-"+Guid.NewGuid().ToString("N");using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(RegionTemplate)).WriteObject(stream,t);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);}
  public void Save(RegionTemplate t){t.Validate();Write(Path.Combine(directory,t.Id+".awtemplate"),t);}
  public RegionTemplate Copy(RegionTemplate t,string name){t.Validate();var boxes=new List<WritingRegion>();foreach(var b in t.Boxes)boxes.Add(b.Region());var copy=RegionTemplate.Create(name,t.Sheet,boxes);Save(copy);return copy;}
 }
}
