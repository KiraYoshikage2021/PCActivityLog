Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
# 裁剪右侧柱子区域并放大
$crop = New-Object System.Drawing.Bitmap 200, 300
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,200,300), (New-Object System.Drawing.Rectangle 1040,100,200,300), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 400, 600
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 400, 600)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\bar_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
