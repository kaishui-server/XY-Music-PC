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

function Invoke-Element {
    param($el)
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return $true }
    $sel = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); return $true }
    return $false
}

# 在线搜索 = 0x5728 0x7EBF 0x641C 0x7D22
# 搜索 = 0x641C 0x7D22
$onlineSearchNav = -join @([char]0x5728, [char]0x7EBF, [char]0x641C, [char]0x7D22)
$searchBtnName = -join @([char]0x641C, [char]0x7D22)

$win = Find-AppWindow
if (-not $win) { "WINDOW NOT FOUND"; exit 1 }
"window found"

# 导航到在线搜索页
$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $onlineSearchNav)
$navItem = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $navCond)
if ($navItem) {
    Invoke-Element $navItem | Out-Null
    "nav to online search page"
} else { "NAV NOT FOUND"; exit 1 }
Start-Sleep -Seconds 4

$win = Find-AppWindow
if (-not $win) { "WINDOW LOST"; exit 1 }

# 检查插件 Tab (QQ音乐 = 0x51QQ: QQ = 'Q''Q', 音乐 = 0x97F3 0x4E50)
$qqTab = "QQ" + [char]0x97F3 + [char]0x4E50
$tabCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $qqTab)
$tabEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if ($tabEl) { "plugin tab found: $qqTab" } else { "plugin tab NOT found: $qqTab" }

# 找搜索输入框 (第一个 Edit)
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
"edits: $($edits.Count)"
if ($edits.Count -eq 0) { "NO EDIT"; exit 1 }

# 输入关键词: 周杰伦 = 0x5468 0x6770 0x4F26
$keyword = -join @([char]0x5468, [char]0x6770, [char]0x4F26)
$vp = $null
if ($edits[0].TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
    $vp.SetValue($keyword)
    "keyword entered: $keyword"
} else { "NO VALUE PATTERN"; exit 1 }

Start-Sleep -Seconds 1

# 用回车触发搜索: 聚焦输入框后发送 Enter (KeywordBox_KeyDown 已处理)
Add-Type -AssemblyName System.Windows.Forms
try {
    $edits[0].SetFocus()
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    "enter sent"
} catch {
    "SETFOCUS FAILED: $($_.Exception.Message)"
    exit 1
}

# 等待搜索结果
Start-Sleep -Seconds 20

$p = Get-Process -Name "OriginalSound HIFI Player" -ErrorAction SilentlyContinue
if ($p) { "APP ALIVE after search" } else { "APP CRASHED after search"; exit 1 }

# 检查应用日志中的 search called 记录
$log = "$env:USERPROFILE\Documents\OriginalSoundPlayer\Logs\WinUIMusicPlayer-20260904.log"
$hits = Get-Content $log -Tail 30 | Select-String -Pattern "search called|getMediaSource|\[ERR\]"
if ($hits) { $hits | ForEach-Object { $_.Line.Substring(0, [Math]::Min(150, $_.Line.Length)) } } else { "no search log entries" }
