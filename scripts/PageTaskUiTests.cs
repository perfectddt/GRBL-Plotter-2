using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

class PageTaskUiTests
{
    static int checks;
    static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    static Button Find(Control root, string text)
    {
        var button = root as Button;
        if (button != null && button.Text == text) return button;
        foreach (Control child in root.Controls)
        {
            Button found = Find(child, text);
            if (found != null) return found;
        }
        return null;
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string statePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "autowriter-page-task-ui-" + Guid.NewGuid().ToString("N") + ".json");
            Assembly assembly = Assembly.LoadFrom(args[0]);
            Type formType = assembly.GetType("GrblPlotter.AutoWriter.MachineSetupForm", true);
            Type taskType = assembly.GetType("GrblPlotter.AutoWriter.Services.PageTaskService", true);
            Type witnessType = assembly.GetType("GrblPlotter.AutoWriter.Services.PageCompletionWitness", true);
            object task = Activator.CreateInstance(taskType, true);
            int loads = 0;
            Action<string> load = delegate(string program) { loads++; };
            using (Form form = (Form)Activator.CreateInstance(formType, flags, null, new object[] { null, statePath, task, load }, null))
            {
                Button freeze = Find(form, "冻结多页任务");
                Button paper = Find(form, "已换纸并确认位置");
                Check(freeze != null && paper != null, "page task buttons exist");
                Check(!paper.Enabled, "paper button starts disabled");
                object[] bindArgs = new object[] { "fp", new List<string> { "PAGE-1", "PAGE-2" }, null };
                taskType.GetMethod("Bind", flags).Invoke(task, bindArgs);
                taskType.GetMethod("NoteLoaded", flags).Invoke(task, null);
                taskType.GetMethod("NoteStarted", flags).Invoke(task, null);
                object witness = Activator.CreateInstance(witnessType, true);
                foreach (string name in new[] { "AllSent", "AllAcknowledged", "PenRaised", "Connected", "IdleBarrier" })
                    witnessType.GetField(name, flags).SetValue(witness, true);
                taskType.GetMethod("NoteFinish", flags).Invoke(task, new object[] { witness });
                Check(paper.Enabled, "paper button enables only after physical completion");
                MethodInfo confirm = formType.GetMethod("ConfirmPaperFromSetup", flags);
                confirm.Invoke(form, null);
                Check(loads == 1, "confirm loads the next page once");
                confirm.Invoke(form, null);
                Check(loads == 1, "repeat confirm does not load again");
                var input = (TextBox)formType.GetField("writingInputText", flags).GetValue(form);
                input.Text += "X";
                object phase = taskType.GetProperty("Phase", flags).GetValue(task, null);
                Check(phase.ToString() == "Fault", "editing invalidates the frozen task");
                confirm.Invoke(form, null);
                Check(loads == 1, "invalid task does not load another page");
            }
            Console.WriteLine("PASS: " + checks + " page task UI checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(error.GetType().Name + ": " + error.Message);
            if (error.InnerException != null) Console.WriteLine(error.InnerException.GetType().Name + ": " + error.InnerException.Message);
            return 1;
        }
    }
}
