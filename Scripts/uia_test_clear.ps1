Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$kwLog = [char]0x65E5 + [char]0x5FD7 + [char]0x8BBE + [char]0x7F6E   # 日志设置
$btnClear = [char]0x6E05 + [char]0x7A7A                               # 清空
$logDir = "$env:USERPROFILE\Documents\XYMusic\Logs"

# 找日志设置 expander
$script:panel = $null
function Walk($el) {
    if ($script:panel) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $kwLog)))
        if ($kw) { $script:panel = $el; return }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $win
if (-not $script:panel) { Write-Output "Expander not found"; exit 1 }
$ec = $script:panel.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
if ($ec.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) { $ec.Expand(); Start-Sleep -Seconds 2 }

# 记录清空前状态
$before = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue
Write-Output "Before clear: $($before.Count) file(s), total $((($before | Measure-Object Length -Sum).Sum)) bytes"

# 点击 expander 内的清空按钮
$clearBtn = $null
foreach ($el in $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq $btnClear) { $clearBtn = $el; break }
}
if (-not $clearBtn) { Write-Output "Clear button not found"; exit 1 }
$clearBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Clear invoked, waiting for confirm dialog..."
Start-Sleep -Seconds 3

# ContentDialog 出现后会有第二个"清空"按钮(PrimaryButton); 取最后一个(弹层在树尾部)
$clearBtns = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq $btnClear) { $clearBtns += $el }
}
Write-Output "'$btnClear' buttons visible: $($clearBtns.Count)"
if ($clearBtns.Count -lt 2) { Write-Output "Confirm dialog not detected"; exit 1 }
$confirm = $clearBtns[$clearBtns.Count - 1]
$confirm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Confirm clicked"
Start-Sleep -Seconds 5

# 验证清空结果
$after = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue
$totalAfter = ($after | Measure-Object Length -Sum).Sum
Write-Output "After clear: $($after.Count) file(s), total $totalAfter bytes"
foreach ($f in $after) { Write-Output "  $($f.Name): $($f.Length) bytes" }

# 验证后日志可继续写入: 触发一次切歌
$next = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButton")))
if ($next) { $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "Next invoked" }
Start-Sleep -Seconds 12

$log = Join-Path $logDir "WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
if (Test-Path $log) {
    $len = (Get-Item $log).Length
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close()
    $lines = ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
    Write-Output "After next-track: log file $len bytes, $($lines.Count) lines"
    $lines | Select-Object -First 3
} else { Write-Output "Active log file missing after clear!" }
