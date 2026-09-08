Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
# 截取整个主屏幕的任务栏区域
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host "屏幕: $($b.Width)x$($b.Height)"
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
$g.Dispose()
# 裁任务栏（底部 60px）
$bar = New-Object System.Drawing.Bitmap $b.Width, 60
$g2 = [System.Drawing.Graphics]::FromImage($bar)
$g2.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0,0,$b.Width,60), (New-Object System.Drawing.Rectangle 0,($b.Height-60),$b.Width,60), [System.Drawing.GraphicsUnit]::Pixel)
$g2.Dispose(); $bmp.Dispose()
$bar.Save("$env:TEMP\taskbar.png", [System.Drawing.Imaging.ImageFormat]::Png); $bar.Dispose()
Write-Host "OK"
