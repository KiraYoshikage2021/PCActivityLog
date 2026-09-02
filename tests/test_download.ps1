# 冒烟测试：下载 / 删除 / 重命名
$ErrorActionPreference = 'Stop'
$f = "$env:USERPROFILE\Downloads\冒烟测试_下载样本.exe"

# 1. 模拟浏览器下载：写文件 + 附加 Zone.Identifier（MotW）
[System.IO.File]::WriteAllText($f, ('X' * 204800))
Set-Content -Path $f -Stream Zone.Identifier -Value "[ZoneTransfer]`nZoneId=3`nReferrerUrl=https://www.example.com/download-page`nHostUrl=https://cdn.example.com/files/smoke-test.exe"
Write-Host "[1] 已创建下载测试文件: $f"

Start-Sleep -Seconds 11

# 2. 模拟重命名
Rename-Item -Path $f -NewName '冒烟测试_改名后.exe'
Write-Host "[2] 已重命名 -> 冒烟测试_改名后.exe"

Start-Sleep -Seconds 3

# 3. 模拟删除
Remove-Item "$env:USERPROFILE\Downloads\冒烟测试_改名后.exe" -Force
Write-Host "[3] 已删除"

Start-Sleep -Seconds 4
Write-Host "完成，等待 2 秒后查询数据库"
Start-Sleep -Seconds 2
