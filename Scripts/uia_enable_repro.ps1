param(
    [int]$EnableIdx = 3,
    [int]$WaitMs = 3000
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

function Get-VisibleTexts {
    $texts = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            $n = $el.Current.Name
            if ($n) { $texts += $n }
        } catch {}
    }
    return $texts | Select-Object -Unique
}

$mf = "$env:LOCALAPPDATA\XYMusic\Plugins\plugins.json"
function Show-Manifest {
    $raw = [System.IO.File]::ReadAllText($mf)
    $json = $raw | ConvertFrom-Json
    $on = @($json | Where-Object { $_.enabled }).Count
    Write-Output "  manifest: $($json.Count) plugins, $on enabled"
}

Write-Output "=== Phase 0: current toggle states ==="
$t = Get-Toggles
Write-Output "toggles found: $($t.Count)"
$i = 0
foreach ($item in $t) { Write-Output "  #$i = $($item.State)"; $i++ }
Show-Manifest

if ($t.Count -eq 0) { Write-Output "no toggles on this page, need nav first"; exit 2 }

if ($EnableIdx -ge $t.Count) { Write-Output "idx out of range"; exit 3 }

Write-Output "=== Phase 1: toggle #$EnableIdx ON ==="
$before = $t[$EnableIdx].State
$t[$EnableIdx].Pattern.Toggle()
Write-Output "clicked (before: $before)"

Start-Sleep -Milliseconds 600
$texts = Get-VisibleTexts
Write-Output "  toast/texts at 0.6s: $($texts -join ' | ')"

Start-Sleep -Milliseconds ($WaitMs - 600)
$t2 = Get-Toggles
Write-Output "  after ${WaitMs}ms: #$EnableIdx = $($t2[$EnableIdx].State) (total toggles: $($t2.Count))"
Show-Manifest

Start-Sleep -Milliseconds 2000
$t3 = Get-Toggles
Write-Output "  after +2s: #$EnableIdx = $($t3[$EnableIdx].State)"
Show-Manifest
