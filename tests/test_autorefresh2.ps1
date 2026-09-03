$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient

function Get-Count {
  param($proc)
  $proc.Refresh()
  if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { return -1 }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($t in $texts) {
    if ($t.Current.Name -match '^共\s*(\d+)') { return [int]$Matches[1] }
  }
  return -1
}

$proc = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "FAIL: 窗口不可见"; exit 1 }

$n1 = Get-Count $proc
Write-Host "[读取1] 共 $n1 条"

# 唯一文件名：时间戳防撞去重表
$f = Join-Path $env:USERPROFILE ("Downloads\自动刷新验证_{0}.bin" -f (Get-Date -Format 'HHmmss'))
[System.IO.File]::WriteAllText($f, ('R' * 76800))
Write-Host "[投放] $(Split-Path $f -Leaf) @ $(Get-Date -Format HH:mm:ss)，界面零操作，等 12 秒..."
Start-Sleep -Seconds 12

$n2 = Get-Count $proc
Write-Host "[读取2] 共 $n2 条"
Write-Host "----------------------------------------"
if ($n2 -eq $n1 + 1) { Write-Host "PASS: 下载事件未操作即自动出现 ($n1 -> $n2)" }
elseif ($n2 -gt $n1) { Write-Host "PASS(含额外事件): $n1 -> $n2" }
else { Write-Host "FAIL: 计数未增加 ($n1 -> $n2)" }
Remove-Item $f -Force
