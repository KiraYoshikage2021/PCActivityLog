# =====================================================================
# installer/build.ps1 —— 一键构建安装包
#   1. dotnet publish（自包含 → installer\publish）
#   2. ISCC 编译 PCActivityLog.iss → installer\dist\PCActivityLog-Setup-<版本>.exe
# 前置：Inno Setup 6（winget install JRSoftware.InnoSetup）；
#       中文语言包首次构建时自动下载到本目录
# 用法：powershell -ExecutionPolicy Bypass -File installer\build.ps1
# =====================================================================
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Root = Split-Path -Parent $PSScriptRoot

# ---- 定位 ISCC ----
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "找不到 ISCC.exe（Inno Setup 6）。请先安装：winget install JRSoftware.InnoSetup" }

# ---- 中文语言包（Inno 官方安装不带简中，首次自动下载到脚本目录）----
$isl = Join-Path $PSScriptRoot "ChineseSimplified.isl"
if (-not (Test-Path $isl)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = "https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/ChineseSimplified.isl"
    Invoke-WebRequest -Uri $url -OutFile $isl
    if ((Get-Item $isl).Length -lt 10KB) {
        throw "中文语言包下载异常，请手动从 https://jrsoftware.org/files/istrans/ 下载 ChineseSimplified.isl 放到 installer 目录"
    }
}

# ---- 发布（自包含，与 README 文档的命令一致）----
$pub = Join-Path $PSScriptRoot "publish"
dotnet publish (Join-Path $Root "PCActivityLog\PCActivityLog.csproj") -c $Configuration -r win-x64 -p:WindowsAppSDKSelfContained=true -o $pub
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

# ---- 版本号取自 csproj（单一事实来源）----
# -Encoding UTF8 必须：csproj 是无 BOM 的 UTF-8，PS5.1 默认按 ANSI 解码会吞掉中文后的引号
[xml]$csproj = Get-Content (Join-Path $Root "PCActivityLog\PCActivityLog.csproj") -Raw -Encoding UTF8
$ver = ($csproj.Project.PropertyGroup.Version | Select-Object -First 1)

# ---- 编译安装包 ----
& $iscc "/DMyAppVersion=$ver" (Join-Path $PSScriptRoot "PCActivityLog.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败（退出码 $LASTEXITCODE）" }

$setup = Join-Path $PSScriptRoot "dist\PCActivityLog-Setup-$ver.exe"
if (-not (Test-Path $setup)) { throw "未找到安装包输出：$setup" }
Write-Host ""
Write-Host "安装包已生成：$setup" -ForegroundColor Green
