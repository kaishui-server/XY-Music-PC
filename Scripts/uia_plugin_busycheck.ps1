Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$proc = Get-Process -Name "*XY Music*" | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
Write-Output "App PID: $($proc.Id), started: $($proc.StartTime), now: $(Get-Date -Format 'HH:mm:ss')"

# 找页面上的按钮: 安装(本地文件)/从URL安装/一键卸载
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output "=== Buttons with enabled state ==="
foreach ($el in $all) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $name = $el.Current.Name
        if ($name -match "安装|卸载|Install|Uninstall") {
            $ip = $null
            $canInvoke = ""
            if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) {
                try { $canInvoke = "CanInvoke=$($ip.Current.CanInvoke)" } catch {}
            }
            Write-Output "Name='$name' IsEnabled=$($el.Current.IsEnabled) $canInvoke"
        }
    }
}

# 当前清单状态
$mf = "$env:LOCALAPPDATA\XYMusic\Plugins\plugins.json"
$raw = [System.IO.File]::ReadAllText($mf)
$json = $raw | ConvertFrom-Json
$on = @($json | Where-Object { $_.Enabled })
Write-Output "Manifest: $(@($json).Count) plugins, $(@($on).Count) enabled"
if ($on.Count -gt 0) { $on | ForEach-Object { Write-Output "  enabled: $($_.Platform)" } }
