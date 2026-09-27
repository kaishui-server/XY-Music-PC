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
Write-Output "Dialogs found: $($dialogs.Count)"
if ($dialogs.Count -eq 0) { exit 1 }
$dlg = $dialogs[$dialogs.Count - 1]

foreach ($el in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $t = $el.Current.ControlType.ProgrammaticName -replace "ControlType\.", ""
    $aid = $el.Current.AutomationId
    $name = $el.Current.Name
    $cls = $el.Current.ClassName
    if ($t -in @("Button", "Edit", "ComboBox", "MenuItem", "ListItem", "Custom", "Pane", "Window")) {
        Write-Output "[$t] AId='$aid' Name='$name' Class='$cls'"
    }
}
