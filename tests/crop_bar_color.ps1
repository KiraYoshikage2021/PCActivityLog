Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
Write-Host "图尺寸: $($src.Width)x$($src.Height)"
# 柱子在全屏图右侧约 x=1580-1620（1920 宽图），y=140-340
$crop = New-Object System.Drawing.Bitmap 100, 230
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,100,230), (New-Object System.Drawing.Rectangle 1570,120,100,230), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 300, 690
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, 300, 690)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\bar_color.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
