Add-Type -AssemblyName UIAutomationClient
Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -Name W -Namespace U
$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { & "D:\工作文件\程序项目\电脑日志记录\PCActivityLog\bin\Release\net8.0-windows\PCActivityLog.exe"; Start-Sleep 3; $p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 }
[U.W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Thumb)
$thumbs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Host "找到 $($thumbs.Count) 个拖拽抓手:"
foreach ($t in $thumbs) {
  $r = $t.Current.BoundingRectangle
  if ($r.Width -gt 0) { Write-Host ("抓手 中心=({0:N0},{1:N0}) 宽{2:N0}" -f ($r.X + $r.Width/2), ($r.Y + $r.Height/2), $r.Width) }
}
