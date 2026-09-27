param(
    [string]$NamePart = "XY Music",
    [int]$Depth = 3
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Dump-Tree($el, $indent, $maxDepth) {
    if ($indent.Length / 2 -ge $maxDepth) { return }
    $name = $el.Current.Name
    $type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
    $aid = $el.Current.AutomationId
    $cls = $el.Current.ClassName
    $line = "$indent[$type] Name='$name' AId='$aid' Cls='$cls'"
    Write-Output $line
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Dump-Tree $c ($indent + "  ") $maxDepth }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $NamePart)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window '$NamePart' not found"; exit 1 }
Write-Output "=== Window found: $($win.Current.ProcessId) ==="
Dump-Tree $win "" $Depth
