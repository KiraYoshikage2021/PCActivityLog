$p = "$env:LOCALAPPDATA\PCActivityLog\settings.json"
$text = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
$good = [ordered]@{
  '时间' = 150; '类型' = 84; '名称' = 791
  '大小 / 版本' = 180; '路径 / 网址' = 320; '备注' = 140
}
$sb = New-Object System.Text.StringBuilder
foreach ($kv in $good.GetEnumerator()) {
  [void]$sb.AppendLine('    "' + $kv.Key + '": ' + $kv.Value + ',')
}
$replacement = '"ColumnWidths": {' + "`n" + $sb.ToString().TrimEnd(",`r`n".ToCharArray()) + "`n  }"
$text = [regex]::Replace($text, '"ColumnWidths":\s*\{[^}]*\}', $replacement)
[IO.File]::WriteAllText($p, $text, (New-Object Text.UTF8Encoding $false))
Write-Host 'ColumnWidths 已清理'
