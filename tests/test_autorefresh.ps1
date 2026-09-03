$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient

function Get-StatusText {
  param($proc)
  $proc.Refresh()
  if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { return "(窗口句柄为0)" }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($t in $texts) { if ($t.Current.Name -match '^共') { return $t.Current.Name } }
  return "(未找到状态栏)"
}

$proc = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Host "FAIL: 窗口不可见，先用第二实例激活"; exit 1 }

$s1 = Get-StatusText $proc
Write-Host "[读取1] $s1"

# 投放下载测试文件（模拟下载落盘）
$f = Join-Path $env:USERPROFILE 'Downloads\自动刷新验证.bin'
[System.IO.File]::WriteAllText($f, ('R' * 76800))
Write-Host "[投放] 文件已创建 $(Get-Date -Format HH:mm:ss)，不做任何界面操作，等 14 秒..."

Start-Sleep -Seconds 14

$s2 = Get-StatusText $proc
Write-Host "[读取2] $s2"

# 对比数字
$n1 = [int]([regex]::Match($s1, '共\s*(\d+)').Groups[1].Value)
$n2 = [int]([regex]::Match($s2, '共\s*(\d+)').Groups[1].Value)
Write-Host "----------------------------------------"
if ($n2 -gt $n1) { Write-Host "PASS: 计数 $n1 -> $n2，新事件未操作即自动出现" }
elseif ($s2 -eq "(未找到状态栏)" -or $s2 -eq "(窗口句柄为0)") { Write-Host "SKIP: $s2" }
else { Write-Host "FAIL: 计数未变化 ($n1 -> $n2)" }

Remove-Item $f -Force
