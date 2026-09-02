$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE 'Downloads'
$f = Join-Path $dir '冒烟关联测试_Setup.exe'
[System.IO.File]::WriteAllText($f, ('L'*10240))
Set-Content -Path $f -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`nHostUrl=https://example.com/冒烟关联测试_Setup.exe"
Write-Host "下载文件已创建，等待入库..."
Start-Sleep -Seconds 10
$key = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PCLogLinkTest'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value '冒烟关联测试' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayVersion -Value '1.0' -PropertyType String -Force | Out-Null
Write-Host "注册表键已添加，等待安装事件..."
Start-Sleep -Seconds 25
Remove-Item $f -Force
Remove-Item $key -Force
Write-Host "清理完成"
