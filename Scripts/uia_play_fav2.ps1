Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 选择收藏标签
$fav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "Favourite")))
if ($fav) { $fav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Write-Output "Favourite tab selected" }
else { Write-Output "Favourite tab not found"; exit 1 }
Start-Sleep -Seconds 4

# 找收藏列表中第一个可播放的封面按钮: 按项目经验是行首列 PlayCommand 封面按钮
# 先列出所有按钮的 AutomationId, 找疑似播放的
$ids = @{}
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))) {
    $aid = $el.Current.AutomationId
    if ($aid) { $ids[$aid] = $true }
}
$ids.Keys | Sort-Object | ForEach-Object { Write-Output "BTN-AID: $_" }
