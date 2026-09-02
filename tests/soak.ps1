$ErrorActionPreference = 'SilentlyContinue'
$proc = Get-Process PCActivityLog | Where-Object { $_.Id -eq 26348 }
if (-not $proc) { $proc = Get-Process PCActivityLog | Select-Object -First 1 }
Write-Host ("起始: 内存 {0:N0} MB | 句柄 {1} | 线程 {2}" -f ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
for ($round = 1; $round -le 8; $round++) {
    # 每轮制造活动：5 个文件下载 + 1 次注册表键增删
    for ($i = 1; $i -le 5; $i++) {
        $f = Join-Path $env:USERPROFILE "Downloads\浸泡采样_$round`_$i.dat"
        [System.IO.File]::WriteAllText($f, ('S' * (50KB + $i)))
        Start-Sleep -Milliseconds 300
    }
    $key = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SoakTest$round"
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name DisplayName -Value "浸泡测试软件$round" -PropertyType String -Force | Out-Null
    Start-Sleep -Seconds 40
    Remove-Item $key -Force
    Get-ChildItem "$env:USERPROFILE\Downloads\浸泡采样_*.dat" | Remove-Item -Force
    Start-Sleep -Seconds 20
    $proc.Refresh()
    Write-Host ("第{0}轮: 内存 {1:N0} MB | 句柄 {2} | 线程 {3}" -f $round, ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
}
