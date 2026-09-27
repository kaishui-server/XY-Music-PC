Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: main window not found"; exit 1 }
Write-Output ("main win pid=" + $win.Current.ProcessId + " isoffscreen=" + $win.Current.IsOffscreen)

$cancel = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CancelPlayingDetailButton")))
Write-Output ("detail cancel btn present: " + ($cancel -ne $null))

$navName = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navName)))
Write-Output ("nav found: " + ($nav -ne $null))

$favName = -join @(0x6536, 0x85CF | ForEach-Object { [char]$_ })
$fav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $favName)))
Write-Output ("fav tab found: " + ($fav -ne $null))

$btns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")))
Write-Output ("CoverThumbnail count: " + $btns.Count)

# window title bars / current page hint: find first few named elements
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$i = 0
foreach ($it in $items) {
    if ($i -ge 25) { break }
    $n = $it.Current.Name
    if ($n) {
        $line = "el: type=" + $it.Current.ControlType.LocalizedControlType + " name='" + $n + "'"
        if ($line.Length -gt 90) { $line = $line.Substring(0, 90) }
        Write-Output $line
        $i++
    }
}
