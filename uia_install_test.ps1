Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pluginUrl = "https://music.cwo.cc.cd/plugins/qq.js?source=linglan&key=CERU_KEY-QaLw7gh1KEpKzEwq7qYE7NweM0rKMRBe8U5aRE8KHQDMsdRc"

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

# 从链接安装 = 0x4ECE 0x94FE 0x63A5 0x5B89 0x88C5
# 从文件安装 = 0x4ECE 0x6587 0x4EF6 0x5B89 0x88C5
$fromUrlBtn = -join @([char]0x4ECE, [char]0x94FE, [char]0x63A5, [char]0x5B89, [char]0x88C5)

$win = Find-AppWindow
if (-not $win) { "WINDOW NOT FOUND"; exit 1 }
"window found"

# 先导航到插件管理页
$pluginNavName = -join @([char]0x63D2, [char]0x4EF6, [char]0x7BA1, [char]0x7406)
$navCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $pluginNavName)
$navItem = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $navCond)
if ($navItem) {
    $navInv = $null
    if ($navItem.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$navInv)) { $navInv.Invoke() }
    else {
        $navSel = $null
        if ($navItem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$navSel)) { $navSel.Select() }
    }
    "nav to plugin page done"
} else {
    "nav item not found"
}
Start-Sleep -Seconds 3
$win = Find-AppWindow
if (-not $win) { "WINDOW LOST"; exit 1 }

# 找 "从链接安装" 文本元素, 向上找可点击的按钮祖先
$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $fromUrlBtn)
$textEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
if (-not $textEl) { "URL BUTTON TEXT NOT FOUND"; exit 1 }
"found text: $fromUrlBtn"

# 沿树向上找支持 Invoke 的祖先
$node = $textEl
$clicked = $false
for ($i = 0; $i -lt 8; $i++) {
    $node = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($node)
    if ($null -eq $node) { break }
    if (Invoke-Element $node) { "clicked ancestor: $($node.Current.ControlType.ProgrammaticName)"; $clicked = $true; break }
}
if (-not $clicked) { "NO CLICKABLE ANCESTOR"; exit 1 }

Start-Sleep -Seconds 3

# 找弹出的 ContentDialog 里的 TextBox, 输入 URL
$win = Find-AppWindow
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
"edits found: $($edits.Count)"
if ($edits.Count -eq 0) { "NO EDIT BOX - dialog not open?"; exit 1 }

$edit = $edits[0]
$valuePattern = $null
if ($edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
    $valuePattern.SetValue($pluginUrl)
    "url entered"
} else {
    "NO VALUE PATTERN"
    exit 1
}

Start-Sleep -Seconds 1

# 找对话框的主按钮(安装插件 = 0x5B89 0x88C5 0x63D2 0x4EF6) 并点击
$installBtn = -join @([char]0x5B89, [char]0x88C5, [char]0x63D2, [char]0x4EF6)
$nameCond2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $installBtn)
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond2)
if ($btn) {
    if (Invoke-Element $btn) { "install clicked" } else { "INSTALL BTN NOT INVOKABLE" }
} else {
    "INSTALL BUTTON NOT FOUND"
    exit 1
}

# 等待安装完成
Start-Sleep -Seconds 15

$p = Get-Process -Name "OriginalSound HIFI Player" -ErrorAction SilentlyContinue
if ($p) { "APP ALIVE after install" } else { "APP CRASHED after install" }

# 检查 manifest 是否包含 QQ音乐
Start-Sleep -Seconds 2
$manifest = Get-Content "$env:LOCALAPPDATA\OriginalSoundPlayer\Plugins\plugins.json" -Raw -ErrorAction SilentlyContinue
if ($manifest -match "QQ音乐") { "MANIFEST CONTAINS QQ Music plugin - INSTALL SUCCESS" } else { "MANIFEST: $manifest" }

