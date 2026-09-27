param([int]$Count = 8, [int]$WaitSec = 10)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

for ($i = 1; $i -le $Count; $i++) {
    $next = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButtonPlayingDetail")))
    if ($next) {
        $inv = $null
        if ($next.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "[$i] next invoked" }
        else { Write-Output "[$i] ERR: NextMusicButtonPlayingDetail no InvokePattern" }
    } else {
        Write-Output "[$i] ERR: NextMusicButtonPlayingDetail not found (trying bottom bar)"
        $next2 = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButton")))
        if ($next2) {
            $inv2 = $null
            if ($next2.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv2)) { $inv2.Invoke(); Write-Output "[$i] bottom bar next invoked" }
        }
    }
    Start-Sleep -Seconds $WaitSec
    $title = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "PlayingDetailTitleTextBlock")))
    Write-Output ("[$i] now playing: '" + $title.Current.Name + "'")
}
Write-Output "LOOP DONE"
