Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 切换到插件管理页
$navName = [string]([char]0x63D2 + [char]0x4EF6 + [char]0x7BA1 + [char]0x7406)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navName)))
if ($nav) { $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Write-Output "nav ok" }
Start-Sleep -Seconds 3

$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output "Total elements: $($all.Count)"

# 1) 所有带 TogglePattern 的 (插件开关)
$toggles = @()
foreach ($el in $all) {
    $tp = $null
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
        $toggles += @{ El = $el; Pattern = $tp; State = $tp.Current.ToggleState }
    }
}
Write-Output "Plugin toggles: $($toggles.Count)"
$i = 0
foreach ($t in $toggles) { Write-Output "  #$i = $($t.State)"; $i++ }

# 2) 页面顶部按钮的可用性 (IsBusy 诊断)
foreach ($el in $all) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $n = $el.Current.Name
        if ($n -and $n.Trim().Length -gt 0 -and $n -notmatch "^\s*$") {
            Write-Output "BTN '$n' IsEnabled=$($el.Current.IsEnabled)"
        }
    }
}
