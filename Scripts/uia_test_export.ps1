param(
    [int]$ButtonIndex = 2,
    [string]$FileName = "XYMusic-export-test.log"
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$kwLog = [char]0x65E5 + [char]0x5FD7 + [char]0x8BBE + [char]0x7F6E   # 日志设置
$btnExport = [char]0x5BFC + [char]0x51FA                               # 导出
$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A                 # 另存为

# 找日志设置 expander
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

# 收集"导出"按钮, 点第 ButtonIndex 个
$btns = @()
foreach ($el in $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq $btnExport) { $btns += $el }
}
if ($btns.Count -lt 3) { Write-Output "Export buttons not found (count=$($btns.Count))"; exit 1 }
$target = $btns[$ButtonIndex]
Write-Output "Invoking export button #$ButtonIndex ..."
$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

# 等待应用内另存为窗口
$dlg = $null
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    $dialogs = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $dialogs += $el }
    }
    if ($dialogs.Count -gt 0) { $dlg = $dialogs[$dialogs.Count - 1]; break }
    # 也检查系统级新窗口
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($w.Current.Name -match "Save As|$dlgTitle") { $dlg = $w; break }
    }
    if ($dlg) { break }
}
if (-not $dlg) { Write-Output "Save dialog not found"; exit 1 }
Write-Output "Save dialog found"

# 文件名输入框: Win32 对话框里 ControlType 是 Pane, 按 Class='Edit' + AId='1001' 找
$edit = $null
foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, "Edit")))) {
    if ($e.Current.AutomationId -eq "1001" -or $e.Current.Name -notmatch "SearchBox") { $edit = $e; break }
}
if (-not $edit) { Write-Output "Filename edit not found"; exit 1 }
try {
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($FileName)
    Write-Output "Filename set: '$($edit.Current.Name)'"
} catch { Write-Output "SetValue failed: $($_.Exception.Message)"; exit 1 }
Start-Sleep -Seconds 1

# 保存按钮: Win32 按钮也是 Pane, 按 AId='1' + Class='Button' 找
$save = $null
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "1")))) {
    if ($b.Current.ClassName -eq "Button") { $save = $b; break }
}
if (-not $save) { Write-Output "Save button not found"; exit 1 }
try {
    $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "Save clicked: '$($save.Current.Name)'"
} catch { Write-Output "Save invoke failed: $($_.Exception.Message)"; exit 1 }
Start-Sleep -Seconds 5

# 验证导出文件 (对话框可能在 Documents 或 Downloads)
$out = "$env:USERPROFILE\Documents\$FileName"
if (-not (Test-Path $out)) {
    $alt = "$env:USERPROFILE\Downloads\$FileName"
    if (Test-Path $alt) { $out = $alt }
}
if (Test-Path $out) {
    $len = (Get-Item $out).Length
    $content = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    $allLines = ($content -split "`r?`n" | Where-Object { $_ -ne "" }).Count
    $inf = ([regex]::Matches($content, "\[INF\]")).Count
    $err = ([regex]::Matches($content, "\[ERR\]")).Count
    $wrn = ([regex]::Matches($content, "\[WRN\]")).Count
    $ftl = ([regex]::Matches($content, "\[FTL\]")).Count
    Write-Output "EXPORTED: $out ($len bytes)"
    Write-Output "Lines: $allLines  INF: $inf  ERR: $err  WRN: $wrn  FTL: $ftl"
    Write-Output "--- first 3 ---"
    ($content -split "`r?`n" | Where-Object { $_ -ne "" } | Select-Object -First 3)
} else { Write-Output "Export file NOT found at $out" }

# 若保存失败对话框仍残留, 用 WM_CLOSE 兜底关闭
$sig = '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);'
Add-Type -MemberDefinition $sig -Name U32X -Namespace NativeX
$dlgTitle2 = $dlgTitle
$left = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle2) { $left += $el }
}
if ($left.Count -gt 0) {
    Write-Output "Dialog still open ($($left.Count)), closing via WM_CLOSE"
    $seen = @{}
    foreach ($d in $left) {
        $hwnd = [IntPtr]$d.Current.NativeWindowHandle
        if ($hwnd -eq [IntPtr]::Zero -or $seen.ContainsKey($hwnd)) { continue }
        $seen[$hwnd] = $true
        [NativeX.U32X]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    Start-Sleep -Seconds 2
}
