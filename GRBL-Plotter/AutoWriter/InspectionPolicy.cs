using System;

namespace GrblPlotter.AutoWriter
{
    // Exact allowlist: no multiline payloads, reset, unlock, motion or settings writes.
    internal static class InspectionPolicy
    {
        internal static bool HasControllerRestart(string responses)
        {
            return responses != null && (responses.Contains(" RX rst:") ||
                responses.Contains(" RX ets Jul") || responses.Contains(" RX [MSG:Grbl_ESP32 Ver"));
        }

        internal static bool AllowsLine(string command, string approvedAxisStep = null)
        {
            if (command == null) return false;
            if (approvedAxisStep != null && (command.Trim() == approvedAxisStep || command.Trim() == "G90")) return true;
            switch (command.Trim())
            {
                case "?": case "$$": case "$G": case "$#": case "$I": case "$N":
                    return true;
                default: return false;
            }
        }
    }
}
