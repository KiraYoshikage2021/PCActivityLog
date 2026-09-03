$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);' -Name W -Namespace U

function Get-Count {
  param($h)
  if ($h -eq [IntPtr]::Zero) { return -1 }
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  foreach ($t in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    if ($t.Current.Name -match '^共\s*(\d+)') { return [int]$Matches[1] }
  }
  return -1
}

$p = Get-Process PCActivityLog | Select-Object -First 1
$h = $p.MainWindowHandle
$n1 = Get-Count $h
Write-Host "[步骤1] 窗口可见，共 $n1 条"

# 隐藏到托盘（模拟点关闭按钮）
[U.W]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
$p.Refresh()
Write-Host "[步骤2] 已发送关闭（应隐藏到托盘），句柄: $($p.MainWindowHandle -ne 0)"

# 隐藏期间投放文件
$f = Join-Path $env:USERPROFILE ("Downloads\重开验证_{0}.bin" -f (Get-Date -Format HHmmss))
[System.IO.File]::WriteAllText($f, ('H' * 30720))
Write-Host "[步骤3] 隐藏期间投放 $(Split-Path $f -Leaf)，等 10 秒入库..."
Start-Sleep -Seconds 10

# 第二实例激活信号 → 窗口重新显示
Start-Process "$f" -ErrorAction SilentlyContinue | Out-Null
& "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"
Start-Sleep -Seconds 3

$p.Refresh()
$h2 = $p.MainWindowHandle
$n2 = Get-Count $h2
Write-Host "[步骤4] 重开窗口后，共 $n2 条"
Write-Host "----------------------------------------"
if ($n2 -ge $n1 + 1) { Write-Host "PASS: 重开窗口即见新记录 ($n1 -> $n2)" }
else { Write-Host "FAIL: $n1 -> $n2" }
Remove-Item $f -Force
