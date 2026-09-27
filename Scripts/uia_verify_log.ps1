param([string]$Keyword = "日志设置")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 递归找包含关键词的 Expander, 再在其中找 ComboBox / ToggleSwitch / Button
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
Write-Output "=== Controls inside '$Keyword' expander ==="

$desc = $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($el in $desc) {
    $t = $el.Current.ControlType
    $aid = $el.Current.AutomationId
    $name = $el.Current.Name
    if ($t -eq [System.Windows.Automation.ControlType]::ComboBox -or
        $t -eq [System.Windows.Automation.ControlType]::CheckBox -or
        $t -eq [System.Windows.Automation.ControlType]::Button -or
        $t -eq [System.Windows.Automation.ControlType]::ListItem) {
        $val = ""
        $vp = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { $val = " Value='$($vp.Current.Value)'" }
        $tg = ""
        $tp = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) { $tg = " Toggle=$($tp.Current.ToggleState)" }
        Write-Output ("[{0}] AId='{1}' Name='{2}'{3}{4}" -f ($t.ProgrammaticName -replace 'ControlType\.',''), $aid, $name, $val, $tg)
    }
}
