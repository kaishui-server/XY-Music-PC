Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement

function Find-AppWindow {
    param($TimeoutSec = 20)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $ws = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $ws) {
            $p = Get-Process -Id $w.Current.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $p -and $p.ProcessName -eq "OriginalSound HIFI Player") { return $w }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

$win = Find-AppWindow
if (-not $win) { "WINDOW NOT FOUND"; exit 1 }
"window found"

# 先导航到插件管理页
$pluginName = -join @([char]0x63D2, [char]0x4EF6, [char]0x7BA1, [char]0x7406)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $pluginName)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
    elseif ($nav.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$sel)) { $sel.Invoke() }
    "nav to plugin page done"
} else {
    "nav item not found"
}
Start-Sleep -Seconds 3

# 重新获取窗口(导航后树变化)
$win = Find-AppWindow
if (-not $win) { "WINDOW LOST after nav"; exit 1 }

# 遍历查找支持 TogglePattern 的元素
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
"total descendants: $($all.Count)"
$toggleTargets = @()
foreach ($el in $all) {
    $tp = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
        $toggleTargets += $el
    }
}
"toggle-pattern elements: $($toggleTargets.Count)"
foreach ($t in $toggleTargets) {
    "  -> name=[" + $t.Current.Name + "] type=" + $t.Current.ControlType.ProgrammaticName
}

if ($toggleTargets.Count -gt 0) {
    $target = $toggleTargets[0]
    $tp = $null
    $target.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp) | Out-Null
    $before = $tp.Current.ToggleState
    $tp.Toggle()
    Start-Sleep -Seconds 4
    $after = $tp.Current.ToggleState
    "toggled: $before -> $after"
    $tp.Toggle()
    Start-Sleep -Seconds 4
    $final = $tp.Current.ToggleState
    "toggled back: $after -> $final"
} else {
    "no toggle targets"
}

$p = Get-Process -Name "OriginalSound HIFI Player" -ErrorAction SilentlyContinue
if ($p) { "APP ALIVE after toggle test" } else { "APP CRASHED after toggle test" }
