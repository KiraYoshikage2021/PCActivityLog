Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\taskbar.png")
# 任务栏中间偏右是应用图标区，裁 x=600-1000 放大
$crop = New-Object System.Drawing.Bitmap 400, 60
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,400,60), (New-Object System.Drawing.Rectangle 600,0,400,60), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 1200, 180
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 1200, 180)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\taskbar_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
