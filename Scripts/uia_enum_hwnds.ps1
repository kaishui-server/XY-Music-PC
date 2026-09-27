Add-Type -AssemblyName UIAutomationClient

$cs = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class HwndDump {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    public static List<string> DumpFor(uint pid) {
        var lines = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid) {
                var cn = new StringBuilder(256); GetClassName(h, cn, 256);
                var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
                lines.Add(String.Format("TOP HWND={0} Class='{1}' Title='{2}' Visible={3}", h, cn, tx, IsWindowVisible(h)));
                EnumChildWindows(h, delegate(IntPtr c, IntPtr ll) {
                    var c2 = new StringBuilder(256); GetClassName(c, c2, 256);
                    var t2 = new StringBuilder(256); GetWindowText(c, t2, 256);
                    lines.Add(String.Format("  CHILD HWND={0} Class='{1}' Title='{2}' Parent={3}", c, c2, t2, GetParent(c)));
                    return true;
                }, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return lines;
    }
}
"@
Add-Type -TypeDefinition $cs -Language CSharp

$proc = Get-Process -Name "*XY Music*" | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Output "App not found"; exit 1 }
Write-Output "PID: $($proc.Id)"
$lines = [HwndDump]::DumpFor($proc.Id)
Write-Output "Total windows: $($lines.Count)"
$lines | Where-Object { $_ -match "TOP|Edit|Button.*保存|#32770" } | Select-Object -First 60
