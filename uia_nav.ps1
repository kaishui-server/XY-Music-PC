param([int]$C1 = 0x63D2, [int]$C2 = 0x4EF6, [int]$C3 = 0x7BA1, [int]$C4 = 0x7406)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$itemName = -join @([char]$C1, [char]$C2, [char]$C3, [char]$C4)

$root = [System.Windows.Automation.AutomationElement]::RootElement
if (-not $root) { Write-Output "ROOT NULL"; exit 1 }

$deadline = (Get-Date).AddSeconds(20)
$window = $null
while ((Get-Date) -lt $deadline) {
    $ws = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($w in $ws) {
        $p = Get-Process -Id $w.Current.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $p -and $p.ProcessName -eq "OriginalSound HIFI Player") { $window = $w; break }
    }
    if ($window) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $window) { Write-Output "WINDOW NOT FOUND"; exit 1 }
Write-Output "WINDOW FOUND"

$cond2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $itemName)
$navItem = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond2)
if (-not $navItem) { Write-Output "NAV ITEM NOT FOUND"; exit 1 }
Write-Output "NAV ITEM FOUND"

$invokePattern = $null
if ($navItem.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
    $invokePattern.Invoke()
    Write-Output "INVOKED"
} else {
    $selPattern = $null
    if ($navItem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selPattern)) {
        $selPattern.Select()
        Write-Output "SELECTED"
    } else {
        Write-Output "NO PATTERN"
        exit 1
    }
}
