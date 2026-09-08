Add-Type -AssemblyName System.Drawing
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); public struct RECT { public int L; public int T; public int R; public int B; }' -Name W -Namespace U

function Snap($name) {
  $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  $h = $p.MainWindowHandle
  $r = New-Object U.W+RECT
  [U.W]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r.R-$r.L), ($r.B-$r.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [U.W]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save("$env:TEMP\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Host "已截图 $name"
}

function ClickNav($y) {
  $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  [U.W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
  # 窗口在屏幕 (0,0) 附近，导航项 x≈60（物理），窗口内 y 换算成屏幕坐标
  [U.W]::SetCursorPos(60, $y) | Out-Null
  Start-Sleep -Milliseconds 250
  [U.W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [U.W]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Seconds 2
}

# 导航项屏幕坐标（窗口顶部 y=0，项高约 44，第一项 y≈100）
ClickNav 145   # 统计
Snap 'winui_stats2'
ClickNav 190   # 设置
Snap 'winui_settings2'
