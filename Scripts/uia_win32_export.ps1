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
public class DlgHelper {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

    public static List<IntPtr> FindChildren(IntPtr parent, string className) {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            if (sb.ToString() == className) result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static string GetText(IntPtr h) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, 512);
        return sb.ToString();
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

# 关闭可能残留的对话框
function Close-LeftDialogs {
    $left = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $left += $el }
    }
    if ($left.Count -gt 0) {
        $seen = @{}
        foreach ($d in $left) {
            $h = [IntPtr]$d.Current.NativeWindowHandle
            if ($h -eq [IntPtr]::Zero -or $seen.ContainsKey($h)) { continue }
            $seen[$h] = $true
            [DlgHelper]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }
        Start-Sleep -Seconds 2
    }
}
Close-LeftDialogs

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
Write-Output "Invoking export button #$ButtonIndex"
$btns[$ButtonIndex].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

# 等待对话框
$dlg = $null
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $dialogs = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $dialogs += $el }
    }
    if ($dialogs.Count -gt 0) { $dlg = $dialogs[$dialogs.Count - 1]; break }
}
if (-not $dlg) { Write-Output "Save dialog not found"; exit 1 }
$dlgHwnd = [IntPtr]$dlg.Current.NativeWindowHandle
Write-Output "Dialog HWND: $dlgHwnd"

# Win32: 找文件名 Edit 子窗口, WM_SETTEXT 设置文件名
$edits = [DlgHelper]::FindChildren($dlgHwnd, "Edit")
Write-Output "Edit children: $($edits.Count)"
$targetEdit = [IntPtr]::Zero
foreach ($e in $edits) {
    $id = [DlgHelper]::GetDlgCtrlID($e)
    $txt = [DlgHelper]::GetText($e)
    Write-Output "  Edit HWND=$e ID=$id Text='$txt'"
    if ($txt -match "\.log") { $targetEdit = $e }
}
if ($edits.Count -gt 0 -and $targetEdit -eq [IntPtr]::Zero) { $targetEdit = $edits[0] }
if ($targetEdit -eq [IntPtr]::Zero) { Write-Output "Filename edit not found"; Close-LeftDialogs; exit 1 }
[DlgHelper]::SendMessage($targetEdit, 0x000C, [IntPtr]::Zero, $FileName) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output "Filename now: '$([DlgHelper]::GetText($targetEdit))'"

# WM_COMMAND IDOK=1 触发保存
[DlgHelper]::PostMessage($dlgHwnd, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null
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
