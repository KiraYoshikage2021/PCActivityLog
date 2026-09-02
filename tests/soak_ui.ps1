$ErrorActionPreference = 'SilentlyContinue'
$proc = Get-Process PCActivityLog | Select-Object -First 1
Write-Host ("起始: 内存 {0:N0} MB | 句柄 {1} | 线程 {2}" -f ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
for ($round = 1; $round -le 6; $round++) {
    # 每轮制造活动：文件增删 + 注册表键增删（含数据表刷新路径）
    for ($i = 1; $i -le 5; $i++) {
        $f = Join-Path $env:USERPROFILE "Downloads\美化浸泡_$round`_$i.dat"
        [System.IO.File]::WriteAllText($f, ('S' * (30KB + $i)))
        Start-Sleep -Milliseconds 300
    }
    $key = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UiSoak$round"
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name DisplayName -Value "美化浸泡软件$round" -PropertyType String -Force | Out-Null
    Start-Sleep -Seconds 30
    Remove-Item $key -Force
    Get-ChildItem "$env:USERPROFILE\Downloads\美化浸泡_*.dat" | Remove-Item -Force
    Start-Sleep -Seconds 20
    $proc.Refresh()
    Write-Host ("第{0}轮: 内存 {1:N0} MB | 句柄 {2} | 线程 {3}" -f $round, ($proc.WorkingSet64/1MB), $proc.HandleCount, $proc.Threads.Count)
}
