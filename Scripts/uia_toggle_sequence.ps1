param([int]$WaitMs = 2500)
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

function Toggle-And-Check {
    param([int]$Idx, [string]$Expect)
    $t = Get-Toggles
    if ($Idx -ge $t.Count) { Write-Output "idx $Idx out of range"; return }
    $t[$Idx].Pattern.Toggle()
    Start-Sleep -Milliseconds $WaitMs
    $t2 = Get-Toggles
    $state = "$($t2[$Idx].State)"
    $pass = if ($state -eq $Expect) { "PASS" } else { "FAIL" }
    Write-Output "#$Idx -> expect $Expect, actual $State [$pass]"
    Show-Manifest
}

Write-Output "=== disable #3 ==="
Toggle-And-Check -Idx 3 -Expect "Off"
Write-Output "=== re-enable #3 ==="
Toggle-And-Check -Idx 3 -Expect "On"
Write-Output "=== enable #11 ==="
Toggle-And-Check -Idx 11 -Expect "On"
Write-Output "=== enable #12 ==="
Toggle-And-Check -Idx 12 -Expect "On"
Write-Output "=== disable #12 ==="
Toggle-And-Check -Idx 12 -Expect "Off"
Write-Output "=== final UI states ==="
$t = Get-Toggles
$onCount = @($t | Where-Object { "$($_.State)" -eq "On" }).Count
Write-Output "UI toggles ON: $onCount / $($t.Count)"
Show-Manifest
