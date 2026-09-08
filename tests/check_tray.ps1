Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
$tray = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$btns = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$target = $null
foreach ($b in $btns) { if ($b.Current.Name -match 'PCActivityLog') { $target = $b; break } }
if (-not $target) { Write-Host "FAIL: 未找到托盘图标"; exit 1 }

$r = $target.Current.BoundingRectangle
Write-Host "托盘图标位置: ($($r.X),$($r.Y)) 尺寸 $($r.Width)x$($r.Height)"

# 检查该元素是否支持右键菜单（UIA 的 ContextMenu 能力）
Write-Host "支持的交互模式:"
$target.GetSupportedPatterns() | ForEach-Object { Write-Host "  $_" }
