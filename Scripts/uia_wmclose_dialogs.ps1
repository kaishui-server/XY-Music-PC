Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$sig = @"
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
"@
Add-Type -MemberDefinition $sig -Name U32 -Namespace Native

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$dlgTitle = [char]0x53E6 + [char]0x5B58 + [char]0x4E3A
$dialogs = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $dialogs += $el }
}
Write-Output "Save dialogs open: $($dialogs.Count)"
if ($dialogs.Count -eq 0) { exit 0 }

# 对每个不同的 NativeWindowHandle 发 WM_CLOSE (0x0010)
$seen = @{}
foreach ($d in $dialogs) {
    $hwnd = [IntPtr]$d.Current.NativeWindowHandle
    if ($hwnd -eq [IntPtr]::Zero -or $seen.ContainsKey($hwnd)) { continue }
    $seen[$hwnd] = $true
    Write-Output "Posting WM_CLOSE to HWND $hwnd (Class='$($d.Current.ClassName)')"
    [Native.U32]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}
Start-Sleep -Seconds 4

$rest = 0
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgTitle) { $rest++ }
}
Write-Output "Save dialogs remaining: $rest"
