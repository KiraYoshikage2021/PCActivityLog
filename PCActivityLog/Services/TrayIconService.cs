using System.Runtime.InteropServices;

namespace PCActivityLog.Services;

/// <summary>
/// 托盘图标服务 —— Windows 原生 Shell_NotifyIcon 实现（纯 P/Invoke，零第三方依赖）。
/// 职责：托盘图标的添加/更新/删除、鼠标左/右键回调、气泡通知（带程序图标）。
///
/// 实现要点：
///  - 在 UI 线程创建一个 message-only 隐藏窗口接收托盘回调消息（该线程有消息泵）；
///  - 图标用 LoadImage 从 app.ico 按托盘尺寸取帧（系统自动选最匹配的一帧，小图清晰），
///    加载失败时回退 exe 内嵌图标（ExtractIconEx）；
///  - 气泡用 NOTIFYICONDATA 的 NIF_INFO，NIIF_USER + hBalloonIcon 让气泡也显示程序图标；
///  - explorer.exe 重启后系统广播 "TaskbarCreated"，监听到后重新补挂图标；
///  - 必须在有线程消息泵的线程上创建与销毁（本程序固定在 UI 线程使用）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly string _tip;
    private readonly Action _onLeftClick;
    private readonly Action _onRightClick;
    private readonly Action? _onBalloonClicked;

    private const string ClassName = "PCActivityLog_TrayWnd";
    private const uint WM_APP_TRAY = 0x8000 + 1; // WM_APP+1：托盘回调消息

    // 回调消息 lParam 里的鼠标/气泡通知码（经典版本语义）
    private const long WM_LBUTTONUP = 0x0202;
    private const long WM_LBUTTONDBLCLK = 0x0203;
    private const long WM_RBUTTONUP = 0x0205;
    private const long NIN_BALLOONUSERCLICK = 0x0405; // 用户点击了气泡

    private const uint NIM_ADD = 0x00, NIM_MODIFY = 0x01, NIM_DELETE = 0x02;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
    private const uint NIIF_USER = 0x04, NIIF_RESPECT_QUIET_TIME = 0x80;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int HWND_MESSAGE = -3;

    private IntPtr _hwnd;
    private WndProcDelegate? _wndProc;      // 字段持有委托，防止被 GC 回收后窗口回调野指针
    private NOTIFYICONDATAW _nid;
    private bool _added;
    private IntPtr _hIcon;
    private string? _iconPath;
    private IntPtr _dpiWindow;
    private uint _taskbarCreatedMsg;

    /// <summary>托盘图标是否已添加。</summary>
    public bool IsVisible => _added;

    public TrayIconService(string tip, Action onLeftClick, Action onRightClick, Action? onBalloonClicked = null)
    {
        _tip = tip;
        _onLeftClick = onLeftClick;
        _onRightClick = onRightClick;
        _onBalloonClicked = onBalloonClicked;
    }

    /// <summary>
    /// 创建消息窗口并添加托盘图标。失败返回 false（不抛异常，只记日志）。
    /// </summary>
    /// <param name="iconPath">app.ico 文件路径（按托盘尺寸自动取帧）。</param>
    /// <param name="dpiWindow">用于读取 DPI 的窗口句柄，托盘图标尺寸跟随该窗口 DPI。</param>
    public bool TryAdd(string? iconPath, IntPtr dpiWindow)
    {
        try
        {
            _iconPath = iconPath;
            _dpiWindow = dpiWindow;
            _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
            if (_hwnd == IntPtr.Zero && !CreateMessageWindow()) return false;
            return AddOrModify();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("托盘图标初始化失败", ex);
            return false;
        }
    }

    /// <summary>显示气泡通知（标题 ≤63 字、内容 ≤255 字，超长截断）。未添加图标时静默忽略。</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        try
        {
            _nid.uFlags |= NIF_INFO;
            _nid.szInfoTitle = Truncate(title, 63);
            _nid.szInfo = Truncate(text, 255);
            _nid.dwInfoFlags = NIIF_USER | NIIF_RESPECT_QUIET_TIME;
            _nid.hBalloonIcon = _hIcon;
            Shell_NotifyIconW(NIM_MODIFY, ref _nid);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("显示气泡通知失败: " + ex.Message);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_added) Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _added = false;
        }
        catch { }
        try
        {
            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        }
        catch { }
        try
        {
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }
        catch { }
    }

    // ================= 内部实现 =================

    /// <summary>添加（首次）或修改（DPI 刷新后）托盘图标；失败时自动降级重试 NIM_ADD。</summary>
    private bool AddOrModify()
    {
        var oldIcon = _hIcon;
        _hIcon = LoadTrayIcon();

        _nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _hIcon,
            szTip = _tip,
        };

        var op = _added ? NIM_MODIFY : NIM_ADD;
        _added = Shell_NotifyIconW(op, ref _nid);
        if (!_added && op == NIM_MODIFY)
            _added = Shell_NotifyIconW(NIM_ADD, ref _nid); // 图标已被系统清掉时改为重新添加

        if (_added && oldIcon != IntPtr.Zero && oldIcon != _hIcon)
        {
            try { DestroyIcon(oldIcon); } catch { }
        }
        return _added;
    }

    /// <summary>加载托盘图标：app.ico 按托盘尺寸取帧 → 失败回退 exe 内嵌图标 → 都失败返回零（无图标）。</summary>
    private IntPtr LoadTrayIcon()
    {
        var iconPath = _iconPath;
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            var (cx, cy) = GetTraySize();
            var h = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
            if (h != IntPtr.Zero) return h;
        }

        var exe = Environment.ProcessPath;
        if (exe != null && ExtractIconExW(exe, 0, out _, out var hSmall, 1) > 0 && hSmall != IntPtr.Zero)
        {
            DiagnosticsLog.Warn("app.ico 加载失败，托盘回退 exe 内嵌图标");
            return hSmall;
        }
        return IntPtr.Zero;
    }

    /// <summary>托盘图标尺寸：跟随窗口 DPI（100%=16px，150%=24px），与窗口图标保持一致。</summary>
    private (int cx, int cy) GetTraySize()
    {
        try
        {
            uint dpi = _dpiWindow != IntPtr.Zero ? GetDpiForWindow(_dpiWindow) : GetDpiForSystem();
            if (dpi == 0) dpi = 96;
            var px = (int)Math.Round(16.0 * dpi / 96.0);
            return (px, px);
        }
        catch { return (16, 16); }
    }

    /// <summary>explorer.exe 重启后图标被系统清除：删除后按当前 DPI 重新加载并补挂。</summary>
    private void ReAddAfterExplorerRestart()
    {
        try
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _added = false;
            AddOrModify();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn("补挂托盘图标失败: " + ex.Message);
        }
    }

    private bool CreateMessageWindow()
    {
        _wndProc = WndProc;
        var hInstance = GetModuleHandleW(null);

        // 类可能已注册过（本进程内服务重建）：注册失败不阻断，直接尝试建窗口
        var cls = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassExW(ref cls);

        _hwnd = CreateWindowExW(0, ClassName, "PCActivityLogTray", 0,
            0, 0, 0, 0, (IntPtr)HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
        return _hwnd != IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            // 回调一律 try/catch：WndProc 里抛异常会直接拖垮整个进程
            try
            {
                switch (lParam.ToInt64())
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        _onLeftClick();
                        break;
                    case WM_RBUTTONUP:
                        _onRightClick();
                        break;
                    case NIN_BALLOONUSERCLICK:
                        _onBalloonClicked?.Invoke();
                        break;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            ReAddAfterExplorerRestart();
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion; // 与 uTimeout 的联合体
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpcName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);
}
