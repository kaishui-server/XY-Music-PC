Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$prop = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
Write-Output ("prop is null: " + ($null -eq $prop))
$cond = New-Object System.Windows.Automation.PropertyCondition($prop, "SettingsItem")
Write-Output ("cond is null: " + ($null -eq $cond))
$proc = Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1
Write-Output ("pid: " + $proc.Id)
$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
Write-Output ("win: " + ($win -ne $null) + " name=" + $win.Current.Name)
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Output ("SettingsItem found: " + ($item -ne $null))
