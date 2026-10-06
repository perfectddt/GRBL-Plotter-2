/*  GRBL-Plotter. Another GCode sender for GRBL.
    This file is part of the GRBL-Plotter application.
   
    Copyright (C) 2015-2020 Sven Hasemann contact: svenhb@web.de

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

// 2018-09-19 Avoid starting prog twice

using System;
using System.Threading;
using System.Windows.Forms;

namespace GrblPlotter
{
    static class Program
    {
        internal static bool OfflineMode { get; private set; }
        internal static bool MachineInspectionMode { get; private set; }
        internal static string InspectionOncePort { get; private set; }
        internal static string ApprovedAxisStep { get; private set; }
        /// <summary>
        /// Der Haupteinstiegspunkt für die Anwendung.
        /// </summary>
        [STAThread]
        static void Main()
        {
            OfflineMode = Array.Exists(Environment.GetCommandLineArgs(),
                argument => string.Equals(argument, "--offline", StringComparison.OrdinalIgnoreCase));
            MachineInspectionMode = Array.Exists(Environment.GetCommandLineArgs(),
                argument => string.Equals(argument, "--inspect-machine", StringComparison.OrdinalIgnoreCase));
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--inspect-once=", StringComparison.OrdinalIgnoreCase))
                    InspectionOncePort = argument.Substring("--inspect-once=".Length);
            if (InspectionOncePort != null) MachineInspectionMode = true;
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--axis-step=", StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(argument, @"^--axis-step=(COM[1-9][0-9]*),(X|Y),(-?(?:1|5))$");
                    if (!match.Success) throw new ArgumentException("Axis step requires COM port, X/Y and exactly +/-1 or +/-5 mm.");
                    InspectionOncePort = match.Groups[1].Value;
                    ApprovedAxisStep = "G91G21G94G1" + match.Groups[2].Value + match.Groups[3].Value + "F60";
                    MachineInspectionMode = true;
                }
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--pen-step=", StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(argument, @"^--pen-step=(COM[1-9][0-9]*),(-?(?:0\.2|0\.5|2))$");
                    if (!match.Success || ApprovedAxisStep != null)
                        throw new ArgumentException("Pen step requires one COM port and exactly +/-0.2, +/-0.5 or +/-2 mm; do not combine motion options.");
                    InspectionOncePort = match.Groups[1].Value;
                    ApprovedAxisStep = "G91G21G94G1Z" + match.Groups[2].Value + "F30";
                    MachineInspectionMode = true;
                }
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--pen-position=", StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(argument, @"^--pen-position=(COM[1-9][0-9]*),(0|5)$");
                    if (!match.Success || ApprovedAxisStep != null)
                        throw new ArgumentException("Pen position requires one COM port and exactly 0 or 5; do not combine motion options.");
                    InspectionOncePort = match.Groups[1].Value;
                    ApprovedAxisStep = "G1G90 Z" + match.Groups[2].Value + ".0F10000";
                    MachineInspectionMode = true;
                }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            //      bool newInstance;
            using (var mutex = new Mutex(true, "Deine_Anwendung", out bool newInstance))
            {
                if (newInstance)
                    Application.Run(new MainForm());
            }
            //Application.Run(new MainForm());
        }
    }
}
