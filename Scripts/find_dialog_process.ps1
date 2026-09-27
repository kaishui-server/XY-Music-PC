Add-Type -AssemblyName UIAutomationClient

$cs = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class FindDlg {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    public static List<string> Find(string title) {
        var lines = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
            if (tx.ToString() == title) {
                uint p; GetWindowThreadProcessId(h, out p);
                var cn = new StringBuilder(256); GetClassName(h, cn, 256);
                lines.Add(String.Format("HWND={0} PID={1} Class='{2}'", h, p, cn));
            }
            return true;
        }, IntPtr.Zero);
        return lines;
    }
    public static List<string> Children(IntPtr hwnd) {
        var lines = new List<string>();
        EnumChildWindows(hwnd, delegate(IntPtr c, IntPtr ll) {
            var cn = new StringBuilder(256); GetClassName(c, cn, 256);
            var t2 = new StringBuilder(256); GetWindowText(c, t2, 256);
            lines.Add(String.Format("HWND={0} Class='{1}' Text='{2}'", c, cn, t2));
            return true;
        }, IntPtr.Zero);
        return lines;
    }
}
"@
Add-Type -TypeDefinition $cs -Language CSharp

$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A
$found = [FindDlg]::Find($dlgTitle)
Write-Output "Dialogs: $($found.Count)"
foreach ($f in $found) { Write-Output $f }
if ($found.Count -gt 0) {
    $first = $found[0] -replace "HWND=(\d+).*", '$1'
    $hwnd = [IntPtr][long]$first
    $children = [FindDlg]::Children($hwnd)
    Write-Output "Children of first dialog: $($children.Count)"
    $children | Where-Object { $_ -match "Edit|#32770|Button" } | Select-Object -First 20
}
