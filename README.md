# 电脑日志记录（PCActivityLog）

一个 Windows 常驻托盘小工具，自动记录这台电脑上发生的事：下载了哪些文件、装了/更新了/卸载了哪些软件、浏览器访问过哪些网页、什么时候开的机关的机。所有数据只存在本机（`%LOCALAPPDATA%\PCActivityLog\`），不联网、不上传。

## 功能一览

| 记录内容 | 说明 |
|---|---|
| 📥 下载 | 监视"下载"等文件夹（可配置多个），文件写入完成才算一次下载；自动读取 Windows 的 Mark of the Web 标记，记录**来源网址** |
| 🗑 删除 / ✏️ 重命名 | 监视文件夹内的文件删除和重命名事件 |
| 💬 微信/QQ 文件 | 自动识别微信（xwechat_files / WeChat Files）与 QQ（Tencent Files）的文件存储目录，记录收到的每个文件，并标注**✔ 已保存 / ⚠ 已清理**（文件被 IM 存储管理清理后状态自动更新）；支持自定义目录兜底 |
| 📦 安装 / ⬆️ 更新 / 🗑 卸载 | 双通道检测：MSI 安装事件日志 + 注册表卸载键对比（覆盖 EXE 安装器和绿色软件），带防误报复核 |
| 💻 开机 / 关机 | 从系统事件日志读取，程序没运行期间的开关机也会在下次启动时补录 |
| 🌐 浏览记录 | 定时增量读取 Chrome / Edge / Firefox 历史库（复制后读取，不干扰浏览器；无痕模式不记录）；**默认不在时间线展示**，需要时切"浏览"筛选或在搜索框搜索（会自动扩到含浏览） |
| ✍️ 手动记录 | 界面上随时补录自定义事件 |
| 🔗 下载↔安装关联 | 安装事件自动回溯匹配最近的下载，显示"由哪个安装包装的" |

每个功能模块都可以在**设置**里随时开关（立即生效，不用重启程序）。

## 界面

- **时间线页**：按类型筛选、关键词搜索（含备注）、日期范围；类型列为彩色胶囊徽章；双击打开文件位置或跳转关联事件；右键编辑备注；导出 CSV / JSON
- **统计页**：本月下载量/总量、安装数、浏览数等汇总卡片 + 最近 12 个月分类柱状图
- **托盘图标**：双击打开主窗口；右键菜单可快速设置、开机自启、退出
- **自动跟随 Windows 浅色/深色主题**：和 Windows 11 原生应用一样，系统切换深色模式时本软件实时跟随（包括系统标题栏一起变深）；也可在设置里强制浅色/深色

## 如何使用

### 方式一：直接用发布版（推荐）

构建免安装的发布目录（WinUI 3 自包含，免装运行时）：

```bash
cd PCActivityLog
dotnet publish -c Release -r win-x64 -p:WindowsAppSDKSelfContained=true -o bin/Release/publish
```

产物在 `bin\Release\publish\`（约 210 MB，含 Windows App SDK 运行时），
整个文件夹拷到别的电脑、双击 `PCActivityLog.exe` 即可用，不需要安装任何运行时。

> ⚠️ WinUI 3 必须用**目录形式**发布（不支持单文件打包）。
> 本项目 csproj 里的 `CopyXamlArtifactsOnPublish` 目标用于绕过
> `dotnet publish` 不复制 `.xbf`/`.pri` 的已知缺陷——**删掉它发布版会启动即崩**。

### 方式二：从源码运行

用 Visual Studio 2022 打开 `PCActivityLog\PCActivityLog.csproj` 按 F5，
或在命令行 `dotnet run --project PCActivityLog`。

### 开机自启动

托盘右键菜单勾选"开机自启动"即可（写当前用户的注册表 Run 键，不需要管理员权限）。

## 数据都存在哪

| 文件 | 作用 |
|---|---|
| `%LOCALAPPDATA%\PCActivityLog\activity.db` | 事件数据库（SQLite，WAL 模式） |
| `%LOCALAPPDATA%\PCActivityLog\settings.json` | 全部设置（模块开关等） |
| `%LOCALAPPDATA%\PCActivityLog\diag.log` | 诊断日志（滚动上限 5 MB，排查问题用） |

想"彻底重置"就退出程序后删掉这个文件夹。

数据保留策略：浏览记录默认保留 90 天，其余事件默认永久，都可以在设置里改。

## 项目结构地图（想改代码看这里）

```
PCActivityLog/                    # WinUI 3 + .NET 8 工程
├── App.xaml(.cs)                 # 入口：单实例、服务组装、全局异常兜底、优雅退出
├── MainWindow.xaml(.cs)          # 主窗口：左侧 NavigationView 导航 + Mica 背景 + 托盘
├── Themes/AppColors.xaml         # 浅/深双色板（ThemeDictionaries，随 RequestedTheme 自动切换）
├── Models/                       # 数据模型
│   ├── ActivityEvent.cs          #   一条事件（对应数据库一行）
│   └── EventType.cs              #   事件类型枚举 + 分类 + 中文名
├── Data/                         # 存储层
│   ├── Database.cs               #   SQLite 建表 / 查询 / 筛选搜索
│   ├── WriteQueue.cs             #   单写队列（所有入库走这里，串行化 + 批量事务）
│   └── StatsRepository.cs        #   统计聚合（卡片 + 按月图表）
├── Watchers/                     # 采集层（每个文件一个监视模块，纯逻辑零 UI 依赖）
│   ├── IWatcherModule.cs         #   模块统一接口 + 事件出口接口
│   ├── WatcherManager.cs         #   模块注册 / 运行中启停 / 事件汇入
│   ├── DownloadWatcher.cs        #   下载/删除/重命名（含落盘判定）
│   ├── ZoneIdentifierReader.cs   #   读取下载来源网址（NTFS 备用数据流）
│   ├── MsiEventWatcher.cs        #   MSI 安装/卸载事件订阅
│   ├── RegistryUninstallWatcher.cs # 注册表对比（安装/更新/卸载）
│   ├── AppWatcher.cs             #   软件监视模块外壳（组合上面两个）
│   ├── SystemEventWatcher.cs     #   开关机（事件日志 + 停机回补）
│   ├── BrowserHistoryWatcher.cs  #   浏览器历史增量导入
│   └── ImFileWatcher.cs          #   微信/QQ 文件监视
├── Services/                     # 支撑服务
│   ├── AppSettings.cs            #   配置读写（settings.json）
│   ├── ThemeService.cs           #   主题跟随（读系统主题 / RequestedTheme / 标题栏深色 DWM）
│   ├── DiagnosticsLog.cs         #   滚动诊断日志（5 MB 上限）
│   ├── NotificationService.cs    #   托盘气泡（节流防刷屏）
│   ├── EventLinker.cs            #   下载↔安装关联匹配
│   ├── RetentionService.cs       #   过期数据清理
│   ├── ExportService.cs          #   CSV / JSON 导出
│   ├── ImFileStatusService.cs    #   IM 文件保存状态定期复核
│   └── AutoStartService.cs       #   开机自启注册表
├── ViewModels/                   # 界面逻辑（MVVM）
│   ├── TimelineViewModel.cs      #   时间线（筛选/分页/前沿+尾沿节流刷新/导出）
│   ├── StatsViewModel.cs         #   统计（汇总卡片 + 图表数据）
│   └── ActivityEventItem.cs      #   列表行（徽章配色/状态标识）
└── Views/                        # 界面
    ├── TimelinePage.xaml(.cs)    #   时间线页（DataGrid + 徽章 + 右键菜单 + 列宽记忆）
    ├── StatsPage.xaml(.cs)       #   统计页（汇总卡片 + 柱状图）
    ├── SettingsPage.xaml(.cs)    #   设置页（模块开关卡片）
    ├── AddEventDialog.cs         #   手动添加记录（ContentDialog）
    └── Controls/BarChart.cs      #   柱状图控件（Canvas 绘制）
```

### 常见修改

- **加一种新事件**：`EventType` 加枚举 → `EventTypeExtensions` 补三处映射 → 写一个 `IWatcherModule` → 在 `WatcherManager.BuildModules` 注册 → 设置页加开关
- **改默认保留期**：`AppSettings` 里的属性默认值
- **改落盘判定时间**：设置页"下载落盘判定等待"，代码在 `AppSettings.SettleSeconds`
- **WinUI 3 注意事项**：`SymbolIcon` 的 `Symbol` 枚举值写错会让 XAML 编译器**静默崩溃**（只报 MSB3073 无行号）；`H.NotifyIcon` 不能写在 XAML 里（会破坏同文件其他命名元素），必须纯代码创建

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

- 无痕/隐私窗口的浏览不会被记录（浏览器本身不写历史库，属正常现象）
- UWP 商店应用（Microsoft Store 装的）的安装卸载暂不记录
- 浏览器历史**从本软件首次运行开始**记录，不回补之前的存量
- 某些绿色软件不写注册表卸载键，无法被"软件监视"捕获（可用手动记录补）

## 开发验证记录

本项目开发过程中做过完整的冒烟测试（2026-09-02）：
下载（含来源 URL）、删除、重命名、安装（1.0）、更新（1.0→2.5 记录旧版本）、卸载、
双向事件关联、开关机回填（55 条）、浏览器增量导入（真实 Edge 数据）、
单实例激活、模块启停压力（550 次零泄漏）、8 分钟浸泡采样（内存/句柄/线程持平）、
优雅退出（无残留进程与错误日志）。

## 附带的开发工具（不属于正式软件，可整个删掉）

| 目录 | 用途 |
|---|---|
| `DbTool/` | 命令行查库小工具：`dotnet run --project DbTool -- "SELECT * FROM events LIMIT 10"`（也支持 DELETE） |
| `WatcherTest/` | 监视模块隔离测试/压力测试的控制台宿主（模块启停泄漏检测） |
| `tests/` | 冒烟测试用的 PowerShell 脚本（注意：含中文的 .ps1 必须存成带 BOM 的 UTF-8，否则 PowerShell 5.1 会解析失败） |

---

*技术栈：.NET 8 + **WinUI 3**（Windows App SDK 1.8）· 6 个 NuGet 包：Microsoft.WindowsAppSDK、Microsoft.Windows.SDK.BuildTools、Microsoft.Data.Sqlite、CommunityToolkit.Mvvm、CommunityToolkit.WinUI.UI.Controls.DataGrid、H.NotifyIcon.WinUI*
