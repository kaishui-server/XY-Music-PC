param([int]$WaitSec = 4)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")))
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

$mf = "$env:LOCALAPPDATA\XYMusic\Plugins\plugins.json"
function Show-Manifest([string]$Tag) {
    $json = [System.IO.File]::ReadAllText($mf) | ConvertFrom-Json
    $on = @($json | Where-Object { $_.enabled }).Count
    Write-Output "[$Tag] manifest: $($json.Count) total, $on enabled"
}
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

# 1. nav to plugin manage page
$pluginPage = -join @([char]0x63D2, [char]0x4EF6, [char]0x7BA1, [char]0x7406)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $pluginPage)))
if (-not $nav) { Write-Output "ERR: nav not found"; exit 2 }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2

$enableName  = -join @([char]0x4E00, [char]0x952E, [char]0x542F, [char]0x7528, [char]0x5168, [char]0x90E8)
$disableName = -join @([char]0x4E00, [char]0x952E, [char]0x7981, [char]0x7528, [char]0x5168, [char]0x90E8)

Show-Manifest "baseline"
$t = Get-Toggles
Write-Output "UI toggles ON: $(@($t | Where-Object { "$($_.State)" -eq 'On' }).Count)/$($t.Count)"

# 2. disable all
$disableBtn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $disableName)))
if (-not $disableBtn) { Write-Output "ERR: disable-all button not found"; exit 3 }
$disableBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output ">>> disable-all clicked"
Start-Sleep -Seconds $WaitSec
Show-Manifest "after-disable"
$t = Get-Toggles
Write-Output "UI toggles ON: $(@($t | Where-Object { "$($_.State)" -eq 'On' }).Count)/$($t.Count)"

# 3. enable all
$enableBtn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $enableName)))
if (-not $enableBtn) { Write-Output "ERR: enable-all button not found"; exit 4 }
$enableBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output ">>> enable-all clicked"
Start-Sleep -Seconds $WaitSec
Show-Manifest "after-enable"
$t = Get-Toggles
Write-Output "UI toggles ON: $(@($t | Where-Object { "$($_.State)" -eq 'On' }).Count)/$($t.Count)"

# 4. restore lx-animemusic to disabled (its baseline state) via row toggle
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
foreach ($it in $items) {
    $txt = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "lx-animemusic")))
    if ($txt) {
        $tp = $null
        foreach ($b in $it.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))) {
            if ($b.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
                $st = $tp.Current.ToggleState
                if ("$st" -eq "On") { $tp.Toggle(); Write-Output "lx-animemusic toggled Off (was On)" }
                break
            }
        }
        break
    }
}
Start-Sleep -Seconds 3
Show-Manifest "after-restore"
