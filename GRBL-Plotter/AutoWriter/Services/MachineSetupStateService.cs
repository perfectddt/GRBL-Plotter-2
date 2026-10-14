using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    // Raw editor state, deliberately independent of a validated runnable writing project.
    [DataContract]
    public sealed class MachineSetupState
    {
        [DataMember(IsRequired = false)] public List<WritingRegion> Regions;
        [DataMember(IsRequired = true)] public int Version = 1;
        [DataMember(IsRequired = true)] public MachineProfile Profile;
        [DataMember(IsRequired = true)] public Dictionary<string, string> Text = new Dictionary<string, string>();
        [DataMember(IsRequired = true)] public Dictionary<string, bool> Checks = new Dictionary<string, bool>();
        [DataMember(IsRequired = true)] public string FontPath = "";
        [DataMember(IsRequired = true)] public int TabIndex;
        [DataMember(IsRequired = false)] public string TabName;
        public void Validate()
        {
            if (Version != 1 || Profile == null || Text == null || Checks == null || FontPath == null)
                throw new InvalidDataException("上次窗口设置版本不支持或字段不完整。");
            if (Regions != null) foreach (var region in Regions)
            {
                if (region == null) throw new InvalidDataException("区域为空。");
                region.Validate(new PageLayoutOptions { WidthMm = double.MaxValue, HeightMm = double.MaxValue });
            }
        }
    }
    public sealed class MachineSetupStateService
    {
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GRBL_Plotter", "AutoWriter", "last-machine-setup.json"); } }
        public string PathName { get; private set; }
        public MachineSetupStateService(string path) { PathName = Path.GetFullPath(path); }
        public MachineSetupState Load(out string warning)
        {
            warning = null;
            if (!File.Exists(PathName)) return null;
            try { return Read(PathName); }
            catch (Exception error)
            {
                warning = "上次窗口设置无法读取：" + error.Message;
                if (File.Exists(PathName + ".bak"))
                {
                    try { var recovered = Read(PathName + ".bak"); warning += "；已恢复备份。"; return recovered; }
                    catch (Exception) { warning += "；备份也不可用，使用初始值。"; }
                }
                return null;
            }
        }
        private static MachineSetupState Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var state = (MachineSetupState)new DataContractJsonSerializer(typeof(MachineSetupState)).ReadObject(stream);
                if (state == null) throw new InvalidDataException("设置为空。");
                state.Validate(); return state;
            }
        }
        public void Save(MachineSetupState state)
        {
            state.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            string temporary = PathName + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(MachineSetupState)).WriteObject(stream, state); stream.Flush(true); }
            if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".bak", true);
            else File.Move(temporary, PathName);
        }
    }
}
