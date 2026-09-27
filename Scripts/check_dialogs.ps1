Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class W {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public static long FindMain(int pid) {
        long f = 0;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (f != 0) return false;
            uint wp; GetWindowThreadProcessId(h, out wp);
            if (wp == (uint)pid) {
                var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
                var cl = new StringBuilder(64); GetClassName(h, cl, 64);
                if (tx.ToString() == "XY Music" && cl.ToString() == "WinUIDesktopWin32WindowClass") f = h.ToInt64();
            }
            return true;
        }, IntPtr.Zero);
        return f;
    }
    public static List<long> Find32770(int pid) {
        var r = new List<long>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint wp; GetWindowThreadProcessId(h, out wp);
            if (wp == (uint)pid) {
                var cl = new StringBuilder(64); GetClassName(h, cl, 64);
                if (cl.ToString() == "#32770") { var tx = new StringBuilder(256); GetWindowText(h, tx, 256); r.Add(h.ToInt64()); }
            }
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
"@
$proc = Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1
if (-not $proc) { Write-Output "ERR: no process"; exit 1 }
$mainH = [W]::FindMain($proc.Id)
if ($mainH -eq 0) { Write-Output "ERR: no main window"; exit 1 }
Write-Output ("main window: 0x" + $mainH.ToString("X"))
$dlgs = [W]::Find32770($proc.Id)
Write-Output ("#32770 dialogs: " + $dlgs.Count)

$win = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$mainH)
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, "ContentDialog")
$content = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $clsCond)
if ($content) {
    Write-Output "ContentDialog PRESENT"
    $txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $content.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)) {
        Write-Output ("  TEXT: " + $t.Current.Name)
    }
    $cbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CloseButton")
    $btn = $content.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
    if ($btn) {
        $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Write-Output "ContentDialog closed"
    }
} else {
    Write-Output "ContentDialog: none"
}
