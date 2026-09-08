Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
# 2576 宽，柱子约在 x=2120-2170（从缩略图比例推算：1620/1920*2576）
$crop = New-Object System.Drawing.Bitmap 130, 300
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,130,300), (New-Object System.Drawing.Rectangle 2100,160,130,300), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 390, 900
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, 390, 900)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\bar_color2.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
