Add-Type -AssemblyName UIAutomationClient
$p = Get-Process PCActivityLog | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Header)
foreach ($h in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
  $r = $h.Current.BoundingRectangle
  if ($r.Width -gt 0) { Write-Host ("列头 [{0}] 宽 {1:N0}px" -f $h.Current.Name, $r.Width) }
}
