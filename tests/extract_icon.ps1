Add-Type -AssemblyName System.Drawing
$ico = 'D:\工作文件\程序项目\电脑日志记录\PCActivityLog\Assets\app.ico'
$b = [IO.File]::ReadAllBytes($ico)
$n = [BitConverter]::ToUInt16($b, 4)
Write-Host "帧数: $n"
for ($i = 0; $i -lt $n; $i++) {
    $o = 6 + 16 * $i
    $w = $b[$o]; if ($w -eq 0) { $w = 256 }
    $len = [BitConverter]::ToUInt32($b, $o + 8)
    $off = [BitConverter]::ToUInt32($b, $o + 12)
    Write-Host ("  {0}px : {1} 字节" -f $w, $len)
    if ($w -eq 16 -or $w -eq 256) {
        $png = New-Object byte[] $len
        [Array]::Copy($b, $off, $png, 0, $len)
        [IO.File]::WriteAllBytes("$env:TEMP\icon_$w.png", $png)
    }
}
# 16px 放大 16 倍便于观察
$src = [System.Drawing.Image]::FromFile("$env:TEMP\icon_16.png")
$big = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($src, 0, 0, 256, 256)
$g.Dispose(); $src.Dispose()
$big.Save("$env:TEMP\icon_16_zoom.png", [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
Write-Host "已导出 icon_256.png 与 icon_16_zoom.png"
