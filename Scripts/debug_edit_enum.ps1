param([int]$ButtonIndex = 1)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$cs = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class DlgDbg {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    public delegate bool EnumProc2(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc2 cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static List<long> EditsOf(IntPtr hwnd) {
        var r = new List<long>();
        EnumChildWindows(hwnd, delegate(IntPtr c, IntPtr ll) {
            var cn = new StringBuilder(256); GetClassName(c, cn, 256);
            if (cn.ToString() == "Edit") r.Add(c.ToInt64());
            return true;
        }, IntPtr.Zero);
        return r;
    }
    public static string TextOf(long h) { var sb = new StringBuilder(512); GetWindowText(new IntPtr(h), sb, 512); return sb.ToString(); }
    public static int IdOf(long h) { return GetDlgCtrlID(new IntPtr(h)); }
    public static List<long> FindTitle(string title) {
        var r = new List<long>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
            if (tx.ToString() == title) r.Add(h.ToInt64());
            return true;
        }, IntPtr.Zero);
        return r;
    }
}
"@
Add-Type -TypeDefinition $cs -Language CSharp

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$kwLog = [char]0x65E5 + [char]0x5FD7 + [char]0x8BBE + [char]0x7F6E
$btnExport = [char]0x5BFC + [char]0x51FA
$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A

$script:panel = $null
function Walk($el) {
    if ($script:panel) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $kwLog)))
        if ($kw) { $script:panel = $el; return }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $win
$btns = @()
foreach ($el in $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq $btnExport) { $btns += $el }
}
Write-Output "Invoking export #$ButtonIndex"
$btns[$ButtonIndex].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$dlgH = 0L
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $hs = [DlgDbg]::FindTitle($dlgTitle)
    foreach ($h in $hs) { $dlgH = $h; break }
    if ($dlgH -ne 0) { break }
}
if ($dlgH -eq 0) { Write-Output "No dialog"; exit 1 }
Write-Output "Dialog handle: $dlgH"

$edits = [DlgDbg]::EditsOf($dlgH)
Write-Output "Edits type: $($edits.GetType().FullName)  Count: $($edits.Count)"
foreach ($e in $edits) {
    Write-Output ("edit: value={0} type={1} id={2} text='{3}'" -f $e, $e.GetType().FullName, [DlgDbg]::IdOf($e), [DlgDbg]::TextOf($e))
}
