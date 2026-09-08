Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
# 裁剪左上角标题栏区域（0,0 到 300,60）并放大 3 倍
$crop = New-Object System.Drawing.Bitmap 300, 60
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,300,60), (New-Object System.Drawing.Rectangle 0,0,300,60), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 900, 180
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 900, 180)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\titlebar_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
