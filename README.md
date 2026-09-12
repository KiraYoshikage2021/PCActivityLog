# 电脑日志记录（PCActivityLog）

一个 Windows 常驻托盘小工具，自动记录这台电脑上发生的事：下载了哪些文件、装了/更新了/卸载了哪些软件、微信/QQ 收到过哪些文件、什么时候开的机关的机。所有数据只存在本机（`%LOCALAPPDATA%\PCActivityLog\`），不联网、不上传。

## 功能一览

| 记录内容 | 说明 |
|---|---|
| 📥 下载 | 监视"下载"等文件夹（可配置多个），文件写入完成才算一次下载；自动读取 Windows 的 Mark of the Web 标记，记录**来源网址** |
| 🗑 删除 / ✏️ 重命名 | 监视文件夹内的文件删除和重命名事件 |
| 💬 微信/QQ 文件 | 自动识别微信（xwechat_files / WeChat Files）与 QQ（Tencent Files）的文件存储目录，记录收到的每个文件，并标注**✔ 已保存 / ⚠ 已清理**（文件被 IM 存储管理清理后状态自动更新）；支持自定义目录兜底 |
| 📦 安装 / ⬆️ 更新 / 🗑 卸载 | 双通道检测：MSI 安装事件日志（带版本号）+ 注册表卸载键对比（覆盖 EXE 安装器和绿色软件），带防误报复核；同产品同版本的重复安装（修复/重跑，典型如 VS 内嵌的 Workloads MSI）自动跳过 |
| 💻 开机 / 关机 | 从系统事件日志读取，程序没运行期间的开关机也会在下次启动时补录 |
| ✍️ 手动记录 | 界面上随时补录自定义事件 |
| 🔗 下载↔安装关联 | 安装事件自动回溯匹配最近的下载，显示"由哪个安装包装的" |

每个功能模块都可以在**设置**里随时开关（立即生效，不用重启程序）。

## 界面

- **时间线（主窗口）**：窗口直接显示时间线（无导航栏），按类型筛选、关键词搜索（含备注）、日期范围；类型列为彩色胶囊徽章；双击打开文件位置或跳转关联事件；右键菜单（打开文件位置 / 跳到来源安装包 / 复制名称）；导出 CSV / JSON；右上角齿轮进入设置
- **托盘图标**：双击打开主窗口；右键菜单可快速打开设置、开机自启、退出
- **自动跟随 Windows 浅色/深色主题**：和 Windows 11 原生应用一样，系统切换深色模式时本软件实时跟随（包括系统标题栏一起变深）；也可在设置里强制浅色/深色

## 如何使用

### 方式一：安装包（推荐）

一键构建 Inno Setup 安装包（前置：安装 Inno Setup 6，`winget install JRSoftware.InnoSetup`）：

```bash
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

产物 `installer\dist\PCActivityLog-Setup-<版本>.exe`（约 60 MB）：按当前用户安装（免管理员）、
自动创建开始菜单/桌面快捷方式；**重跑新版本安装包即原位升级**（用户数据不受影响）；
升级与卸载前会通过程序自身的退出信号优雅关闭（冲写数据库队列，不强杀）；
卸载时可选是否同时删除应用数据。

### 方式二：免安装目录版

构建免安装的发布目录（WinUI 3 自包含，免装运行时）：

```bash
cd PCActivityLog
dotnet publish -c Release -r win-x64 -p:WindowsAppSDKSelfContained=true -o bin/Release/publish
```

产物在 `bin\Release\publish\`（约 210 MB，含 Windows App SDK 运行时），
整个文件夹拷到别的电脑、双击 `PCActivityLog.exe` 即可用，不需要安装任何运行时。

> ⚠️ WinUI 3 必须用**目录形式**发布（不支持单文件打包）。
> 本项目 csproj 里的 `CopyXamlArtifactsOnPublish` 目标用于绕过
> `dotnet publish` 不复制 `.xbf`/`.pri` 的已知缺陷；紧随其后的
> `VerifyPublishArtifacts` 目标会在发布时校验 XAML 产物与 app.ico 确实进入发布目录，
> 缺失直接构建失败——不会把"启动即崩"的包发给用户。

### 方式三：从源码运行

用 Visual Studio 2022 打开根目录的 `PCActivityLog.sln`（或直接打开 `PCActivityLog\PCActivityLog.csproj`）按 F5，
或在命令行 `dotnet run --project PCActivityLog`。运行单元测试：`dotnet test PCActivityLog.Tests`。

### 开机自启动

托盘右键菜单勾选"开机自启动"即可（写当前用户的注册表 Run 键，不需要管理员权限）。

### 系统识别与卸载

本程序是免安装的目录形式发布，默认会把自身信息登记进当前用户的卸载注册表
（`HKCU\...\Uninstall\PCActivityLog`），这样 Windows「设置 → 应用」和
BCUninstaller 等卸载工具能正确显示"电脑日志记录"的名称、版本和图标，
而不是把文件夹误识别成别的程序。登记条目同时提供卸载入口：

- 卸载时程序会清除登记信息、关闭开机自启动，然后打开资源管理器定位到程序文件夹，**程序文件由你手动删除**；
- 可选勾选"同时删除应用数据"（日志数据库与设置）；
- 如果程序正在后台运行，会先请求它退出；
- 不想登记的话，在设置 → 通用行为里关闭"在系统中注册程序信息"，下次启动自动清除登记；
- **通过安装包安装时**，卸载条目由安装器负责（`PCActivityLog_is1`），程序检测到后自动跳过
  自我登记并清理旧条目，避免「设置 → 应用」出现重复条目；绿色目录版行为不变。

### 更换图标后缩略图没变？

Explorer 会缓存 exe 的旧图标。替换 `Assets\app.ico` 重新发布后，运行一次
`ie4uinit.exe -show`（或重启 Explorer）即可刷新缩略图缓存。

## 数据都存在哪

| 文件 | 作用 |
|---|---|
| `%LOCALAPPDATA%\PCActivityLog\activity.db` | 事件数据库（SQLite，WAL 模式） |
| `%LOCALAPPDATA%\PCActivityLog\settings.json` | 全部设置（模块开关等） |
| `%LOCALAPPDATA%\PCActivityLog\diag.log` | 诊断日志（滚动上限 5 MB，排查问题用） |

想"彻底重置"就退出程序后删掉这个文件夹。

数据保留策略：事件默认永久保留。如需自动清理过期数据，可在配置文件 settings.json 中把 `OtherRetentionDays` 设为保留天数（0 = 永久，修改后重启程序生效）。

数据库内的时间戳以 **UTC** 存储（界面显示为本地时间；v2.6.0 起生效，旧数据库在启动时自动一次性迁移）。

## 项目结构地图（想改代码看这里）

```
PCActivityLog/                    # WinUI 3 + .NET 8 工程
├── App.xaml(.cs)                 # 入口：单实例、服务组装、自我登记、卸载流程、优雅退出
├── MainWindow.xaml(.cs)          # 主窗口：时间线铺满 + Mica 背景 + 原生托盘
├── Themes/AppColors.xaml         # 浅/深双色板（ThemeDictionaries，随 RequestedTheme 自动切换）
├── Models/                       # 数据模型
│   ├── ActivityEvent.cs          #   一条事件（对应数据库一行）
│   └── EventType.cs              #   事件类型枚举 + 分类 + 中文名
├── Data/                         # 存储层
│   ├── Database.cs               #   SQLite 建表 / 查询 / 筛选搜索
│   └── WriteQueue.cs             #   单写队列（所有入库走这里，串行化 + 批量事务）
├── Watchers/                     # 采集层（每个文件一个监视模块，纯逻辑零 UI 依赖）
│   ├── IWatcherModule.cs         #   模块统一接口 + 事件出口接口
│   ├── WatcherManager.cs         #   模块注册 / 运行中启停 / 事件汇入
│   ├── DownloadWatcher.cs        #   下载/删除/重命名（含落盘判定）
│   ├── ZoneIdentifierReader.cs   #   读取下载来源网址（NTFS 备用数据流）
│   ├── MsiEventWatcher.cs        #   MSI 安装/卸载事件订阅
│   ├── RegistryUninstallWatcher.cs # 注册表对比（安装/更新/卸载；自动排除自身登记键）
│   ├── AppWatcher.cs             #   软件监视模块外壳（组合上面两个）
│   ├── SystemEventWatcher.cs     #   开关机（事件日志 + 停机回补）
│   └── ImFileWatcher.cs          #   微信/QQ 文件监视
├── Services/                     # 支撑服务
│   ├── AppSettings.cs            #   配置读写（settings.json）
│   ├── ThemeService.cs           #   主题跟随（读系统主题 / RequestedTheme / 标题栏深色 DWM）
│   ├── DiagnosticsLog.cs         #   滚动诊断日志（5 MB 上限）
│   ├── TrayIconService.cs        #   托盘图标（原生 Shell_NotifyIcon + Win32 托盘菜单，零第三方依赖）
│   ├── NotificationService.cs    #   托盘气泡（节流防刷屏）
│   ├── EventLinker.cs            #   下载↔安装关联匹配
│   ├── RetentionService.cs       #   过期数据清理
│   ├── ExportService.cs          #   CSV / JSON 导出
│   ├── ImFileStatusService.cs    #   IM 文件保存状态定期复核
│   ├── AutoStartService.cs       #   开机自启注册表
│   └── AppRegistrationService.cs #   系统登记（卸载注册表条目 + --uninstall 入口）
├── ViewModels/                   # 界面逻辑（MVVM，CommunityToolkit.Mvvm）
│   ├── TimelineViewModel.cs      #   时间线（筛选/分页/前沿+尾沿节流刷新/导出）
│   └── ActivityEventItem.cs      #   列表行（徽章配色/状态标识）
└── Views/                        # 界面
    ├── TimelinePage.xaml(.cs)    #   时间线页（主视图：筛选工具栏 + 原生 ListView + 右键菜单）
    ├── SettingsPage.xaml(.cs)    #   设置页（模块开关卡片，顶部返回）
    ├── UninstallWindow.xaml(.cs) #   卸载确认窗口（--uninstall）
    └── AddEventDialog.cs         #   手动添加记录（ContentDialog）
```

同级还有 `PCActivityLog.Tests/`（单元测试，xUnit）与根目录 `PCActivityLog.sln`（主工程 + 测试工程，VS 直接打开）。

### 常见修改

- **加一种新事件**：`EventType` 加枚举 → `EventTypeExtensions` 补三处映射 → 写一个 `IWatcherModule` → 在 `WatcherManager.BuildModules` 注册 → 设置页加开关
- **改默认保留期**：`AppSettings` 里的属性默认值
- **改落盘判定时间**：设置页"下载落盘判定等待"，代码在 `AppSettings.SettleSeconds`
- **WinUI 3 注意事项**：`SymbolIcon` 的 `Symbol` 枚举值写错会让 XAML 编译器**静默崩溃**（只报 MSB3073 无行号）；托盘菜单与托盘图标均为 Win32/Shell 原生实现（`MainWindow.ShowTrayMenu` / `Services/TrayIconService.cs`），改动托盘行为请直接改这两处

## 稳定性设计（为什么它不会越跑越卡）

这是常驻软件，资源管理是第一优先级，规则写死在代码里：

1. **Dispose 全覆盖**：所有 FileSystemWatcher、事件日志订阅、定时器在模块 Stop 时全部释放；程序退出时 WatcherManager 逐一销毁
2. **模块重启用新实例**：开关模块 = 销毁旧对象 + 建新对象，杜绝残留引用
3. **事件订阅配对解绑**：每处 `+=` 都有对应 `-=`（窗口关闭、模块停止时）
4. **内存上限**：下载判定跟踪表 10 分钟过期；界面列表分页；诊断日志 5 MB 滚动；数据库写入单队列串行化
5. **异常三层兜底**：UI 线程 / 后台线程 / Task 异常统一记日志，单个监视器出错只重建该模块，进程不崩
6. **FileSystemWatcher 缓冲区溢出自愈**：收到 Error 事件自动重建监视器

已验证：550 次模块启停循环句柄数零增长；强制 GC 后托管堆完全回落。

## 已知限制

- UWP 商店应用（Microsoft Store 装的）的安装卸载暂不记录
- 某些绿色软件不写注册表卸载键，无法被"软件监视"捕获（可用手动记录补）

## 开发验证记录

本项目开发过程中做过完整的冒烟测试（2026-09-02）：
下载（含来源 URL）、删除、重命名、安装（1.0）、更新（1.0→2.5 记录旧版本）、卸载、
双向事件关联、开关机回填（55 条）、
单实例激活、模块启停压力（550 次零泄漏）、8 分钟浸泡采样（内存/句柄/线程持平）、
优雅退出（无残留进程与错误日志）。

## 附带的开发工具（不属于正式软件，可整个删掉）

| 目录 | 用途 |
|---|---|
| `DbTool/` | 命令行查库小工具：`dotnet run --project DbTool -- "SELECT * FROM events LIMIT 10"`（也支持 DELETE） |
| `tests/` | 冒烟测试用的 PowerShell 脚本；**可重复验收入口为 `tests/acceptance.ps1`**（构建 → 产物检查 → 文档一致性守卫 → 单实例冒烟 → 诊断日志扫描）。注意：含中文的 .ps1 必须存成带 BOM 的 UTF-8，否则 PowerShell 5.1 会解析失败 |
| `tests/make_ico.py` | 图标生成管线：SVG 逐尺寸光栅化（resvg）→ 多尺寸 ico（16/24 用简化版，32+ 用主图标）。用法：`uv run tests/make_ico.py --dark <svg> --compact <svg> --out PCActivityLog/Assets/app.ico` |

---

*技术栈：.NET 8 + **WinUI 3**（Windows App SDK 1.8）· 5 个 NuGet 包全部为微软官方维护：Microsoft.WindowsAppSDK、Microsoft.Windows.SDK.BuildTools、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite、System.Diagnostics.EventLog（托盘/菜单/通知为 Win32/Shell 原生实现，无第三方托盘库）· 安装包 Inno Setup（installer/）· 单元测试 xUnit + GitHub Actions CI*
