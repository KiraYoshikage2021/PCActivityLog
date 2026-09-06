Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name M -Namespace U
$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800
[U.M]::SetCursorPos(760, 552) | Out-Null
Start-Sleep -Milliseconds 300
[U.M]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 70
[U.M]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "右键已发送，保持现场"
Start-Sleep -Milliseconds 900
