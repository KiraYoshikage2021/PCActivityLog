Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr e);' -Name M -Namespace U
# 托盘图标在 (1591,1392)，这是逻辑坐标；屏幕 2560x1440，需按 DPI 换算
# 先直接试逻辑坐标
[U.M]::SetCursorPos(1591, 1392) | Out-Null
Start-Sleep -Milliseconds 500
[U.M]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTDOWN
Start-Sleep -Milliseconds 100
[U.M]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)  # RIGHTUP
Write-Host "已在托盘图标 (1591,1392) 右键"
Start-Sleep -Seconds 3
