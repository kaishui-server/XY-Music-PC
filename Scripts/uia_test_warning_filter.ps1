param([string]$Keyword = [char]0x65E5 + [char]0x5FD7 + [char]0x8BBE + [char]0x7F6E)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-20260927.log"
function Count-Inf {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close()
    return @(([regex]::Matches($txt, "\[INF\]")).Count)
}
function Find-LogPanel {
    $script:panel = $null
    function Walk($el) {
        if ($script:panel) { return }
        $p = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
            $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Keyword)))
            if ($kw) { $script:panel = $el; return }
        }
        $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($c in $children) { Walk $c }
    }
    Walk $win
    return $script:panel
}
function Get-ToggleBtn($panel) {
    foreach ($el in $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $tp = $null
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
            $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) { return $el }
    }
    return $null
}
function Invoke-Next {
    $btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButton")))
    if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "next invoked" }
    else { Write-Output "NEXT BUTTON NOT FOUND" }
}

$panel = Find-LogPanel
if (-not $panel) { Write-Output "Log panel not found"; exit 1 }
$toggleBtn = Get-ToggleBtn $panel
if (-not $toggleBtn) { Write-Output "Toggle not found"; exit 1 }
$tp = $toggleBtn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)

$before = Count-Inf
Write-Output "Baseline INF entries: $before"

# --- Phase 1: warning-only ON, skip track, expect no new INF ---
if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $tp.Toggle(); Start-Sleep -Seconds 2 }
Write-Output "Toggle state: $($tp.Current.ToggleState)"
Invoke-Next
Start-Sleep -Seconds 14
$mid = Count-Inf
Write-Output "INF entries with warning-only ON: $mid (delta: $($mid - $before))"

# --- Phase 2: warning-only OFF, skip track, expect new INF ---
if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { $tp.Toggle(); Start-Sleep -Seconds 2 }
Write-Output "Toggle state: $($tp.Current.ToggleState)"
Invoke-Next
Start-Sleep -Seconds 14
$after = Count-Inf
Write-Output "INF entries with warning-only OFF: $after (delta: $($after - $mid))"

# --- Verdict ---
$okFilter = ($mid -eq $before)
$okRestore = ($after -gt $mid)
Write-Output ("RESULT: filter_suppresses_inf={0} restore_writes_inf={1}" -f $okFilter, $okRestore)
$cfg = "$env:LOCALAPPDATA\XYMusic\LogSettings.json"
Write-Output "LogSettings.json: $(Get-Content $cfg -Raw)"
