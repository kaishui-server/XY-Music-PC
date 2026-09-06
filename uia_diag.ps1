Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$ws = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("TOTAL TOP WINDOWS: " + $ws.Count)
foreach ($w in $ws) {
    $procId = $w.Current.ProcessId
    $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($null -ne $p -and ($p.ProcessName -like '*OriginalSound*' -or $p.ProcessName -like '*Bass*')) {
        Write-Output ("APPWIN: [" + $w.Current.Name + "] proc=" + $p.ProcessName + " class=" + $w.Current.ClassName)
    }
}
