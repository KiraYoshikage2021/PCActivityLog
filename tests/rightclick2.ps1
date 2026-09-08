Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr e); [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p); public struct POINT { public int X; public int Y; }' -Name M -Namespace U
# 移到目标位置并读回实际坐标（确认是否被 DPI 缩放）
[U.M]::SetCursorPos(1591, 1392) | Out-Null
Start-Sleep -Milliseconds 300
$p = New-Object U.M+POINT
[U.M]::GetCursorPos([ref]$p) | Out-Null
Write-Host "设定 (1591,1392) -> 实际 ($($p.X),$($p.Y))"
# 右键
[U.M]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 120
[U.M]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "已右键"
Start-Sleep -Seconds 3
