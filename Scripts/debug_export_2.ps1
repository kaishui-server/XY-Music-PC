Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WEnum {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
}
"@
$targetPid = (Get-Process | Where-Object { $_.Name -eq "XY Music" } | Select-Object -First 1).Id
$found = @()
$cb = {
    param($h, $l)
    $wpid = 0
    [WEnum]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
    if ($wpid -eq $targetPid) {
        $script:found += $h
    }
    return $true
}
[WEnum]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
Write-Output "visible top-level windows of pid $targetPid : $($found.Count)"
foreach ($h in $found) {
    $cls = New-Object System.Text.StringBuilder 256
    [WEnum]::GetClassName($h, $cls, 256) | Out-Null
    $txt = New-Object System.Text.StringBuilder 256
    [WEnum]::GetWindowText($h, $txt, 256) | Out-Null
    $vis = [WEnum]::IsWindowVisible($h)
    Write-Output ("hwnd=0x{0:X}  class={1}  title={2}  visible={3}" -f $h.ToInt64(), $cls, $txt, $vis)
}
