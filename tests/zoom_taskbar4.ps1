Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\taskbar.png")
# 更右侧区域 x=1450-1900
$crop = New-Object System.Drawing.Bitmap 450, 60
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,450,60), (New-Object System.Drawing.Rectangle 1450,0,450,60), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 1350, 180
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 1350, 180)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\taskbar_zoom4.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
