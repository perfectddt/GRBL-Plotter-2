using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;
class PaginationUiTests {
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Get(Form f,string n){return f.GetType().GetField(n,flags).GetValue(f);}
 static object Call(Form f,string n){return f.GetType().GetMethod(n,flags).Invoke(f,null);}
 static void Assert(bool b,string n){if(!b)throw new Exception(n);}
 [STAThread] static int Main(string[] a){
 var asm=Assembly.LoadFrom(a[0]);var type=asm.GetType("GrblPlotter.AutoWriter.MachineSetupForm",true);
 string path=Path.Combine(Path.GetTempPath(),"pagination-ui-"+Guid.NewGuid().ToString("N"),"state.json");
 using(var f=(Form)Activator.CreateInstance(type,flags,null,new object[]{null,path},null)) {
 ((TextBox)Get(f,"writingInputText")).Text="工王工王";
 ((TextBox)Get(f,"fontSizeInput")).Text="10";
 ((TextBox)Get(f,"letterSpacingInput")).Text="2";
 ((TextBox)Get(f,"lineSpacingInput")).Text="0";
 var fields=(Dictionary<string,TextBox>)Get(f,"pageFields");
 fields["宽"].Text="46";fields["高"].Text="30";
 var page=(NumericUpDown)Get(f,"pageSelector");
 var first=(IList)Call(f,"BuildWritingStrokes"); Assert(first.Count==7,"selected first page only");
 Assert(((IList)Get(f,"writingPages")).Count==2,"two pages");
 ((TextBox)Get(f,"writingText")).Text="old";page.Value=2;
 Assert(((TextBox)Get(f,"writingText")).Text=="","switch clears preview");
 Assert(((IList)Call(f,"BuildWritingStrokes")).Count==7,"selected second page only");
 Call(f,"SaveLastState");page.Value=1;Call(f,"RestoreLastState");Assert(page.Value==2,"state selected page restored");
 ((TextBox)Get(f,"writingText")).Text="old";fields["宽"].Text="50";
 Assert(Get(f,"writingPages")==null && ((TextBox)Get(f,"writingText")).Text=="","parameter invalidates page cache and code");
 }
 Console.WriteLine("PASS pagination UI selected page/state/invalidation");return 0;
 }
}
