Add-Type -AssemblyName System.Drawing
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); public struct RECT { public int L; public int T; public int R; public int B; }' -Name W -Namespace U

# 2026-09 起导航栏已移除：主窗口直接显示时间线，设置从筛选工具栏右侧齿轮进入。
# 本脚本截 时间线 + 设置 两张图；齿轮坐标按窗口矩形推算，不同 DPI/窗口尺寸下可能需要微调。

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

function Get-MainRect {
  $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  [U.W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 600
  $r = New-Object U.W+RECT
  [U.W]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  return $r
}

# 1. 时间线（默认首页）
$r = Get-MainRect
Snap 'winui_timeline'

# 2. 点筛选工具栏右侧的齿轮 → 设置页
#    齿轮在工具栏按钮排最右：x ≈ 窗口右缘-50，y ≈ 顶部+标题栏(32)+页边距(16)+工具栏中心(28)
[U.W]::SetCursorPos($r.R - 50, $r.T + 76) | Out-Null
Start-Sleep -Milliseconds 250
[U.W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [U.W]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 2
Snap 'winui_settings'
