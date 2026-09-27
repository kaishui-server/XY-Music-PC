param([string]$Keyword = "日志设置")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$script:panel = $null
function Walk($el) {
    if ($script:panel) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Keyword)))
        if ($kw) { $script:panel = $el; return }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $win
if (-not $script:panel) { Write-Output "Expander not found"; exit 1 }

$desc = $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$clearBtn = $null
foreach ($el in $desc) {
    $ip = $null
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $el.Current.Name -eq "清空" -and
        $el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) {
        $clearBtn = $el; break
    }
}
if (-not $clearBtn) { Write-Output "Clear button not found"; exit 1 }
Write-Output "Clear button found, invoking..."
$clearBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3

# 查找确认对话框 (ContentDialog)
$dlgCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Pane)
Write-Output "--- Looking for confirm dialog ---"
$dialog = $null
# ContentDialog 的按钮通常在窗口子树里, 找名为"清空"的主按钮(不同于设置页的清空按钮, 对话框里的是 PrimaryButton)
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq "清空") {
        # 排除设置页的按钮(它在 expander 内), 对话框按钮的父链不同; 简单方式: 找到第二个清空按钮
        $script:count = 0
    }
}
# 列出所有"清空"按钮供调试
$i = 0
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "清空")))) {
    Write-Output "Button '清空' #$i Type=$($el.Current.ControlType.ProgrammaticName)"
    $i++
}
