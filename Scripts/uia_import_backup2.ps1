param([int]$WaitImportSec = 180)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1
if (-not $proc) { Write-Output "ERR: process not found"; exit 1 }
$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }
Write-Output "window acquired via handle"

# 1. 导航到设置
$aidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SettingsItem")
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $aidCond)
if (-not $item) { Write-Output "ERR: SettingsItem not found"; exit 1 }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3
Write-Output "settings nav OK"

# 2. 展开"从手机版导入备份"折叠项(其文本的父级是 Expander 头部 Button)
$importTitle = -join @([char]0x4ECE, [char]0x624B, [char]0x673A, [char]0x7248, [char]0x5BFC, [char]0x5165, [char]0x5907, [char]0x4EFD)
$expBtn = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.Name -eq $importTitle) {
        $p = [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($el)
        if ($p -and $p.Current.ClassName -eq "Microsoft.UI.Xaml.Controls.Expander") { $expBtn = $p; break }
    }
}
if (-not $expBtn) { Write-Output "ERR: import expander header not found"; exit 1 }
$expBtn.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Seconds 2
Write-Output "expander expanded"

# 3. 找"选择备份文件"按钮
$btnName = -join @([char]0x9009, [char]0x62E9, [char]0x5907, [char]0x4EFD, [char]0x6587, [char]0x4EF6)
$bcond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $btnName)
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bcond)
if (-not $btn) { Write-Output "ERR: import button not found"; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "import button clicked"
Start-Sleep -Seconds 3

# 4. 处理"打开"文件对话框
$dlgName = -join @([char]0x6253, [char]0x5F00)
$dlg = $null
for ($i = 0; $i -lt 10; $i++) {
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgName) { $dlg = $el }
    }
    if ($dlg) { break }
    Start-Sleep -Seconds 1
}
if (-not $dlg) { Write-Output "ERR: open dialog not found"; exit 1 }
Write-Output "open dialog found"

$edit = $null
foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) {
    $edit = $e
}
if (-not $edit) { Write-Output "ERR: filename edit not found"; exit 1 }
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$env:USERPROFILE\Downloads\xymusic-20260926.json")
Start-Sleep -Seconds 1
Write-Output "filename set"

# 文件对话框是 Win32 #32770: UIA 按钮树常被截断, 用 WM_COMMAND(IDOK) 触发"打开"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Open {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);
    public static IntPtr FindDialog(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr lp) {
            uint wpid; GetWindowThreadProcessId(h, out wpid);
            if (wpid == (uint)pid) {
                var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64);
                if (sb.ToString() == "#32770") { found = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
$dlgHwnd = [Win32Open]::FindDialog($proc.Id)
if ($dlgHwnd -eq [IntPtr]::Zero) { Write-Output "ERR: #32770 dialog not found"; exit 1 }
[Win32Open]::PostMessage($dlgHwnd, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null
Write-Output "file selected (WM_COMMAND IDOK), importing..."

# 5. 等待导入完成: 结果对话框(ContentDialog)出现
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, "ContentDialog")
$cbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CloseButton")
$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
for ($i = 0; $i -lt $WaitImportSec; $i++) {
    Start-Sleep -Seconds 1
    $content = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $clsCond)
    if ($content) {
        Write-Output "result dialog detected after $($i+1)s"
        Start-Sleep -Seconds 2
        $texts = $content.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
        foreach ($t in $texts) { Write-Output ("  DIALOG: " + $t.Current.Name) }
        $closeBtn = $content.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
        if ($closeBtn) { $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "dialog closed" }
        break
    }
}
Write-Output "DONE"
