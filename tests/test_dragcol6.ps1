Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name M -Namespace U

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { & "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"; Start-Sleep 3; $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

# 稳稳落在 时间列右抓手内部 (862,512)
[U.M]::SetCursorPos(862, 512) | Out-Null
Start-Sleep -Milliseconds 300
[U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 250
for ($i = 0; $i -lt 10; $i++) {
  [U.M]::mouse_event(1, 5, 0, 0, [UIntPtr]::Zero)   # 相对 +5，共 +50
  Start-Sleep -Milliseconds 60
}
Start-Sleep -Milliseconds 300
[U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "慢速拖拽完成，光标现在 X:"
[System.Windows.Forms.Cursor]::Position.X
