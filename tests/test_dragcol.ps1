Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);' -Name M -Namespace U

# 时间|类型 列边界（窗口 700,346 起：卡片边距+146 列宽 → x≈846；表头行 y≈534）
$x = 846; $y = 534
[U.M]::SetCursorPos($x, $y) | Out-Null
Start-Sleep -Milliseconds 200
[U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
Start-Sleep -Milliseconds 120
for ($i = 0; $i -lt 6; $i++) { [U.M]::mouse_event(1, 10, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }  # 相对右移 60px
Start-Sleep -Milliseconds 150
[U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
Write-Host "拖拽完成 (+60px)"

Start-Sleep -Milliseconds 600
# 关窗（隐藏到托盘，顺路触发列宽保存）
$p = Get-Process PCActivityLog | Select-Object -First 1
[U.M]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Write-Host "--- settings.json 中的 ColumnWidths ---"
$j = Get-Content "$env:LOCALAPPDATA\PCActivityLog\settings.json" -Raw | ConvertFrom-Json
$j.ColumnWidths.PSObject.Properties | ForEach-Object { Write-Host ("{0} = {1}" -f $_.Name, $_.Value) }
