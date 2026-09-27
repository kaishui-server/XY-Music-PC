param(
    [int[]]$DisableIdx = @(3, 8, 12),
    [int]$EnableIdx = 3,
    [int]$WaitMs = 2500
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
    $on = ($json | Where-Object { $_.Enabled }).Count
    Write-Output "  manifest: $($json.Count) plugins, $on enabled"
}

Write-Output "=== Phase 1: disable [$($DisableIdx -join ',')] ==="
foreach ($idx in $DisableIdx) {
    $t = Get-Toggles
    if ($idx -ge $t.Count) { Write-Output "idx $idx out of range"; continue }
    $before = $t[$idx].State
    if ("$before" -eq "On") {
        $t[$idx].Pattern.Toggle()
        Write-Output "toggled #$idx off"
    } else { Write-Output "#$idx already Off" }
    Start-Sleep -Milliseconds $WaitMs
    # 操作后重读状态
    $t2 = Get-Toggles
    Write-Output "  after: #$idx = $($t2[$idx].State)"
    Show-Manifest
}

Write-Output "=== Phase 2: all-off verify ==="
$t = Get-Toggles
$onCount = ($t | Where-Object { "$($_.State)" -eq "On" }).Count
Write-Output "UI toggles ON: $onCount / $($t.Count)"
Show-Manifest

Write-Output "=== Phase 3: try enable #$EnableIdx ==="
$t = Get-Toggles
if ($EnableIdx -lt $t.Count) {
    $t[$EnableIdx].Pattern.Toggle()
    Write-Output "clicked enable on #$EnableIdx"
}
Start-Sleep -Milliseconds $WaitMs
$t2 = Get-Toggles
Write-Output "  after: #$EnableIdx = $($t2[$EnableIdx].State)"
Show-Manifest
Start-Sleep -Milliseconds 1500
$t3 = Get-Toggles
Write-Output "  after 4s: #$EnableIdx = $($t3[$EnableIdx].State)"
Show-Manifest
