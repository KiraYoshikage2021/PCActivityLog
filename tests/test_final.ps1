$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE 'Downloads'
$f = Join-Path $dir '冒烟测试_最终版.exe'

# 模拟浏览器下载：写文件 + MotW
[System.IO.File]::WriteAllText($f, ('M' * 409600))
$zone = "[ZoneTransfer]`r`nZoneId=3`r`nReferrerUrl=https://www.example.com/download-page`r`nHostUrl=https://cdn.example.com/files/final-test.exe"
Set-Content -Path $f -Stream Zone.Identifier -Value $zone
Write-Host "CREATED $f"

Start-Sleep -Seconds 11
Rename-Item $f -NewName '冒烟测试_最终版改名.exe'
Write-Host 'RENAMED'

Start-Sleep -Seconds 3
Remove-Item (Join-Path $dir '冒烟测试_最终版改名.exe') -Force
Write-Host 'DELETED'
Start-Sleep -Seconds 5
