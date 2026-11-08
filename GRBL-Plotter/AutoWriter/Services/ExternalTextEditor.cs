using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GrblPlotter.AutoWriter.Services
{
    [DataContract]
    public sealed class ExternalTextEditor
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Program { get; set; }
        [DataMember] public string Arguments { get; set; }
        public static string ConfigPath { get { return Path.Combine(Path.GetDirectoryName(MachineSetupStateService.DefaultPath), "external-editors.json"); } }
        public static List<ExternalTextEditor> Load()
        {
            if (!File.Exists(ConfigPath)) return new List<ExternalTextEditor> {
                new ExternalTextEditor { Name = "VS Code", Program = "code.cmd", Arguments = "{file}" },
                new ExternalTextEditor { Name = "Neovim", Program = "nvim.exe", Arguments = "{file}" },
                new ExternalTextEditor { Name = "记事本", Program = "notepad.exe", Arguments = "{file}" } };
            using (var stream = File.OpenRead(ConfigPath))
                return (List<ExternalTextEditor>)new DataContractJsonSerializer(typeof(List<ExternalTextEditor>)).ReadObject(stream);
        }
        public static void Save(List<ExternalTextEditor> editors)
        {
            foreach (var editor in editors)
                if (string.IsNullOrWhiteSpace(editor.Name) || string.IsNullOrWhiteSpace(editor.Program))
                    throw new ArgumentException("编辑器名称和程序路径不能为空。");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            string temp = ConfigPath + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = File.Create(temp))
                new DataContractJsonSerializer(typeof(List<ExternalTextEditor>)).WriteObject(stream, editors);
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, ConfigPath + ".bak");
            else File.Move(temp, ConfigPath);
        }
        // Windows command-line quoting; the filename is always one argument, including spaces.
        public static string QuoteFile(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }
        public string BuildArguments(string file)
        {
            string args = Arguments ?? "";
            return args.Contains("{file}") ? args.Replace("{file}", QuoteFile(file)) : args + " " + QuoteFile(file);
        }
    }
}
