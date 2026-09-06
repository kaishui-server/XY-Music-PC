Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement

function Find-AppWindow {
    param($TimeoutSec = 20)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $ws = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $ws) {
            $p = Get-Process -Id $w.Current.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $p -and $p.ProcessName -eq "OriginalSound HIFI Player") { return $w }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Invoke-NavItem {
    param($Window, [int[]]$CharCodes)
    $name = -join ($CharCodes | ForEach-Object { [char]$_ })
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $nav = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $nav) { return "NAV NOT FOUND: $name" }
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
        $sel.Select(); return "OK"
    }
    $inv = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        $inv.Invoke(); return "OK"
    }
    return "NO PATTERN: $name"
}

# 插件管理 = 0x63D2 0x4EF6 0x7BA1 0x7406
# 在线搜索 = 0x5728 0x7EBF 0x641C 0x7D22
$pluginNav = @(0x63D2, 0x4EF6, 0x7BA1, 0x7406)
$searchNav = @(0x5728, 0x7EBF, 0x641C, 0x7D22)

$win = Find-AppWindow
if (-not $win) { "WINDOW NOT FOUND"; exit 1 }
"window found, navigating..."

$r1 = Invoke-NavItem -Window $win -CharCodes $pluginNav
Start-Sleep -Seconds 3
$r2 = Invoke-NavItem -Window $win -CharCodes $searchNav
Start-Sleep -Seconds 3
$r3 = Invoke-NavItem -Window $win -CharCodes $pluginNav
Start-Sleep -Seconds 3
$r4 = Invoke-NavItem -Window $win -CharCodes $pluginNav
Start-Sleep -Seconds 3

$p = Get-Process -Name "OriginalSound HIFI Player" -ErrorAction SilentlyContinue
if ($p) { "APP ALIVE after multi-nav ($r1/$r2/$r3/$r4)" } else { "APP CRASHED during multi-nav ($r1/$r2/$r3/$r4)" }
