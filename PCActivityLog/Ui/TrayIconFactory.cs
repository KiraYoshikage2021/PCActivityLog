using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCActivityLog.Ui;

/// <summary>
/// 图标工厂 —— 用代码绘制应用图标（托盘 / 窗口 / exe 文件图标），无需图片资源文件。
///
/// 设计：深蓝对角渐变圆角底 + 三条"事件日志"条 —— 每条由一枚彩色圆点
/// （浅青/浅绿/浅琥珀，呼应时间线里浏览·下载/安装/其他事件的徽章色）
/// 和一段白色圆角横条组成，长短错落像一页活动清单。
///
/// 绘制坐标全部按画布尺寸等比缩放，任意尺寸（16~256）都清晰；
/// 生成的位图全部 Freeze()，跨线程安全且渲染开销与静态图相同。
/// </summary>
public static class TrayIconFactory
{
    private static ImageSource? _trayCached;
    private static ImageSource? _windowCached;

    /// <summary>托盘图标（32px，与常见任务栏 DPI 匹配）。</summary>
    public static ImageSource Create()
    {
        if (_trayCached != null) return _trayCached;
        _trayCached = Render(32);
        return _trayCached;
    }

    /// <summary>窗口/对话框标题栏图标（编码为 PNG 帧再解码，Window.Icon 兼容性最好）。</summary>
    public static ImageSource CreateWindowIcon()
    {
        if (_windowCached != null) return _windowCached;
        var source = Render(256);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        _windowCached = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        _windowCached.Freeze();
        return _windowCached;
    }

    /// <summary>生成多尺寸 .ico 文件（16/24/32/48/64/128/256，PNG 内嵌格式，Vista+ 通用）。</summary>
    public static void SaveIco(string path)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            var bmp = Render(s);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            pngs.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        // ICONDIR 头
        w.Write((ushort)0);          // 保留
        w.Write((ushort)1);          // 类型：图标
        w.Write((ushort)pngs.Count); // 图像数量

        // 目录项（16 字节/项）
        int offset = 6 + 16 * pngs.Count;
        for (var i = 0; i < pngs.Count; i++)
        {
            var size = sizes[i];
            w.Write((byte)(size == 256 ? 0 : size)); // 宽（256 用 0 表示）
            w.Write((byte)(size == 256 ? 0 : size)); // 高
            w.Write((byte)0);                        // 调色板数
            w.Write((byte)0);                        // 保留
            w.Write((ushort)1);                      // 色彩平面
            w.Write((ushort)32);                     // 位深
            w.Write((uint)pngs[i].Length);           // 数据长度
            w.Write((uint)offset);                   // 数据偏移
            offset += pngs[i].Length;
        }

        // PNG 数据
        foreach (var png in pngs) w.Write(png);
    }

    /// <summary>按指定尺寸渲染图标。</summary>
    private static ImageSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, size);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>核心绘制。s = 画布边长，所有坐标按 s 等比。</summary>
    private static void Draw(DrawingContext dc, double s)
    {
        double U = s / 256.0; // 单位换算

        // 背景：深蓝对角渐变圆角方块（圆角率 ≈ 22%，现代"超椭圆"感）
        var bg = new LinearGradientBrush(
            Color.FromRgb(0x4F, 0x8E, 0xF9),   // 左上亮蓝
            Color.FromRgb(0x1E, 0x4F, 0xD8),   // 右下深蓝
            new Point(0, 0), new Point(1, 1));
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, null,
            new Rect(4 * U, 4 * U, 248 * U, 248 * U), 56 * U, 56 * U);

        // 三条事件日志：彩色圆点 + 白色圆角横条（长短错落）
        double dotR = 15 * U;
        double barH = 20 * U;
        double dotCx = 60 * U;
        double barX = 92 * U;

        var rows = new (double Cy, double BarW, Color Dot)[]
        {
            (78 * U,  150 * U, Color.FromRgb(0xA5, 0xE3, 0xFF)), // 冰青（提亮增强对比）
            (128 * U, 104 * U, Color.FromRgb(0x6E, 0xE7, 0xA0)), // 浅绿
            (178 * U, 140 * U, Color.FromRgb(0xFC, 0xD3, 0x4D)), // 浅琥珀
        };

        var barBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)); // 白 90%
        barBrush.Freeze();

        foreach (var (cy, barW, dotColor) in rows)
        {
            var dot = new SolidColorBrush(dotColor);
            dot.Freeze();
            dc.DrawEllipse(dot, null, new Point(dotCx, cy), dotR, dotR);
            dc.DrawRoundedRectangle(barBrush, null,
                new Rect(barX, cy - barH / 2, barW, barH), barH / 2, barH / 2);
        }
    }
}
