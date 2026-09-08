# 验证退出链路：把"关闭时最小化到托盘"设为否，点 X 应触发 ExitApplication
$p = "$env:LOCALAPPDATA\PCActivityLog\settings.json"
$j = Get-Content $p -Raw | ConvertFrom-Json
$j.MinimizeToTrayOnClose = $false
$j | ConvertTo-Json -Depth 6 | Set-Content $p -Encoding UTF8
Write-Host "已设置：点 X 时退出程序"
