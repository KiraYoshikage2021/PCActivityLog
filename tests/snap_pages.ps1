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

$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[U.W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 600

# 点"统计"导航项（左侧第二个）
[U.W]::SetCursorPos(60, 340) | Out-Null
Start-Sleep -Milliseconds 200
[U.W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [U.W]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 2
Snap 'winui_stats'

# 点"设置"导航项（左侧第三个）
[U.W]::SetCursorPos(60, 400) | Out-Null
Start-Sleep -Milliseconds 200
[U.W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [U.W]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Seconds 2
Snap 'winui_settings'
