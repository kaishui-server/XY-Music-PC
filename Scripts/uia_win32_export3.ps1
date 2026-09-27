param(
    [int]$ButtonIndex = 2,
    [string]$FileName = "XYMusic-export-test.log"
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$cs = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class DlgX {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);

    public static List<long> FindTitle(string title) {
        var r = new List<long>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
            if (tx.ToString() == title) r.Add(h.ToInt64());
            return true;
        }, IntPtr.Zero);
        return r;
    }
    public static long FindChildById(long hwnd, int ctrlId) {
        long found = 0;
        EnumChildWindows(new IntPtr(hwnd), delegate(IntPtr c, IntPtr ll) {
            if (found == 0 && GetDlgCtrlID(c) == ctrlId) found = c.ToInt64();
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static void SetText(long hwnd, string text) { SendMessage(new IntPtr(hwnd), 0x000C, IntPtr.Zero, text); }
    public static string GetText(long hwnd) { var sb = new StringBuilder(512); GetWindowText(new IntPtr(hwnd), sb, 512); return sb.ToString(); }
    public static void Close(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0010, IntPtr.Zero, IntPtr.Zero); }
    public static void ClickOk(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0111, new IntPtr(1), IntPtr.Zero); }
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

function Close-LeftDialogs {
    $hs = [DlgX]::FindTitle($dlgTitle)
    foreach ($h in $hs) { [DlgX]::Close($h) }
    if ($hs.Count -gt 0) { Start-Sleep -Seconds 2; Write-Output "Closed $($hs.Count) leftover dialog(s)" }
}

# 找日志设置 expander 与导出按钮
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
if (-not $script:panel) { Write-Output "Expander not found"; exit 1 }
$ec = $script:panel.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
if ($ec.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) { $ec.Expand(); Start-Sleep -Seconds 2 }

$btns = @()
foreach ($el in $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq $btnExport) { $btns += $el }
}
if ($btns.Count -lt 3) { Write-Output "Export buttons not found"; exit 1 }
Close-LeftDialogs
Write-Output "Invoking export button #$ButtonIndex"
$btns[$ButtonIndex].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

# 等待对话框
$dlgH = 0L
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $hs = [DlgX]::FindTitle($dlgTitle)
    foreach ($h in $hs) { $dlgH = $h; break }
    if ($dlgH -ne 0) { break }
}
if ($dlgH -eq 0) { Write-Output "Dialog not found"; exit 1 }
Write-Output "Dialog handle: $dlgH"

# 文件名 Edit: 控件 ID 1001
$editH = [DlgX]::FindChildById($dlgH, 1001)
if ($editH -eq 0) { Write-Output "Filename edit (ID 1001) not found"; Close-LeftDialogs; exit 1 }
Write-Output "Filename edit: $editH"
[DlgX]::SetText($editH, $FileName)
Start-Sleep -Milliseconds 800
$cur = [DlgX]::GetText($editH)
Write-Output "Edit text now: '$cur' (expected: '$FileName')"
if ($cur -ne $FileName) { Write-Output "WARNING: filename mismatch" }

# WM_COMMAND IDOK=1 保存
[DlgX]::ClickOk($dlgH)
Write-Output "IDOK posted"
Start-Sleep -Seconds 6

# 验证导出文件
$found = $null
foreach ($dir in @("Documents", "Downloads", "Desktop")) {
    $p = Join-Path $env:USERPROFILE "$dir\$FileName"
    if (Test-Path $p) { $found = $p; break }
}
if ($found) {
    $len = (Get-Item $found).Length
    $content = [System.IO.File]::ReadAllText($found, [System.Text.Encoding]::UTF8)
    $allLines = ($content -split "`r?`n" | Where-Object { $_ -ne "" }).Count
    $inf = ([regex]::Matches($content, "\[INF\]")).Count
    $err = ([regex]::Matches($content, "\[ERR\]")).Count
    $wrn = ([regex]::Matches($content, "\[WRN\]")).Count
    $ftl = ([regex]::Matches($content, "\[FTL\]")).Count
    Write-Output "EXPORTED: $found ($len bytes)"
    Write-Output "Lines: $allLines  INF: $inf  ERR: $err  WRN: $wrn  FTL: $ftl"
    Write-Output "--- first 2 ---"
    ($content -split "`r?`n" | Where-Object { $_ -ne "" } | Select-Object -First 2)
} else { Write-Output "Export file NOT found" }

Close-LeftDialogs
