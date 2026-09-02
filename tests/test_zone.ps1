$f = Join-Path $env:USERPROFILE 'Downloads\zone检查测试.exe'
[System.IO.File]::WriteAllText($f, 'zzz')
$zone = "[ZoneTransfer]`r`nZoneId=3`r`nReferrerUrl=https://www.example.com/download-page`r`nHostUrl=https://cdn.example.com/files/zone-test.exe"
Set-Content -Path $f -Stream Zone.Identifier -Value $zone
Write-Host '--- ADS 内容 ---'
Get-Content -Path $f -Stream Zone.Identifier | ForEach-Object { Write-Host "[$_]" }
Write-Host '--- cmd dir /r 形式 ---'
cmd /c "dir /r `"$f`"" | Select-Object -Last 3
