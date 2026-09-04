Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name M -Namespace U

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[U.M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400

function Try-Drag($x, $y) {
  [U.M]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 150
  [U.M]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 100
  foreach ($dx in 10,10,10,10,10,10) { # +60
    [U.M]::mouse_event(1, $dx, 0, 0, [UIntPtr]::Zero)
    [U.M]::SetCursorPos($x, $y) | Out-Null # 重定位再相对移：等效绝对累计
    Start-Sleep -Milliseconds 30
  }
  # 直接把光标放到 x+60 再抬起，确保列宽增量确定
  [U.M]::SetCursorPos(($x + 60), $y) | Out-Null
  Start-Sleep -Milliseconds 100
  [U.M]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 400
}

# 依次尝试候选边界坐标，成功（时间列宽变为 ~206）即停
foreach ($cand in @((866,530),(871,528),(861,532),(866,520),(876,534))) {
  Try-Drag $cand[0] $cand[1]
  # 读取当前 时间 列实际宽度：关一次窗触发保存再读文件？太重——先全部试完再统一验证
}
Start-Sleep -Milliseconds 800
[U.M]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Write-Host "--- ColumnWidths ---"
$j = Get-Content "$env:LOCALAPPDATA\PCActivityLog\settings.json" -Raw | ConvertFrom-Json
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$j.ColumnWidths.PSObject.Properties | ForEach-Object { Write-Host ("{0} = {1}" -f $_.Name, $_.Value) }
