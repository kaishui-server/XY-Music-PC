param([int]$LyricWaitSec = 12, [int]$SwitchWaitSec = 6, [int]$ReplayWaitSec = 10)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

# ---- 中文字面量按字符码拼接(避免脚本编码问题) ----
$s_lib      = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })          # 音乐库
$s_fav      = -join @(0x6536, 0x85CF | ForEach-Object { [char]$_ })                    # 收藏
$s_empty    = -join @(0x6682, 0x65E0, 0x6B4C, 0x8BCD | ForEach-Object { [char]$_ })    # 暂无歌词
$p_timing   = -join @(0x64AD, 0x653E, 0x8BA1, 0x65F6 | ForEach-Object { [char]$_ })    # 播放计时
$p_plugin   = -join @(0x63D2, 0x4EF6, 0x6B4C, 0x8BCD | ForEach-Object { [char]$_ })    # 插件歌词
$p_search   = -join @(0x8054, 0x7F51, 0x641C, 0x7D22 | ForEach-Object { [char]$_ })    # 联网搜索
$p_lyric    = -join @(0x6B4C, 0x8BCD | ForEach-Object { [char]$_ })                    # 歌词

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
function Read-LogLines {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
    return ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
}
function Get-Title {
    $t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
    if ($t) { return $t.Current.Name }
    return ""
}
function Invoke-ById([string]$id) {
    $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
    if (-not $el) { return $false }
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return $true }
    return $false
}
function Close-Detail { Invoke-ById "CancelPlayingDetailButton" | Out-Null; Start-Sleep -Seconds 2 }

# 歌词区验证: LyricTextBlock 行文本 + 暂无歌词空态
function Get-LyricState {
    $state = @{ Lines = @(); EmptyState = $false; Blank = $false }
    $tb = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "LyricTextBlock")))
    foreach ($el in $tb) {
        try { if ($el.Current.Name) { $state.Lines += $el.Current.Name } } catch {}
    }
    $emptyEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $s_empty)))
    if ($emptyEl) { $state.EmptyState = $true }
    if ($state.Lines.Count -eq 0 -and -not $state.EmptyState) { $state.Blank = $true }
    return $state
}

# ---- 1. 导航 音乐库 -> 收藏 ----
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $s_lib)))
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); Write-Output "nav: music library" }
} else { Write-Output "ERR: music library nav not found"; exit 2 }
Start-Sleep -Seconds 3
$fav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $s_fav)))
if ($fav) {
    $fsel = $null
    if ($fav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$fsel)) { $fsel.Select(); Write-Output "tab: favourite" }
} else { Write-Output "ERR: favourite tab not found"; exit 2 }
Start-Sleep -Seconds 4

# ---- 2. 依次尝试收藏行, 直到有一首播放成功 ----
$coverBtns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")))
Write-Output "favourite rows: $($coverBtns.Count)"
$playedRow = -1
for ($i = 0; $i -lt [Math]::Min(5, $coverBtns.Count); $i++) {
    $base = (Read-LogLines).Count
    $inv = $null
    if (-not $coverBtns[$i].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { continue }
    $inv.Invoke()
    Start-Sleep -Seconds 8
    $newLines = (Read-LogLines) | Select-Object -Skip $base
    $done = $newLines | Where-Object { $_ -match $p_timing -and $_ -match "完成" }
    if ($done) { $playedRow = $i; Write-Output "row $($i+1) playing OK"; break }
    Write-Output "row $($i+1) play failed, trying next"
}
if ($playedRow -lt 0) { Write-Output "ERR: no playable row found"; exit 3 }

Start-Sleep -Seconds $LyricWaitSec
$firstTitle = Get-Title
Write-Output "song A: '$firstTitle'"

# ---- 3. 首次播放确认有歌词(baseline) ----
Invoke-ById "AlbumCoverImageBtn" | Out-Null
Start-Sleep -Seconds 5
$state1 = Get-LyricState
Write-Output ("baseline lyrics: lines=" + $state1.Lines.Count + " emptyState=" + $state1.EmptyState + " blank=" + $state1.Blank)
if ($state1.Lines.Count -gt 0) { Write-Output "  sample: $($state1.Lines[0])" }
Close-Detail

# ---- 4. 队列切到另一首歌 ----
$base2 = (Read-LogLines).Count
Invoke-ById "CurrentPlayListButton" | Out-Null
Start-Sleep -Seconds 3
$lv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CurrentPlayListView")))
if (-not $lv) { Write-Output "ERR: queue listview not found"; exit 4 }
$items = $lv.FindAll([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.ControlType]::ListItem, [System.Windows.Automation.AutomationElement]::ControlTypeProperty)))
$items = $lv.FindAll([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
Write-Output "queue items: $($items.Count)"

$targetB = $null
foreach ($it in $items) {
    if ($firstTitle -and $it.Current.Name.Contains($firstTitle)) { continue }
    if ($it.Current.Name -and $it.Current.Name.Trim().Length -gt 0) { $targetB = $it; break }
}
if (-not $targetB) { Write-Output "ERR: no other song in queue"; exit 5 }
Write-Output "song B: '$($targetB.Current.Name)'"
$targetB.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds $SwitchWaitSec
Write-Output "after switch B, title: '$(Get-Title)'"

# ---- 5. 队列点回 song A(重播!) ----
$lv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CurrentPlayListView")))
$items = $lv.FindAll([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
$targetA = $null
foreach ($it in $items) { if ($it.Current.Name -and $it.Current.Name.Contains($firstTitle)) { $targetA = $it; break } }
if (-not $targetA) { Write-Output "ERR: song A not in queue anymore"; exit 6 }
Write-Output "replaying song A..."
$base3 = (Read-LogLines).Count
$targetA.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds $ReplayWaitSec
Write-Output "after replay A, title: '$(Get-Title)'"

# ---- 6. 打开详情页验证歌词恢复 ----
Invoke-ById "AlbumCoverImageBtn" | Out-Null
Start-Sleep -Seconds 5
$state2 = Get-LyricState
Write-Output ("replay lyrics: lines=" + $state2.Lines.Count + " emptyState=" + $state2.EmptyState + " blank=" + $state2.Blank)
if ($state2.Lines.Count -gt 0) { Write-Output "  sample: $($state2.Lines[0])" }

# ---- 7. 日志增量(重播段: 观察是否重新联网搜索/缓存命中) ----
Write-Output "=== log between switch and verify ==="
$all = Read-LogLines
$new2 = $all | Select-Object -Skip $base2
$new2 | Where-Object { $_ -match $p_timing -or $_ -match $p_plugin -or $_ -match $p_search -or $_ -match "GetKrc|GetMixed|GetLyricsAsync" } | ForEach-Object { if ($_.Length -gt 180) { Write-Output ("  " + $_.Substring(0,180)) } else { Write-Output ("  " + $_) } }

# ---- 8. 结论 ----
if ($state2.Lines.Count -gt 0) { Write-Output "RESULT: PASS - lyrics restored after replay" }
elseif ($state2.EmptyState) { Write-Output "RESULT: FAIL - shows empty state (cache miss / search skipped)" }
else { Write-Output "RESULT: FAIL - blank lyrics region" }
