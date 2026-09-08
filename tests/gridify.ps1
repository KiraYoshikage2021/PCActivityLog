$p = 'D:\工作文件\程序项目\电脑日志记录\PCActivityLog\Views\SettingsPage.xaml'
$lines = [IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)
# 卡片注释行索引 -> (row, col)
$cards = @{ 21 = @(0,0); 39 = @(0,1); 69 = @(1,0); 91 = @(1,1); 102 = @(2,0); 119 = @(2,1); 136 = @(3,0) }
$out = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    [void]$out.Add($line)
    if ($cards.ContainsKey($i)) {
        $r = $cards[$i][0]; $c = $cards[$i][1]
        $border = $lines[$i+1]
        $border = $border.Replace('<Border Background=', ('<Border Grid.Row="' + $r + '" Grid.Column="' + $c + '" Background='))
        [void]$out.Add($border)
        $i++  # 跳过原 Border 行
    }
}
[IO.File]::WriteAllLines($p, $out, (New-Object Text.UTF8Encoding $false))
Write-Host "已给 7 个卡片分配 Grid 位置"
