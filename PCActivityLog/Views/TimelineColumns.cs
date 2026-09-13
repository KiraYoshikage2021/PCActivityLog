namespace PCActivityLog.Views;

/// <summary>
/// 时间线列宽定义 —— 表头拖拽调宽（GridSplitter）用的默认值/最小值/持久化映射。
/// 名称列（下标 2）恒为 ★ 填充剩余空间，不参与固定宽度与持久化。
/// </summary>
internal static class TimelineColumns
{
    public const int Count = 6;
    public const int NameColumn = 2;

    /// <summary>默认列宽（像素）。大小/版本列 240：容纳"旧版本 → 新版本"长版本号。</summary>
    public static readonly double[] Defaults = { 150, 84, 0, 240, 320, 140 };

    /// <summary>最小列宽（沿用 v1.x DataGrid 时代的 MinWidth，拖拽下限）。
    /// 名称★列下限 120：窗口不够宽时先压缩名称列，压到下限后整表转横向滚动（列表底部滚动条）。</summary>
    public static readonly double[] Minimums = { 110, 70, 120, 100, 120, 80 };

    /// <summary>参与持久化的固定列下标（★ 名称列除外）。</summary>
    private static readonly int[] Persisted = { 0, 1, 3, 4, 5 };

    /// <summary>钳制列宽到 [最小值, 4000]。</summary>
    public static double Clamp(int col, double width)
        => Math.Clamp(width, Minimums[col], 4000);

    /// <summary>把持久化的 5 个固定列宽展开成 6 列宽度（名称列恒 0 不使用）；saved 为空用默认值。</summary>
    public static double[] FromPersisted(IReadOnlyList<double>? saved)
    {
        var w = (double[])Defaults.Clone();
        if (saved == null) return w;
        for (int i = 0; i < Persisted.Length && i < saved.Count; i++)
            w[Persisted[i]] = Clamp(Persisted[i], saved[i]);
        return w;
    }

    /// <summary>把 6 列宽度收拢成 5 个固定列宽（四舍五入到整数）用于持久化。</summary>
    public static List<double> ToPersisted(double[] widths)
        => Persisted.Select(i => Math.Round(widths[i])).ToList();
}
