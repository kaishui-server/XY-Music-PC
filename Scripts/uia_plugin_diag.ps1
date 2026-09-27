param([string]$Action = "state")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 导航到插件管理
$navName = [string]([char]0x63D2 + [char]0x4EF6 + [char]0x7BA1 + [char]0x7406)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navName)))
if ($nav) { $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Write-Output "nav ok" }
else { Write-Output "nav NOT found (maybe already on page)" }
Start-Sleep -Seconds 3

# 列出所有 ToggleSwitch: 名字为 "启用/禁用" 或 Off/On, 按 UIA 顺序
$toggles = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::ToggleSwitch) {
        $tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        # 取所在行的平台名: 向上两层找 TextBlock
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $cell = $walker.GetParent($el); $row = $walker.GetParent($cell)
        $plat = ""
        if ($row) {
            $tb = $row.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
            if ($tb) { $plat = $tb.Current.Name }
        }
        $toggles += @{ El = $el; State = $tp.Current.ToggleState; Platform = $plat; Pattern = $tp }
    }
}
Write-Output "Toggles found: $($toggles.Count)"
$i = 0
foreach ($t in $toggles) {
    Write-Output ("#{0}: {1}  [{2}]" -f $i, $t.Platform, $t.State)
    $i++
}

if ($Action -eq "state") { exit 0 }

# toggle 指定索引
if ($Action -eq "toggle") {
    $idx = [int]$args[0]
    $state = $args[1]
    if ($idx -lt $toggles.Count) {
        $t = $toggles[$idx]
        $cur = $t.Pattern.Current.ToggleState
        Write-Output "Before: $($t.Platform) = $cur"
        if ("$cur" -ne $state) {
            $t.Pattern.Toggle()
            Write-Output "Toggled to $state"
        } else { Write-Output "Already $state" }
    }
}
