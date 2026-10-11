using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

class PenPreviewTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static double Coordinate(object point, string name) { return Convert.ToDouble(point.GetType().GetField(name, Instance).GetValue(point)); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.LoadFrom(args[0]);
            var mainType = assembly.GetType("GrblPlotter.MainForm", true);
            var grbl = assembly.GetType("GrblPlotter.Grbl", true);
            object main = FormatterServices.GetUninitializedObject(mainType);
            var positionField = grbl.GetField("posWork", Static);
            var connectionField = grbl.GetField("isConnected", Static);
            object savedPosition = positionField.GetValue(null);
            object savedConnection = connectionField.GetValue(null);
            using (var picture = new PictureBox())
            using (var editor = (Control)Activator.CreateInstance(mainType.GetField("fCTBCode", Instance).FieldType))
            {
                mainType.GetField("pictureBox1", Instance).SetValue(main, picture);
                mainType.GetField("fCTBCode", Instance).SetValue(main, editor);
                editor.Text = "G90\nG0 X10 Y20\nG1 Z5\nG1 X11 Y21";
                try
                {
                    foreach (bool connected in new[] { false, true })
                    {
                        object position = Activator.CreateInstance(positionField.FieldType, new object[] { 3.0, 4.0, 0.0 });
                        positionField.SetValue(null, position);
                        connectionField.SetValue(null, connected);
                        mainType.GetMethod("PlacePenMarkerOnRedInk", Instance).Invoke(main, null);
                        object current = positionField.GetValue(null);
                        Check(Coordinate(current, "X") == 3 && Coordinate(current, "Y") == 4, "Preview load changed controller position");
                        object marker = mainType.GetField("penOnPaper", Instance).GetValue(main);
                        Check(Coordinate(marker, "X") == 10 && Coordinate(marker, "Y") == 20, "Preview marker not placed on program");
                        object result = mainType.GetMethod("SetPenStart", Instance).Invoke(main, new object[] { 35.0, 36.0 });
                        Check(result == null, "Display-only marker refused while idle");
                        current = positionField.GetValue(null);
                        Check(Coordinate(current, "X") == 3 && Coordinate(current, "Y") == 4, "Marker edit changed work position");
                    }
                    object before = mainType.GetField("penOnPaper", Instance).GetValue(main);
                    Check(mainType.GetMethod("SetPenStart", Instance).Invoke(main, new object[] { double.NaN, 2.0 }) != null, "NaN marker accepted");
                    Check(Coordinate(mainType.GetField("penOnPaper", Instance).GetValue(main), "X") == Coordinate(before, "X"), "Rejected input changed marker");
                    mainType.GetField("isStreaming", Instance).SetValue(main, true);
                    Check(mainType.GetMethod("SetPenStart", Instance).Invoke(main, new object[] { 1.0, 2.0 }) != null, "Streaming marker edit accepted");
                }
                finally { positionField.SetValue(null, savedPosition); connectionField.SetValue(null, savedConnection); }
            }
            Console.WriteLine("PASS: " + checks + " display-only pen preview checks; no serial instance or hardware used");
            return 0;
        }
        catch (Exception error) { while (error.InnerException != null) error = error.InnerException; Console.Error.WriteLine(error); return 1; }
    }
}
