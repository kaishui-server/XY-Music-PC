Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# nav to plugin manage
$name = -join @(0x63D2, 0x4EF6, 0x7BA1, 0x7406 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
    Start-Sleep -Seconds 2
}

# toggle every ON switch to OFF
$mf = "$env:LOCALAPPDATA\XYMusic\Plugins\plugins.json"
for ($round = 0; $round -lt 5; $round++) {
    $toggles = @()
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $tp = $null
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
            $toggles += $tp
        }
    }
    $onCount = @($toggles | Where-Object { "$($_.Current.ToggleState)" -eq "On" }).Count
    if ($onCount -eq 0) { break }
    Write-Output "round ${round}: $onCount ON, toggling them off..."
    foreach ($tp in $toggles) {
        if ("$($tp.Current.ToggleState)" -eq "On") { $tp.Toggle(); Start-Sleep -Milliseconds 1200 }
    }
    Start-Sleep -Seconds 2
}

Start-Sleep -Seconds 2
$raw = [System.IO.File]::ReadAllText($mf)
$json = $raw | ConvertFrom-Json
$on = @($json | Where-Object { $_.enabled }).Count
Write-Output "final manifest: $($json.Count) plugins, $on enabled"
