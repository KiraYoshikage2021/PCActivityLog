# 独立验证 FileSystemWatcher 在 Downloads 是否工作
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = "$env:USERPROFILE\Downloads"
$watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName -bor [System.IO.NotifyFilters]::Size -bor [System.IO.NotifyFilters]::LastWrite
$watcher.EnableRaisingEvents = $true

$events = New-Object System.Collections.ArrayList
Register-ObjectEvent $watcher Created -Action { $null = $global:fswEvents.Add("Created: $($EventArgs.FullPath)") } | Out-Null
Register-ObjectEvent $watcher Renamed -Action { $null = $global:fswEvents.Add("Renamed: $($EventArgs.OldFullPath) -> $($EventArgs.FullPath)") } | Out-Null
Register-ObjectEvent $watcher Deleted -Action { $null = $global:fswEvents.Add("Deleted: $($EventArgs.FullPath)") } | Out-Null

$global:fswEvents = $events

$f = "$env:USERPROFILE\Downloads\fsw独立测试.tmp.txt"
[System.IO.File]::WriteAllText($f, 'hello')
Start-Sleep -Milliseconds 800
Rename-Item $f -NewName 'fsw独立测试2.tmp.txt'
Start-Sleep -Milliseconds 800
Remove-Item "$env:USERPROFILE\Downloads\fsw独立测试2.tmp.txt" -Force
Start-Sleep -Seconds 3

Write-Host "捕获到 $($global:fswEvents.Count) 个事件:"
$global:fswEvents | ForEach-Object { Write-Host "  $_" }
