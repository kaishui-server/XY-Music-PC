Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 找插件管理页的 ListView (含 ToggleSwitch 的行)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output "Total elements: $($all.Count)"
$types = @{}
foreach ($el in $all) {
    $t = $el.Current.ControlType.ProgrammaticName -replace "ControlType\.", ""
    if (-not $types.ContainsKey($t)) { $types[$t] = 0 }
    $types[$t]++
}
Write-Output "=== Control type counts ==="
$types.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Output "$($_.Key): $($_.Value)" }

# Toggle 模式可用的元素
Write-Output "=== Elements with TogglePattern ==="
$i = 0
foreach ($el in $all) {
    $tp = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $cell = $walker.GetParent($el); $row = $walker.GetParent($cell)
        $plat = ""
        if ($row) {
            $tb = $row.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
            if ($tb) { $plat = $tb.Current.Name }
        }
        Write-Output ("#{0}: Type={1} Name='{2}' State={3} Row='{4}'" -f $i, ($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''), $el.Current.Name, $tp.Current.ToggleState, $plat)
        $i++
    }
}
