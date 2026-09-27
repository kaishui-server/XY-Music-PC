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
public class DlgHelper2 {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    public static List<IntPtr> FindByTitle(string title) {
        var r = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
            if (tx.ToString() == title) r.Add(h);
            return true;
        }, IntPtr.Zero);
        return r;
    }
    public static List<IntPtr> ChildrenByClass(IntPtr hwnd, string cls) {
        var r = new List<IntPtr>();
        EnumChildWindows(hwnd, delegate(IntPtr c, IntPtr ll) {
            var cn = new StringBuilder(256); GetClassName(c, cn, 256);
            if (cn.ToString() == cls) r.Add(c);
            return true;
        }, IntPtr.Zero);
        return r;
    }
    public static List<string> AllChildren(IntPtr hwnd) {
        var r = new List<string>();
        EnumChildWindows(hwnd, delegate(IntPtr c, IntPtr ll) {
            var cn = new StringBuilder(256); GetClassName(c, cn, 256);
            var t2 = new StringBuilder(256); GetWindowText(c, t2, 256);
            r.Add(String.Format("HWND={0} Class='{1}' Text='{2}' ID={3}", c, cn, t2, GetDlgCtrlID(c)));
            return true;
        }, IntPtr.Zero);
        return r;
    }
    public static uint PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
    public static string Text(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
}
"@
Add-Type -TypeDefinition $cs -Language CSharp

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }
$proc = Get-Process -Name "*XY Music*" | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1

$kwLog = [char]0x65E5 + [char]0x5FD7 + [char]0x8BBE + [char]0x7F6E
$btnExport = [char]0x5BFC + [char]0x51FA
$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A

function Close-LeftDialogs {
    $hs = [DlgHelper2]::FindByTitle($dlgTitle)
    foreach ($h in $hs) { [DlgHelper2]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
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

# 等待 #32770 对话框 (系统级)
$dlgHwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    foreach ($h in [DlgHelper2]::FindByTitle($dlgTitle)) {
        if ([DlgHelper2]::PidOf($h) -eq $proc.Id) { $dlgHwnd = $h; break }
    }
    if ($dlgHwnd -eq [IntPtr]::Zero) {
        foreach ($h in [DlgHelper2]::FindByTitle($dlgTitle)) { $dlgHwnd = $h; break }
    }
    if ($dlgHwnd -ne [IntPtr]::Zero) { break }
}
if ($dlgHwnd -eq [IntPtr]::Zero) { Write-Output "Dialog not found"; exit 1 }
$dlgPid = [DlgHelper2]::PidOf($dlgHwnd)
$dlgProc = (Get-Process -Id $dlgPid -ErrorAction SilentlyContinue).ProcessName
Write-Output "Dialog HWND=$dlgHwnd PID=$dlgPid ($dlgProc)"

# dump 全部子窗口(前 40 个)
$all = [DlgHelper2]::AllChildren($dlgHwnd)
Write-Output "Child windows: $($all.Count)"

# 找文件名 Edit
$edits = [DlgHelper2]::ChildrenByClass($dlgHwnd, "Edit")
Write-Output "Edit children: $($edits.Count)"
if ($edits.Count -eq 0) {
    # 也检查所有 #32770 子对话框
    $subs = [DlgHelper2]::ChildrenByClass($dlgHwnd, "#32770")
    Write-Output "#32770 children: $($subs.Count)"
    foreach ($s in $subs) {
        $se = [DlgHelper2]::ChildrenByClass($s, "Edit")
        Write-Output "  sub $s -> $($se.Count) edits"
        if ($se.Count -gt 0 -and $edits.Count -eq 0) { $dlgHwnd = $s; $edits = $se }
    }
}

if ($edits.Count -eq 0) {
    Write-Output "No filename edit found — aborting"
    Close-LeftDialogs
    exit 1
}
# 取包含 .log 的或第一个
$targetEdit = [IntPtr]::Zero
foreach ($e in $edits) {
    if ([DlgHelper2]::Text($e) -match "\.log") { $targetEdit = $e; break }
}
if ($targetEdit -eq [IntPtr]::Zero) {
    foreach ($e in $edits) { $targetEdit = $e; break }
}
if ($targetEdit -eq [IntPtr]::Zero) { Write-Output "No target edit"; Close-LeftDialogs; exit 1 }
Write-Output "Target edit: $targetEdit"

[DlgHelper2]::SendMessage($targetEdit, 0x000C, [IntPtr]::Zero, $FileName) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output "Filename set to: '$([DlgHelper2]::Text($targetEdit))'"

# WM_COMMAND IDOK=1
[DlgHelper2]::PostMessage($dlgHwnd, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null
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
    Write-Output "--- first 3 ---"
    ($content -split "`r?`n" | Where-Object { $_ -ne "" } | Select-Object -First 3)
} else { Write-Output "Export file NOT found" }

Close-LeftDialogs
