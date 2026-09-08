Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\winui_latest.png")
$crop = New-Object System.Drawing.Bitmap 130, 260
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,130,260), (New-Object System.Drawing.Rectangle 1590,185,130,260), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 390, 780
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($crop, 0, 0, 390, 780)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\bar_soft.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
