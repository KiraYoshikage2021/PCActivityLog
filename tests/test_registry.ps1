$ErrorActionPreference = 'Stop'
$key = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PCLogSmokeTest'

Write-Host "[1] 添加假软件键（版本 1.0）..."
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value '冒烟测试软件' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayVersion -Value '1.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name Publisher -Value '测试公司' -PropertyType String -Force | Out-Null

Write-Host "    等待 25 秒（轮询 10s + 复核）..."
Start-Sleep -Seconds 25

Write-Host "[2] 改版本号 1.0 -> 2.5（触发更新事件）..."
New-ItemProperty -Path $key -Name DisplayVersion -Value '2.5' -PropertyType String -Force | Out-Null
Start-Sleep -Seconds 25

Write-Host "[3] 删除假软件键（触发卸载事件）..."
Remove-Item -Path $key -Force
Start-Sleep -Seconds 25

Write-Host "完成"
