using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
class WritingEditorUiTests
{
    static BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(Form form, string name) { return form.GetType().GetField(name, Flags).GetValue(form); }
    [STAThread] static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(args[0]);
        var type = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);
        string path = Path.Combine(Path.GetTempPath(), "writing-editor-ui-" + Guid.NewGuid().ToString("N"), "state.json");
        using (var form = (Form)Activator.CreateInstance(type, Flags, null, new object[] { null, path }, null))
        {
            var box = (TextBox)Field(form, "writingInputText");
            var handle = box.Handle;
            foreach (string input in new[] { "A\r\n\nB", "A\r\r\nB", "A\nB" })
            {
                box.Text = input;
                if (box.Text != "A\r\nB" || box.Lines.Length != 2) throw new Exception("Editor assignment failed");
            }
            foreach (string input in new[] { "A\n\nB", "A\r\n\r\nB" })
            {
                box.Text = input;
                if (box.Text != "A\r\n\r\nB" || box.Lines.Length != 3) throw new Exception("Intentional blank line lost");
            }
            var stateType = assembly.GetType("GrblPlotter.AutoWriter.Services.MachineSetupState", true);
            object state = Activator.CreateInstance(stateType);
            stateType.GetField("Profile").SetValue(state, Field(form, "profile"));
            ((Dictionary<string, string>)stateType.GetField("Text").GetValue(state)).Add("text", "A\r\n\nB");
            object service = Field(form, "lastState");
            service.GetType().GetMethod("Save").Invoke(service, new[] { state });
            type.GetMethod("RestoreLastState", Flags).Invoke(form, null);
            if (box.Text != "A\r\nB") throw new Exception("Old state recovery failed");
            type.GetMethod("SaveLastState", Flags).Invoke(form, null);
            object[] loadArgs = { null };
            object saved = service.GetType().GetMethod("Load").Invoke(service, loadArgs);
            if (((Dictionary<string, string>)stateType.GetField("Text").GetValue(saved))["text"] != "A\r\nB") throw new Exception("Saved text not canonical");
            if (box.WordWrap || box.ScrollBars != ScrollBars.Both) throw new Exception("Editor visual row policy incorrect");
        }
        Console.WriteLine("PASS actual MachineSetupForm: 5 assignment cases, old-state restore, canonical save, explicit rows");
        return 0;
    }
}
