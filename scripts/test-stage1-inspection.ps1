$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $repoRoot 'GRBL-Plotter\AutoWriter\InspectionPolicy.cs'))
$bridge = @'
namespace GrblPlotter.AutoWriter {
    public static class InspectionTestBridge {
        public static bool HasRestart(string value) { return InspectionPolicy.HasControllerRestart(value); }
        public static bool AllowsPenUp(string value) { return InspectionPolicy.AllowsLine(value, "G1G90 Z0.0F10000"); }
        public static bool Allows(string value) { return InspectionPolicy.AllowsLine(value); }
        public static bool AllowsStep(string value) { return InspectionPolicy.AllowsLine(value, "G91G21G94G1X1F60"); }
        public static bool AllowsPenTwoMm(string value) { return InspectionPolicy.AllowsLine(value, "G91G21G94G1Z2F30"); }
        public static bool AllowsPenStep(string value) { return InspectionPolicy.AllowsLine(value, "G91G21G94G1Z0.2F30"); }
    }
}
'@
Add-Type -TypeDefinition ($source + $bridge)
if (![GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenUp('G1G90 Z0.0F10000')) { throw 'Original absolute pen-up rejected.' }
foreach ($command in @('G1G90 Z5.0F10000', 'G1G90 Z0.0F300', 'G91G1Z0F10000', 'G0X0Y0', "G1G90 Z0.0F10000`nG1Z5")) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenUp($command)) { throw "Unselected absolute pen command allowed: $command" }
}
Write-Output 'PASS: selected original absolute pen-up excludes pen-down, other speed, relative moves and XY.'
foreach ($response in @('2026 RX rst:0x1 (POWERON_RESET)', '2026 RX ets Jul 29 2019', '2026 RX [MSG:Grbl_ESP32 Ver 1.3a]')) {
    if (![GrblPlotter.AutoWriter.InspectionTestBridge]::HasRestart($response)) { throw 'Controller restart not detected.' }
}
foreach ($response in @($null, '2026 RX <Idle|MPos:0,0,0>', '2026 RX [VER:1.3a.20221114:]')) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::HasRestart($response)) { throw 'Normal response falsely identified as restart.' }
}
Write-Output 'PASS: ESP32 restart messages detected; ordinary status and firmware queries remain allowed.'
foreach ($command in @('?', '$$', '$G', '$#', '$I', '$N', (" `r`n" + '$$' + " `r`n"))) {
    if (![GrblPlotter.AutoWriter.InspectionTestBridge]::Allows($command)) { throw "Rejected query: $command" }
}
foreach ($command in @('$X', '$H', '$10=2', 'G1 X1', 'M3 S5', 'M5', ('$$' + "`nG1 X1"), ("?`n" + '$X'), [string][char]24, '~', '!', '', $null)) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::Allows($command)) { throw "Allowed write or motion: $command" }
}
Write-Output 'PASS: inspection queries allowed; settings, motion, reset and multiline payloads rejected.'

foreach ($command in @('G91G21G94G1X1F60','G90')) {
    if (![GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsStep($command)) { throw "Approved step rejected: $command" }
}
foreach ($command in @('G91G21G94G1X2F60','G91G21G94G1Z1F60','G91G21G94G1Y1F60','M3 S100','$X','$10=2')) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsStep($command)) { throw "Unselected motion allowed: $command" }
}
Write-Output 'PASS: explicit one-axis step does not allow other distance, axis, pen, unlock or settings commands.'
foreach ($command in @('G91G21G94G1Z0.2F30','G90')) {
    if (![GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenStep($command)) { throw "Selected pen step rejected: $command" }
}
foreach ($command in @('G91G21G94G1Z5F30','G91G21G94G1Z0.2F300','G91G21G94G1X0.2F30','M3 S100','$X')) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenStep($command)) { throw "Unselected pen motion allowed: $command" }
}
Write-Output 'PASS: selected pen micro-step rejects larger distance, speed, other axis and accessory commands.'

foreach ($command in @('G91G21G94G1Z2F30','G90')) {
    if (![GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenTwoMm($command)) { throw "Selected 2mm step rejected: $command" }
}
foreach ($command in @('G91G21G94G1Z5F30','G91G21G94G1Z2F300','G91G21G94G1X2F30','M3 S100','$X')) {
    if ([GrblPlotter.AutoWriter.InspectionTestBridge]::AllowsPenTwoMm($command)) { throw "Unselected 2mm motion allowed: $command" }
}
Write-Output 'PASS: selected Z 2mm step rejects other distance, speed, axis and accessories.'
