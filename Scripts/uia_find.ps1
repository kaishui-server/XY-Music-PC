param(
    [string]$NamePart = "XY Music",
    [string]$FindName = "",
    [string]$FindAId = "",
    [int]$MaxDepth = 12
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$script:found = @()

function Find-El($el, $depth) {
    if ($depth -ge $MaxDepth) { return }
    $name = $el.Current.Name
    $aid = $el.Current.AutomationId
    $match = $false
    if ($FindName -ne "" -and $name -like "*$FindName*") { $match = $true }
    if ($FindAId -ne "" -and $aid -eq $FindAId) { $match = $true }
    if ($match) {
        $type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $script:found += [PSCustomObject]@{ Type=$type; Name=$name; AId=$aid; Depth=$depth }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Find-El $c ($depth + 1) }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $NamePart)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }
Find-El $win 0
if ($script:found.Count -eq 0) { Write-Output "NOT FOUND: name='$FindName' aid='$FindAId'" }
else { $script:found | ForEach-Object { Write-Output ("[{0}] d{1} Name='{2}' AId='{3}'" -f $_.Type, $_.Depth, $_.Name, $_.AId) } }
