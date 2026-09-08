$ErrorActionPreference = 'SilentlyContinue'
$proc = Get-Process PCActivityLog | Select-Object -First 1
Write-Host ("起始: 内存 {0:N0} MB | 句柄 {1} | 线程 {2}" -f ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
for ($round = 1; $round -le 6; $round++) {
    # 每轮制造活动：文件 + 注册表键
    for ($i = 1; $i -le 4; $i++) {
        $f = Join-Path $env:USERPROFILE "Downloads\WinUI浸泡_$round`_$i.dat"
        [System.IO.File]::WriteAllText($f, ('S' * (40KB + $i)))
        Start-Sleep -Milliseconds 300
    }
    $key = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinUiSoak$round"
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name DisplayName -Value "WinUI浸泡软件$round" -PropertyType String -Force | Out-Null
    Start-Sleep -Seconds 35
    Remove-Item $key -Force
    Get-ChildItem "$env:USERPROFILE\Downloads\WinUI浸泡_*.dat" | Remove-Item -Force
    Start-Sleep -Seconds 20
    $proc.Refresh()
    Write-Host ("第{0}轮: 内存 {1:N0} MB | 句柄 {2} | 线程 {3}" -f $round, ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
}
Write-Host "浸泡测试完成"
