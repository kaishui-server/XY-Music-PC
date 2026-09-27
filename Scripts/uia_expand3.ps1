param(
    [string]$Keyword = "日志设置",
    [string]$Expand = "1"
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$script:target = $null

function Walk($el) {
    if ($script:target) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Keyword)))
        if ($kw) {
            $script:target = $el
            return
        }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}

Walk $win
if (-not $script:target) { Write-Output "Expander containing '$Keyword' not found"; exit 1 }

$p = $null
$script:target.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p) | Out-Null
Write-Output ("Found Expander: Name='{0}' State={1}" -f $script:target.Current.Name, $p.Current.ExpandCollapseState)
if ($Expand -eq "1" -and $p.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
    $p.Expand()
    Start-Sleep -Seconds 2
}
Write-Output ("Final state: {0}" -f $p.Current.ExpandCollapseState)
