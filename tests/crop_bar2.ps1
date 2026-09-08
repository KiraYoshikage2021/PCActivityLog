Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
Write-Host "原图尺寸: $($src.Width) x $($src.Height)"
# 柱子位于原图右侧约 x=1590-1660, y=170-390（原图1920宽）
$crop = New-Object System.Drawing.Bitmap 140, 280
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,140,280), (New-Object System.Drawing.Rectangle 1560,150,140,280), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 420, 840
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 420, 840)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\bar_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
