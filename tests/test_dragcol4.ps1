Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name M -Namespace U

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { & "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"; Start-Sleep 3; $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 600

# 视觉模型定位（栅格423,254 → 物理846,508），在附近做 3x3 精扫拖拽
$points = @()
foreach ($dx in -4, 0, 4) { foreach ($dy in -4, 0, 4) { $points += ,@((846 + $dx), (508 + $dy)) } }

$i = 0
foreach ($pt in $points) {
  $i++
  $bx = $pt[0]; $by = $pt[1]
  [U.M]::SetCursorPos($bx, $by) | Out-Null
  Start-Sleep -Milliseconds 120
  [U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [U.M]::SetCursorPos(($bx + 50), $by) | Out-Null
  Start-Sleep -Milliseconds 180
  [U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 250
}

[U.M]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$j = Get-Content "$env:LOCALAPPDATA\PCActivityLog\settings.json" -Raw | ConvertFrom-Json
Write-Host "--- ColumnWidths ---"
$j.ColumnWidths.PSObject.Properties | ForEach-Object { Write-Host ("{0} = {1}" -f $_.Name, $_.Value) }
