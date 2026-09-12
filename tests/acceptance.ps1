# =====================================================================
# tests/acceptance.ps1 —— 可重复执行的验收入口（替代一次性手工冒烟）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tests\acceptance.ps1             # 全量
#   powershell -ExecutionPolicy Bypass -File tests\acceptance.ps1 -SkipBuild  # 跳过构建
#   powershell -ExecutionPolicy Bypass -File tests\acceptance.ps1 -SkipTests  # 跳过单元测试
#
# 检查项：
#   1. Release 构建（0 错误）
#   2. 单元测试（xUnit，PCActivityLog.Tests）
#   3. 构建产物存在
#   4. 文档一致性守卫：README 不得再出现已移除功能/不存在文件的描述
#      （防止"代码删了、文档还在宣传"的漂移再次发生）
#   5. 单实例冒烟：仅当已有实例在运行时执行——启动构建产物，
#      验证它走激活路径快速退出（同时覆盖 WinUI XAML 资源加载）
#   6. diag.log 错误扫描（历史累计 ERROR，供排查参考）
#
# 注意：含中文的 .ps1 必须存成带 BOM 的 UTF-8（见 README）。
# =====================================================================
param([switch]$SkipBuild, [switch]$SkipTests)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8  # 让 dotnet 的中文输出在控制台不乱码
$Root = Split-Path -Parent $PSScriptRoot
$Proj = Join-Path $Root "PCActivityLog"
$script:FailCount = 0

function Step($m) { Write-Host "`n== $m ==" -ForegroundColor Cyan }
function Pass($m) { Write-Host "  [PASS] $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:FailCount++ }
function Warn($m) { Write-Host "  [WARN] $m" -ForegroundColor Yellow }

# ---------- 1. 构建 ----------
if ($SkipBuild) {
    Step "1/6 构建（-SkipBuild 跳过）"
} else {
    Step "1/6 构建（dotnet build -c Release）"
    dotnet build (Join-Path $Proj "PCActivityLog.csproj") -c Release --nologo -v q
    if ($LASTEXITCODE -eq 0) { Pass "构建成功（0 错误）" } else { Fail "构建失败（退出码 $LASTEXITCODE）" }
}
if ($script:FailCount -gt 0) { Write-Host "`n构建失败，终止后续检查。" -ForegroundColor Red; exit 1 }

# ---------- 2. 单元测试 ----------
if ($SkipTests) {
    Step "2/6 单元测试（-SkipTests 跳过）"
} else {
    Step "2/6 单元测试（dotnet test）"
    dotnet test (Join-Path $Root "PCActivityLog.Tests\PCActivityLog.Tests.csproj") -c Release --nologo
    if ($LASTEXITCODE -eq 0) { Pass "全部单元测试通过" } else { Fail "单元测试失败（退出码 $LASTEXITCODE）" }
}

# ---------- 3. 构建产物 ----------
Step "3/6 构建产物"
$Exe = Join-Path $Proj "bin\Release\net8.0-windows10.0.19041.0\win-x64\PCActivityLog.exe"
if (Test-Path $Exe) { Pass "PCActivityLog.exe 存在" } else { Fail "找不到 $Exe" }

# ---------- 4. 文档一致性守卫 ----------
Step "4/6 文档一致性守卫（README vs 代码现状）"
$Readme = Get-Content (Join-Path $Root "README.md") -Raw
# 每条守卫 = 一条"当前为真的代码事实"。将来若功能回归，应有意识地更新对应守卫。
$Guards = [ordered]@{
    "已移除的采集器 BrowserHistoryWatcher.cs 不应出现在 README" = "BrowserHistoryWatcher"
    "不存在的 WatcherTest/ 目录不应出现在 README"               = "WatcherTest"
    "不存在的『右键编辑备注』功能不应出现在 README"             = "右键编辑备注"
    "已移除的『浏览记录』功能不应出现在 README"                 = "浏览记录"
}
foreach ($g in $Guards.Keys) {
    if ($Readme -match [regex]::Escape($Guards[$g])) { Fail "$g（仍命中『$($Guards[$g])』）" }
    else { Pass $g }
}

# ---------- 5. 单实例冒烟 ----------
Step "5/6 单实例冒烟"
$Running = Get-Process -Name "PCActivityLog" -ErrorAction SilentlyContinue
if ($Running) {
    $firstPid = ($Running | Select-Object -First 1).Id
    Warn "检测到已运行实例（PID $firstPid），执行第二实例启动测试；其主窗口可能被激活（单实例设计行为）"
    $Sw = [System.Diagnostics.Stopwatch]::StartNew()
    $P = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden
    $ExitedInTime = $P.WaitForExit(30000)
    $Sw.Stop()
    if (-not $ExitedInTime) {
        Stop-Process -Id $P.Id -Force -ErrorAction SilentlyContinue
        Fail "第二实例 30 秒未退出（单实例路径失效？）"
    } elseif ($P.ExitCode -ne 0) {
        Fail "第二实例退出码非 0：$($P.ExitCode)"
    } elseif ($Sw.ElapsedMilliseconds -gt 15000) {
        Warn "第二实例退出耗时 $([int]$Sw.ElapsedMilliseconds)ms（偏慢，但通过）"
    } else {
        Pass "第二实例经激活路径在 $([int]$Sw.ElapsedMilliseconds)ms 内以退出码 0 退出（XAML 资源加载正常）"
    }
} else {
    Warn "当前无运行实例，跳过启动冒烟（脚本不代替用户拉起常驻进程）；可先手动启动程序再重跑本脚本"
}

# ---------- 6. diag.log 错误扫描 ----------
Step "6/6 diag.log 错误扫描"
$Diag = Join-Path $env:LOCALAPPDATA "PCActivityLog\diag.log"
if (Test-Path $Diag) {
    $Errs = @(Select-String -Path $Diag -Pattern "\[ERROR")
    if ($Errs.Count -eq 0) {
        Pass "diag.log 无 ERROR 记录"
    } else {
        Warn "diag.log 含 $($Errs.Count) 条 ERROR（历史累计，仅供排查参考），最近 3 条："
        $Errs | Select-Object -Last 3 | ForEach-Object { Write-Host "    $($_.Line)" -ForegroundColor DarkGray }
    }
} else {
    Warn "未找到 diag.log（程序可能从未在本机运行过）"
}

# ---------- 结果 ----------
Write-Host ""
if ($script:FailCount -eq 0) {
    Write-Host "验收通过：全部必检项 PASS。" -ForegroundColor Green
    exit 0
} else {
    Write-Host "验收失败：$($script:FailCount) 项 FAIL。" -ForegroundColor Red
    exit 1
}
