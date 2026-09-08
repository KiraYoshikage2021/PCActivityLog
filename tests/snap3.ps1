Add-Type -AssemblyName System.Drawing
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L; public int T; public int R; public int B; }' -Name W -Namespace U
$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$h = $p.MainWindowHandle; $r = New-Object U.W+RECT
[U.W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R-$r.L), ($r.B-$r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[U.W]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save("$env:TEMP\winui_settings3.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host '已截图设置页'
