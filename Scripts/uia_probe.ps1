Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("top-level windows: " + $wins.Count)
foreach ($w in $wins) {
    $n = $w.Current.Name
    $pid2 = $w.Current.ProcessId
    $ct = $w.Current.ControlType.LocalizedControlType
    if ($n -or $pid2 -ne 0) { Write-Output ("win: '" + $n + "' pid=" + $pid2 + " type=" + $ct) }
}
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, (Get-Process -Name "XY Music").Id)
$appWin = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $appWin) { Write-Output "ERR: app window not found by pid"; exit 1 }
Write-Output ("app win found: '" + $appWin.Current.Name + "'")
$kids = $appWin.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("direct children: " + $kids.Count)
foreach ($k in $kids) {
    $n = $k.Current.Name
    $aid = $k.Current.AutomationId
    $ct = $k.Current.ControlType.LocalizedControlType
    $line = "child: type=$ct id='$aid' name='" + $n + "'"
    if ($line.Length -gt 100) { $line = $line.Substring(0, 100) }
    Write-Output $line
}
# count nav items & CoverThumbnail & cancel button
$navName = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })
$nav = $appWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navName)))
Write-Output ("nav found: " + ($nav -ne $null))
$btns = $appWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")))
Write-Output ("CoverThumbnail count: " + $btns.Count)
