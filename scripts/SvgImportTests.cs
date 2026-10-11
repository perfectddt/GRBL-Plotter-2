// SVG import ground-truth harness.
// Converts an SVG with GRBL-Plotter's own importer and prints the resulting G-code,
// so an import result can be inspected without starting the GUI.
//
// Usage: svg-import-harness.exe <svg-path> [device] [z-up] [z-down] [z-feed] [sync]
//   device : Laser | Plotter | Router | Individual   (default Laser)
//   z-up / z-down / z-feed : numeric overrides; use "-" to keep the value from Settings
//   sync   : literal "sync" -> run AutoWriter's PenControlSync before converting
//            (the same call the setup window makes before a graphic import)
//
// Keep this file ASCII only (it is compiled by csc, which reads a BOM-less file as ANSI).
using System;
using System.Reflection;
using System.Text;

class SvgImportHarness
{
    const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags AnyInst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static Assembly app;

    static Type T(string fullName)
    {
        Type t = app.GetType(fullName, false);
        if (t == null) throw new Exception("type not found: " + fullName);
        return t;
    }

    static object PropGet(object o, string name)
    {
        PropertyInfo p = o.GetType().GetProperty(name, AnyInst);
        if (p == null) throw new Exception("property not found: " + o.GetType().FullName + "." + name);
        return p.GetValue(o, null);
    }

    static void PropSet(object o, string name, object v)
    {
        PropertyInfo p = o.GetType().GetProperty(name, AnyInst);
        if (p == null) throw new Exception("property not found: " + o.GetType().FullName + "." + name);
        Type tt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        object val = Convert.ChangeType(v, tt, System.Globalization.CultureInfo.InvariantCulture);
        p.SetValue(o, val, null);
    }

    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            Console.Error.WriteLine("HARNESS-FAILED: " + e.GetType().FullName + " :: " + e.Message);
            Console.Error.WriteLine(e.StackTrace);
            return 4;
        }
    }

    static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: svg-import-harness.exe <svg-path> [device] [zup] [zdown] [zfeed] [sync]");
            return 2;
        }

        string svg = System.IO.Path.GetFullPath(args[0]);
        string deviceName = (args.Length > 1 && args[1].Length > 0) ? args[1] : "Laser";
        string zUpArg = (args.Length > 2 && args[2] != "-") ? args[2] : null;
        string zDownArg = (args.Length > 3 && args[3] != "-") ? args[3] : null;
        string zFeedArg = (args.Length > 4 && args[4] != "-") ? args[4] : null;
        bool doSync = args.Length > 5 && args[5] == "sync";

        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        app = typeof(GrblPlotter.Graphic).Assembly;

        // Point the data folder at the app folder, the same way the real app does in
        // offline mode, so the tool list (_currentToolList.xml) is the user's real one.
        Type datapath = T("GrblPlotter.Datapath");
        string appFolder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        datapath.GetField("AppDataFolder", AnyStatic).SetValue(null, appFolder);
        Console.WriteLine("### appDataFolder=" + appFolder);

        // ---- app settings (the same values the GUI loads out of user.config) ----
        Type settingsType = T("GrblPlotter.Properties.Settings");
        object settings = settingsType.GetProperty("Default", AnyStatic).GetValue(null, null);
        Func<string, double> num = delegate (string key)
        {
            PropertyInfo p = settingsType.GetProperty(key, AnyInst);
            if (p == null) return 0;
            return Convert.ToDouble(p.GetValue(settings, null), inv);
        };

        // ---- device Z values (mirrors UCDevice*.UpdateTools) ----
        double up, down, feed, feedXY, finalS;
        bool useM3;
        if (deviceName == "Router")
        {
            up = num("DeviceRouterZUp"); down = num("DeviceRouterZDown"); feed = num("DeviceRouterSpeedZ");
            feedXY = num("DeviceRouterSpeedXY"); finalS = num("DeviceRouterSpindle"); useM3 = true;
        }
        else if (deviceName == "Plotter")
        {
            up = num("DevicePlotterZUp"); down = num("DevicePlotterZDown"); feed = num("DevicePlotterSpeedZ");
            feedXY = num("DevicePlotterSpeed"); finalS = 0; useM3 = true;
        }
        else
        {
            up = num("DeviceLaserZSave"); down = num("DeviceLaserZDown"); feed = num("DeviceLaserZFeed");
            feedXY = num("DeviceLaserSpeed"); finalS = num("DeviceLaserPower"); useM3 = false;
        }
        if (zUpArg != null) up = double.Parse(zUpArg, inv);
        if (zDownArg != null) down = double.Parse(zDownArg, inv);
        if (zFeedArg != null) feed = double.Parse(zFeedArg, inv);

        Type deviceEnumType = T("GrblPlotter.UserControls.DeviceSelection");
        Type myControlType = T("GrblPlotter.UserControls.MyControl");
        Type toolPropType = T("GrblPlotter.ToolProperty");

        object toolProp = Activator.CreateInstance(toolPropType, true);
        object dev = PropGet(toolProp, deviceName);
        PropSet(dev, "SaveZ", up);
        PropSet(dev, "FinalZ", down);
        PropSet(dev, "FeedZ", feed);
        PropSet(dev, "FeedXY", feedXY);
        PropSet(dev, "FinalS", finalS);
        PropSet(dev, "UseM3", useM3);

        object deviceValue = Enum.Parse(deviceEnumType, deviceName);
        myControlType.GetMethod("SetToolsProperties", AnyStatic).Invoke(null, new object[] { deviceValue, toolProp });
        int tab = deviceName == "Laser" ? 0 : deviceName == "Plotter" ? 1 : deviceName == "Router" ? 2 : 3;
        myControlType.GetMethod("SetSelectedDevice", AnyStatic).Invoke(null, new object[] { tab });

        Console.WriteLine("### device=" + deviceName + "  Zup=" + up + "  Zdown=" + down + "  Zfeed=" + feed + "  FeedXY=" + feedXY);

        IDisposable penScope = null;
        if (doSync)
        {
            // Same call MachineSetupForm makes before a graphic import.
            Type profileType = T("GrblPlotter.AutoWriter.Models.MachineProfile");
            Type speedsType = T("GrblPlotter.AutoWriter.Models.MachineSpeeds");
            Type optionsType = T("GrblPlotter.AutoWriter.Services.WritingProgramOptions");
            Type syncType = T("GrblPlotter.AutoWriter.Services.PenControlSync");

            object profile = Activator.CreateInstance(profileType);
            PropSet(profile, "PenControlMode", "Z");
            PropSet(profile, "PenUpValue", (double?)up);
            PropSet(profile, "PenDownValue", (double?)down);
            object speeds = Activator.CreateInstance(speedsType);
            PropSet(speeds, "ZMmPerMin", (double?)feed);
            PropSet(speeds, "DrawingMmPerMin", (double?)1000);
            PropSet(speeds, "TravelMmPerMin", (double?)1800);
            PropSet(profile, "Speeds", speeds);

            object options = Activator.CreateInstance(optionsType);
            PropSet(options, "ZMmPerMin", (double?)feed);
            PropSet(options, "DrawingMmPerMin", (double?)1000);

            penScope = (IDisposable)syncType.GetMethod("Begin", AnyStatic).Invoke(null, new object[] { profile, options });
            Console.WriteLine("### sync=temporary per-conversion pen override");
        }

        bool converted;
        try
        {
            converted = GrblPlotter.GCodeFromSvg.ConvertFromFile(svg, false, null, null);
        }
        catch (Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            Console.Error.WriteLine("CONVERT-THREW: " + e.GetType().FullName + " :: " + e.Message);
            return 3;
        }

        finally { if (penScope != null) penScope.Dispose(); }

        FieldInfo field = typeof(GrblPlotter.Graphic).GetField("GCode", AnyStatic | BindingFlags.NonPublic | BindingFlags.Public);
        string text = field == null ? "(GCode field not found)" : field.GetValue(null).ToString();

        Console.WriteLine("### converted=" + converted + "  chars=" + text.Length);
        Console.WriteLine(text);
        return 0;
    }
}
