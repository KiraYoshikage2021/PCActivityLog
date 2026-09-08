Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);' -Name W -Namespace U
$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$h = $p.MainWindowHandle
Write-Host "关闭前: PID=$($p.Id) 窗口可见=$([U.W]::IsWindowVisible($h))"
# 发送 WM_CLOSE（等价于点 X 按钮）
[U.W]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 3
$still = Get-Process PCActivityLog -ErrorAction SilentlyContinue | Select-Object -First 1
if ($still) {
    $still.Refresh()
    Write-Host "关闭后: 进程仍在 (PID=$($still.Id))"
    Write-Host "窗口可见=$([U.W]::IsWindowVisible($still.MainWindowHandle))"
    Write-Host ""
    if ([U.W]::IsWindowVisible($still.MainWindowHandle) -eq $false) { Write-Host "✓ PASS: 窗口隐藏、进程存活（最小化到托盘）" }
    else { Write-Host "? 窗口仍可见" }
} else {
    Write-Host "✗ FAIL: 进程已退出（应该最小化到托盘）"
}
