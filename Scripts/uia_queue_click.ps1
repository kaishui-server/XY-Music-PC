param([int]$WaitSec = 5)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")))
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

function Get-Title {
    $t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
    if ($t) { $t.Current.Name } else { "" }
}

# 1. open queue panel
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CurrentPlayListButton")))
if (-not $btn) { Write-Output "ERR: queue button not found"; exit 2 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "queue panel opened"
Start-Sleep -Seconds 3

# 2. guard check: opening panel must NOT replay/restart the current song
$before = Get-Title
Start-Sleep -Seconds 2
$afterOpen = Get-Title
Write-Output "title before='$before' after-open='$afterOpen' guard=$(if ($before -eq $afterOpen) {'OK'} else {'CHANGED!'})"

# 3. dump queue items
$lv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CurrentPlayListView")))
if (-not $lv) { Write-Output "ERR: queue listview not found"; exit 3 }
$items = $lv.FindAll([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
Write-Output "queue items: $($items.Count)"
for ($i = 0; $i -lt [Math]::Min(10, $items.Count); $i++) { Write-Output "  [$i] $($items[$i].Current.Name)" }

# 4. pick an item not matching current title and single-click it (SelectionItemPattern.Select)
$target = $null
foreach ($it in $items) {
    if ($afterOpen -and $it.Current.Name.Contains($afterOpen)) { continue }
    $target = $it; break
}
if (-not $target) { Write-Output "ERR: no other item to click"; exit 4 }
Write-Output "clicking item: '$($target.Current.Name)'"
$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()

# 5. verify the play bar jumped to the clicked song
Start-Sleep -Seconds $WaitSec
$afterClick = Get-Title
Write-Output "title after-click='$afterClick'"
$ok = ($afterClick -ne $afterOpen) -and ($afterClick.Length -gt 0) -and ($target.Current.Name.Contains($afterClick))
Write-Output "RESULT: $(if ($ok) {'PASS'} else {'FAIL'})"

# 6. log tail for playback evidence (shared read, app holds write lock)
$dir = "$env:USERPROFILE\Documents\XYMusic\Logs"
$latest = Get-ChildItem $dir -Force | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latest) {
    $fs = [System.IO.FileStream]::new($latest.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $len = [Math]::Min(5000, $fs.Length)
        $fs.Seek(-$len, [System.IO.SeekOrigin]::End) | Out-Null
        $buf = New-Object byte[] $len
        [void]$fs.Read($buf, 0, $len)
        $text = [System.Text.Encoding]::UTF8.GetString($buf)
        Write-Output "--- log tail ---"
        ($text -split "`n" | Select-Object -Last 20) | ForEach-Object { Write-Output $_ }
    } finally { $fs.Dispose() }
}
