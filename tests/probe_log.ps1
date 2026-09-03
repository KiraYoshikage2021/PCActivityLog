$p = "$env:LOCALAPPDATA\PCActivityLog\diag.log"
try {
  [System.IO.File]::AppendAllText($p, "external-probe @ $(Get-Date -Format 'HH:mm:ss')`n")
  Write-Host '外部追加成功：文件未被锁'
} catch { Write-Host "外部追加失败: $($_.Exception.Message)" }
Get-Item $p | ForEach-Object { Write-Host "大小: $($_.Length) 字节, 最后写入: $($_.LastWriteTime)" }
