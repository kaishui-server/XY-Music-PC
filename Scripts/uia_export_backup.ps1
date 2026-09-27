param(
    [string]$FileName = "xy_music_backup_export_test.json",
    [int]$WaitExportSec = 120
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# 中文字面量用字符码拼接, 避免编码问题
$kwExportTitle = -join @([char]0x5BFC, [char]0x51FA, [char]0x5907, [char]0x4EFD)                    # 导出备份
$kwPickSaveBtn = -join @([char]0x9009, [char]0x62E9, [char]0x4FDD, [char]0x5B58, [char]0x4F4D, [char]0x7F6E) # 选择保存位置
$kwDlgTitle    = -join @([char]0x53E6, [char]0x5B58, [char]0x4E3A)                                   # 另存为
$kwDone        = -join @([char]0x5BFC, [char]0x51FA, [char]0x5B8C, [char]0x6210)                     # 导出完成
$kwFailed      = -join @([char]0x5BFC, [char]0x51FA, [char]0x5907, [char]0x4EFD, [char]0x5931, [char]0x8D25) # 导出备份失败

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
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    public static List<long> FindDialogs(int pid, string title) {
        var r = new List<long>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint wpid; GetWindowThreadProcessId(h, out wpid);
            if (wpid == (uint)pid) {
                var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
                var cl = new StringBuilder(64); GetClassName(h, cl, 64);
                if (tx.ToString() == title && cl.ToString() == "#32770") r.Add(h.ToInt64());
            }
            return true;
        }, IntPtr.Zero);
        return r;
    }
    // MainWindowHandle 可能指向桌面歌词等子窗口, 按标题+类名找真正主窗口
    public static long FindMainWindow(int pid) {
        long found = 0;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (found != 0) return false;
            uint wpid; GetWindowThreadProcessId(h, out wpid);
            if (wpid == (uint)pid) {
                var tx = new StringBuilder(256); GetWindowText(h, tx, 256);
                var cl = new StringBuilder(64); GetClassName(h, cl, 64);
                if (tx.ToString() == "XY Music" && cl.ToString() == "WinUIDesktopWin32WindowClass") found = h.ToInt64();
            }
            return true;
        }, IntPtr.Zero);
        return found;
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
    public static void ClickOk(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0111, new IntPtr(1), IntPtr.Zero); }
    public static void CloseDlg(long hwnd) { PostMessage(new IntPtr(hwnd), 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
"@
Add-Type -TypeDefinition $cs -Language CSharp

$proc = Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1
if (-not $proc) { Write-Output "ERR: process not found"; exit 1 }
$mainH = [DlgX]::FindMainWindow($proc.Id)
if ($mainH -eq 0) { Write-Output "ERR: main window not found"; exit 1 }
$win = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$mainH)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }
Write-Output "window acquired (pid $($proc.Id), hwnd 0x$($mainH.ToString('X')))"

# 残留对话框清理
foreach ($h in [DlgX]::FindDialogs($proc.Id, $kwDlgTitle)) { [DlgX]::CloseDlg($h) }

# 1. 导航到设置
$aidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SettingsItem")
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $aidCond)
if (-not $item) { Write-Output "ERR: SettingsItem not found"; exit 1 }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3
Write-Output "settings nav OK"

# 2. 展开"导出备份" Expander
$expBtn = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.Name -eq $kwExportTitle) {
        $p = [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($el)
        if ($p -and $p.Current.ClassName -eq "Microsoft.UI.Xaml.Controls.Expander") { $expBtn = $p; break }
    }
}
if (-not $expBtn) { Write-Output "ERR: export expander header not found"; exit 1 }
$ecp = $expBtn.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
if ($ecp.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) { $ecp.Expand(); Start-Sleep -Seconds 2 }
Write-Output "export expander expanded"

# 3. 点击"选择保存位置"
$bcond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $kwPickSaveBtn)
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bcond)
if (-not $btn) { Write-Output "ERR: pick-save button not found"; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "pick-save button clicked"

# 4. 等待"另存为"对话框并处理
$dlgH = 0L
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $hs = [DlgX]::FindDialogs($proc.Id, $kwDlgTitle)
    if ($hs.Count -gt 0) { $dlgH = $hs[0]; break }
}
if ($dlgH -eq 0) { Write-Output "ERR: save dialog not found"; exit 1 }
Write-Output "save dialog handle: $dlgH"

$editH = [DlgX]::FindChildById($dlgH, 1001)
if ($editH -eq 0) { Write-Output "ERR: filename edit (ID 1001) not found"; [DlgX]::CloseDlg($dlgH); exit 1 }
[DlgX]::SetText($editH, $FileName)
Start-Sleep -Milliseconds 800
$cur = [DlgX]::GetText($editH)
Write-Output "filename set to: '$cur'"
if ($cur -ne $FileName) { Write-Output "WARN: filename mismatch, still proceeding" }
[DlgX]::ClickOk($dlgH)
Write-Output "IDOK posted, exporting..."

# 5. 等待导出结果 ContentDialog (跳过仅含 ProgressRing 的进度层)
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, "ContentDialog")
$cbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CloseButton")
$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$done = $false
for ($i = 0; $i -lt $WaitExportSec; $i++) {
    Start-Sleep -Seconds 1
    $content = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $clsCond)
    if ($content) {
        $texts = $content.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
        $joined = ($texts | ForEach-Object { $_.Current.Name }) -join " | "
        if ($joined.Contains($kwDone) -or $joined.Contains($kwFailed)) {
            Write-Output "result dialog detected after $($i+1)s"
            foreach ($t in $texts) { Write-Output ("  DIALOG: " + $t.Current.Name) }
            $closeBtn = $content.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
            if ($closeBtn) { $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "dialog closed" }
            $done = $true
            break
        }
    }
}
if (-not $done) { Write-Output "WARN: result dialog not detected in ${WaitExportSec}s" }

# 6. 验证导出文件
$target = "$env:USERPROFILE\Documents\$FileName"
if (Test-Path $target) {
    Write-Output "EXPORTED: $target ($((Get-Item $target).Length) bytes)"
} else {
    Write-Output "ERR: export file NOT found at $target"
    foreach ($dir in @("Downloads", "Desktop")) {
        $p = Join-Path $env:USERPROFILE "$dir\$FileName"
        if (Test-Path $p) { Write-Output "  but found at: $p" }
    }
}
Write-Output "DONE"
