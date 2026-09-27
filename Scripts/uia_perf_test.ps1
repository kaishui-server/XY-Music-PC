param(
    [int[]]$EnableIdx = @(3, 8, 12),
    [int]$WaitMs = 2000
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

function Get-Toggles {
    $list = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $tp = $null
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
            $list += @{ El = $el; Pattern = $tp; State = $tp.Current.ToggleState }
        }
    }
    return ,$list
}

$mf = "$env:LOCALAPPDATA\XYMusic\Plugins\plugins.json"
function Show-Manifest {
    $raw = [System.IO.File]::ReadAllText($mf)
    $json = $raw | ConvertFrom-Json
    $on = @($json | Where-Object { $_.enabled }).Count
    Write-Output "  manifest: $($json.Count) plugins, $on enabled"
}

# nav to plugin manage
$name = -join @(0x63D2, 0x4EF6, 0x7BA1, 0x7406 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
    Start-Sleep -Seconds 2
}

Write-Output "=== enable plugins [$($EnableIdx -join ',')] ==="
foreach ($idx in $EnableIdx) {
    $t = Get-Toggles
    if ($idx -ge $t.Count) { Write-Output "idx $idx out of range"; continue }
    if ("$($t[$idx].State)" -ne "On") {
        $t[$idx].Pattern.Toggle()
        Start-Sleep -Milliseconds $WaitMs
    }
}
$t = Get-Toggles
$onCount = @($t | Where-Object { "$($_.State)" -eq "On" }).Count
Write-Output "UI toggles ON: $onCount"
Show-Manifest
