Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\taskbar.png")
# 我们的图标在约 x=1495-1535（从上一张图：第2个图标位置）
$crop = New-Object System.Drawing.Bitmap 60, 60
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,60,60), (New-Object System.Drawing.Rectangle 1490,0,60,60), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 480, 480
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, 480, 480)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\our_icon_taskbar.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
