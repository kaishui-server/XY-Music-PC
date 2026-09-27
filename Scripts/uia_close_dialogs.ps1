$wsh = New-Object -ComObject WScript.Shell
$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A
$activated = $wsh.AppActivate($dlgTitle)
Write-Output "AppActivate: $activated"
Start-Sleep -Milliseconds 500
$wsh.SendKeys("{ESC}")
Write-Output "ESC sent"
Start-Sleep -Seconds 2

Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$rest = 0
if ($win) {
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $rest++ }
    }
}
Write-Output "Save dialogs remaining: $rest"
