Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$cs = @"
using System;
using System.Runtime.InteropServices;
public class Mini {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@
Add-Type -TypeDefinition $cs -Language CSharp
Write-Output "C# compiled"

$prop = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
Write-Output ("prop null: " + ($null -eq $prop))
try {
    $cond = New-Object System.Windows.Automation.PropertyCondition($prop, "SettingsItem")
    Write-Output ("cond null: " + ($null -eq $cond))
} catch {
    Write-Output ("cond create failed: " + $_.Exception.Message)
}
