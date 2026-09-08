Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte sc, uint f, UIntPtr e);' -Name K -Namespace U
$p = Get-Process PCActivityLog | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[U.K]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800
# Tab 聚焦到导航区，然后方向键下移两次到"统计"
[U.K]::keybd_event(0x09, 0, 0, [UIntPtr]::Zero); [U.K]::keybd_event(0x09, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
[U.K]::keybd_event(0x28, 0, 0, [UIntPtr]::Zero); [U.K]::keybd_event(0x28, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
[U.K]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero); [U.K]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero)
Write-Host "已发送 Tab/下/回车"
Start-Sleep -Seconds 2
