Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("$env:TEMP\taskbar.png")
Write-Host "任务栏图尺寸: $($src.Width)x$($src.Height)"
# 应用图标在 x≈870-1000 区间（从第一张图看，ZCode 图标在约 x=890）
$crop = New-Object System.Drawing.Bitmap 300, 60
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,300,60), (New-Object System.Drawing.Rectangle 850,0,300,60), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()
$big = New-Object System.Drawing.Bitmap 1200, 240
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, 1200, 240)
$g2.Dispose(); $crop.Dispose()
$big.Save("$env:TEMP\taskbar_zoom2.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "OK"
