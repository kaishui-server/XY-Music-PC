param([int]$WaitImportSec = 90)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1
if (-not $proc) { Write-Output "ERR: process not found"; exit 1 }
$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }
Write-Output "window acquired via handle"

# 1. 导航到设置
$aidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SettingsItem")
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $aidCond)
if (-not $item) { Write-Output "ERR: SettingsItem not found"; exit 1 }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3
Write-Output "settings nav OK"

# 2. 找"选择备份文件"按钮
$btnName = -join @(0x9009, 0x62E9, 0x5907, 0x4EFD, 0x6587, 0x4EF6 | ForEach-Object { [char]$_ })
$bcond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $btnName)
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bcond)
if (-not $btn) { Write-Output "ERR: import button not found"; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "import button clicked"
Start-Sleep -Seconds 3

# 3. 处理"打开"文件对话框
$dlgName = -join @(0x6253, 0x5F00 | ForEach-Object { [char]$_ })
$dlg = $null
for ($i = 0; $i -lt 10; $i++) {
    foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgName) { $dlg = $el }
    }
    if ($dlg) { break }
    Start-Sleep -Seconds 1
}
if (-not $dlg) { Write-Output "ERR: open dialog not found"; exit 1 }
Write-Output "open dialog found"

$edit = $null
foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) {
    $edit = $e
}
if (-not $edit) { Write-Output "ERR: filename edit not found"; exit 1 }
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("$env:USERPROFILE\Downloads\xymusic-20260926.json")
Start-Sleep -Seconds 1

$openBtn = $null
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "1")))) {
    if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $openBtn = $b; break }
}
if (-not $openBtn) { Write-Output "ERR: open button not found"; exit 1 }
$openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "file selected, importing..."

# 4. 等待导入完成: 结果对话框(ContentDialog)出现
$clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, "ContentDialog")
$cbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CloseButton")
$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
for ($i = 0; $i -lt $WaitImportSec; $i++) {
    Start-Sleep -Seconds 1
    $content = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $clsCond)
    if ($content) {
        Write-Output "result dialog detected after $($i+1)s"
        Start-Sleep -Seconds 2
        $texts = $content.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
        foreach ($t in $texts) { Write-Output ("  DIALOG: " + $t.Current.Name) }
        $closeBtn = $content.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
        if ($closeBtn) { $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "dialog closed" }
        break
    }
}
Write-Output "DONE"
