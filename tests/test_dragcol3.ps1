Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L; public int T; public int R; public int B; }' -Name G -Namespace U
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name M -Namespace U

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { & "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"; Start-Sleep 3; $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object U.G+RECT
[U.G]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
Write-Host ("窗口: L={0} T={1} R={2} B={3} (宽{4} 高{5})" -f $r.L, $r.T, $r.R, $r.B, ($r.R-$r.L), ($r.B-$r.T))

# 表头"时间|类型"边界：窗口左边 + 边框8 + 外边距12 + 卡片边框1 + 列宽146 ≈ L+167
$bx = $r.L + 167
$by = $r.T + 184   # 标题栏32+页签区56+工具栏~66+上边距12+表头半高19
Write-Host ("拖拽点: ($bx, $by)")

[U.M]::SetCursorPos($bx, $by) | Out-Null
Start-Sleep -Milliseconds 200
[U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 120
[U.M]::SetCursorPos(($bx + 60), $by) | Out-Null   # 移到 +60 处
Start-Sleep -Milliseconds 250
[U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "拖拽 +60px 完成"
Start-Sleep -Milliseconds 800

[U.M]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$j = Get-Content "$env:LOCALAPPDATA\PCActivityLog\settings.json" -Raw | ConvertFrom-Json
Write-Host "--- ColumnWidths ---"
$j.ColumnWidths.PSObject.Properties | ForEach-Object { Write-Host ("{0} = {1}" -f $_.Name, $_.Value) }
