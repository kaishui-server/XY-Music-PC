param([int]$Idx = 3)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$toggles = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $tp = $null
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) {
        $toggles += @{ El = $el; Pattern = $tp }
    }
}
if ($Idx -ge $toggles.Count) { Write-Output "idx out of range"; exit 1 }
$t = $toggles[$Idx]

$el = $t.El
Write-Output "Element: Type=$($el.Current.ControlType.ProgrammaticName) Name='$($el.Current.Name)' AId='$($el.Current.AutomationId)' Class='$($el.Current.ClassName)'"
Write-Output "Supported patterns:"
foreach ($p in $el.GetSupportedPatterns()) { Write-Output "  $($p.ProgrammaticName)" }
$tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output "State before: $($tp.Current.ToggleState)"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$tp.Toggle()
Write-Output "Toggle() returned after $($sw.ElapsedMilliseconds)ms"
# 立即读
$t1 = $el.Current.ControlType
$tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output "State IMMEDIATELY: $($tp.Current.ToggleState)"
Start-Sleep -Milliseconds 100
$tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output "State +100ms: $($tp.Current.ToggleState)"
Start-Sleep -Milliseconds 400
$tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output "State +500ms: $($tp.Current.ToggleState)"
Start-Sleep -Milliseconds 1500
$tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output "State +2s: $($tp.Current.ToggleState)"
