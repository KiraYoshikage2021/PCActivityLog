$ErrorActionPreference = 'Stop'
$key = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PCLogSmokeTest2'

Write-Host "[1] 添加假软件键（版本 3.0）@ $(Get-Date -Format HH:mm:ss)"
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value 'WinUI冒烟软件' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayVersion -Value '3.0' -PropertyType String -Force | Out-Null
Write-Host "    等待 75 秒（30s轮询 × 2轮复核 + 余量）..."
Start-Sleep -Seconds 75

Write-Host "[2] 改版本 3.0 -> 4.0 @ $(Get-Date -Format HH:mm:ss)"
New-ItemProperty -Path $key -Name DisplayVersion -Value '4.0' -PropertyType String -Force | Out-Null
Start-Sleep -Seconds 75

Write-Host "[3] 删除键（触发卸载）@ $(Get-Date -Format HH:mm:ss)"
Remove-Item -Path $key -Force
Start-Sleep -Seconds 75
Write-Host "完成 @ $(Get-Date -Format HH:mm:ss)"
