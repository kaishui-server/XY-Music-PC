Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$searchBtnName = -join @([char]0x641C, [char]0x7D22)
"name len: $($searchBtnName.Length)"
"name: $searchBtnName"

try {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $searchBtnName)
    "cond OK: $($c -ne $null)"
} catch {
    "COND FAILED: $($_.Exception.Message)"
}
