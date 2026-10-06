using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using GrblPlotter.AutoWriter.Models;

namespace GrblPlotter.AutoWriter.Services
{
    public sealed class MachineProfileService
    {
        public void Save(string profilePath, MachineProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profilePath))
                throw new ArgumentException("A profile path is required.", "profilePath");
            if (profile == null)
                throw new ArgumentNullException("profile");

            profile.Validate();
            string fullPath = Path.GetFullPath(profilePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("The profile path has no directory.");
            Directory.CreateDirectory(directory);

            byte[] json = Serialize(profile);
            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            WriteDurably(temporaryPath, json);

            if (File.Exists(fullPath))
            {
                string backupPath = fullPath + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
                File.Replace(temporaryPath, fullPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }

        public MachineProfile Load(string profilePath)
        {
            if (string.IsNullOrWhiteSpace(profilePath))
                throw new ArgumentException("A profile path is required.", "profilePath");
            string fullPath = Path.GetFullPath(profilePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Machine profile was not found.", fullPath);

            try
            {
                MachineProfile profile;
                using (FileStream stream = File.OpenRead(fullPath))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MachineProfile));
                    profile = serializer.ReadObject(stream) as MachineProfile;
                }
                if (profile == null)
                    throw new MachineProfileValidationException("Machine profile JSON is empty or has the wrong root type.");
                profile.Validate();
                return profile;
            }
            catch (MachineProfileValidationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Machine profile JSON could not be read.", exception);
            }
        }

        private static byte[] Serialize(MachineProfile profile)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MachineProfile));
                serializer.WriteObject(stream, profile);
                return stream.ToArray();
            }
        }

        private static void WriteDurably(string path, byte[] data)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
        }
    }
}
