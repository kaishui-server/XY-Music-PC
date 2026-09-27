Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A
$dialogs = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $dialogs += $el }
}
Write-Output "Save dialogs open: $($dialogs.Count)"
if ($dialogs.Count -eq 0) { exit 0 }
$dlg = $dialogs[$dialogs.Count - 1]

$cancel = $null
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "2")))) {
    if ($b.Current.ClassName -eq "Button") { $cancel = $b; break }
}
if (-not $cancel) { Write-Output "Cancel (AId=2) not found"; exit 1 }
Write-Output "Cancel found: Name='$($cancel.Current.Name)' Type=$($cancel.Current.ControlType.ProgrammaticName)"

# Try LegacyIAccessible DoDefaultAction
$liaPatternId = [System.Windows.Automation.AutomationElement]::LegacyIAccessiblePattern
$lia = $null
try { $lia = $cancel.GetCurrentPattern($liaPatternId) } catch { Write-Output "GetPattern failed: $($_.Exception.Message)" }
if ($lia) {
    try { $lia.DoDefaultAction(); Write-Output "DoDefaultAction OK" }
    catch { Write-Output "DoDefaultAction failed: $($_.Exception.Message)" }
} else {
    Write-Output "LegacyIAccessible pattern unavailable"
}
Start-Sleep -Seconds 3

$rest = 0
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $rest++ }
}
Write-Output "Save dialogs remaining: $rest"
