Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l); public delegate bool EnumWindowsProc(IntPtr h, IntPtr l); [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);' -Name M -Namespace U

function Count-ExplorerWins {
  $wins = New-Object System.Collections.ArrayList
  $cb = { param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [U.M]::GetClassName($h, $sb, 256) | Out-Null
    if ($sb.ToString() -eq 'CabinetWClass' -and [U.M]::IsWindowVisible($h)) { $null = $wins.Add($h) }
    return $true
  }
  [U.M]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  return $wins.Count
}

# 关掉刚才测试开的资源管理器窗口
Get-Process explorer | Where-Object { $_.MainWindowTitle -ne '' } | ForEach-Object { [U.M]::PostMessage($_.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
Start-Sleep -Milliseconds 800
$before = Count-ExplorerWins
Write-Host "起始资源管理器窗口: $before"

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { & "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"; Start-Sleep 3; $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

# 右键第一行数据（时间列区域: x=760, 表头底约534, 首行中心约552）
[U.M]::SetCursorPos(760, 552) | Out-Null
Start-Sleep -Milliseconds 250
[U.M]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)  # RIGHTDOWN
Start-Sleep -Milliseconds 60
[U.M]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero) # RIGHTUP
Write-Host "已右键行"
Start-Sleep -Milliseconds 900

# 点击菜单第一项（右下方约 +25,+15 处）
[U.M]::SetCursorPos(785, 570) | Out-Null
Start-Sleep -Milliseconds 350
[U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "已点击菜单项"
Start-Sleep -Seconds 3

$after = Count-ExplorerWins
Write-Host "结果资源管理器窗口: $after"
if ($after -gt $before) { Write-Host 'PASS: 打开文件位置正常工作' } else { Write-Host 'FAIL: 没有新窗口（复现故障）' }
