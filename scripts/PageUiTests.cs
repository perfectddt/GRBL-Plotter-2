using System;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
class PageUiTests {
    static int checks;
    static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(Form f,string name) { return f.GetType().GetField(name,flags).GetValue(f); }
    static void Assert(bool value,string name) { if (!value) throw new Exception(name); checks++; }
    static void Invalidate(Form f, Action action,string name) { var preview=(TextBox)Get(f,"writingText"); preview.Text="old program"; action(); Assert(preview.Text=="",name); }
    [STAThread] static int Main(string[] args) {
        var assembly=Assembly.LoadFrom(args[0]); var type=assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm",true);
        using(var f=(Form)Activator.CreateInstance(type,true)) {
            var selector=(ComboBox)Get(f,"fontSelector");
            string longName=new string('字',180); selector.Items.Add(longName);
            selector.CreateControl();
            selector.GetType().GetMethod("OnDropDown",flags).Invoke(selector,new object[]{EventArgs.Empty});
            Assert((bool)selector.GetType().GetProperty("HorizontalScrollEnabled",flags).GetValue(selector,null),"font list native horizontal scrollbar");
            Assert((int)selector.GetType().GetProperty("HorizontalExtent",flags).GetValue(selector,null)>selector.DropDownWidth,"font list scroll extent retains full name");
            selector.Items.Remove(longName);
            var page=(CheckBox)Get(f,"pageEnabled"); var origin=(CheckBox)Get(f,"startAtOrigin");
            Assert(page.Checked && !origin.Checked && !origin.Enabled,"page origin rule visible");
            Assert(((TextBox)Get(f,"writingInputText")).Multiline,"paragraph input");
            foreach(string name in new[]{"writingInputText","letterSpacingInput","lineSpacingInput","fontSizeInput","parkX","parkY"}) {
                var box=(TextBox)Get(f,name); Invalidate(f,()=>box.Text += "1",name);
            }
            foreach(var box in ((Dictionary<string,TextBox>)Get(f,"pageFields")).Values) Invalidate(f,()=>box.Text+="1","page field");
            foreach(var box in ((Dictionary<string,TextBox>)Get(f,"overrides")).Values) Invalidate(f,()=>box.Text="1","writing override");
            foreach(string name in new[]{"parkEnabled","autoWrap","pageEnabled","startAtOrigin"}) {
                var box=(CheckBox)Get(f,name); Invalidate(f,()=>box.Checked=!box.Checked,name);
            }
            Assert(origin.Enabled,"legacy alignment selectable");
            var preview=(TextBox)Get(f,"writingText"); preview.Text="old program";
            type.GetMethod("NewProfile",flags).Invoke(f,null); Assert(preview.Text=="","new profile invalidates");
        }
        Console.WriteLine("PASS: "+checks+" page UI checks");return 0;
    }
}
