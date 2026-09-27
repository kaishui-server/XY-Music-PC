Add-Type -AssemblyName UIAutomationClient
Add-Type -AutomationTypes -ErrorAction SilentlyContinue
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
Write-Output "Dialog HWND: $($dlg.Current.NativeWindowHandle) Class: $($dlg.Current.ClassName)"

foreach ($el in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $aid = $el.Current.AutomationId
    $cls = $el.Current.ClassName
    if ($aid -in @("1001", "1", "2") -or $cls -in @("Edit", "Button")) {
        $patterns = ($el.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace "Pattern$", "" -replace "^System\.Windows\.Automation\.", "" }) -join ","
        Write-Output "AId='$aid' Class='$cls' Name='$($el.Current.Name)' Type=$($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.','')"
        Write-Output "    Patterns: $patterns"
    }
}
