param([int]$Idx = 3, [string]$Dir = "on", [int]$WaitSec = 4)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

function Get-Toggles {
    $list = @()
    foreach ($el in $script:win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $tp = $null
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
            $list += @{ El = $el; Pattern = $tp; State = $tp.Current.ToggleState }
        }
    }
    return ,$list
}

$mf = "C:\Users\admin\AppData\Local\XYMusic\Plugins\plugins.json"
function Show-Manifest {
    $raw = [System.IO.File]::ReadAllText($mf)
    $json = $raw | ConvertFrom-Json
    $on = @($json | Where-Object { $_.Enabled })
    $names = ($on | ForEach-Object { $_.Platform }) -join ", "
    Write-Output "  manifest[$(Get-Date -Format HH:mm:ss.fff)]: enabled=$(@($on).Count) [$names]"
}

Write-Output "=== before ==="
$t = Get-Toggles
Write-Output "toggle #$Idx = $($t[$Idx].State)"
Show-Manifest

Write-Output "=== clicking #$Idx -> $Dir ==="
if ($Dir -eq "on") {
    if ("$($t[$Idx].State)" -eq "Off") { $t[$Idx].Pattern.Toggle(); Write-Output "Toggle() sent" }
    else { Write-Output "already On" }
} else {
    if ("$($t[$Idx].State)" -eq "On") { $t[$Idx].Pattern.Toggle(); Write-Output "Toggle() sent" }
    else { Write-Output "already Off" }
}

for ($s = 1; $s -le $WaitSec; $s++) {
    Start-Sleep -Seconds 1
    $t2 = Get-Toggles
    if ($t2.Count -gt $Idx) { Write-Output "  +${s}s UI: #$Idx = $($t2[$Idx].State) (total toggles: $($t2.Count))" }
    Show-Manifest
}
