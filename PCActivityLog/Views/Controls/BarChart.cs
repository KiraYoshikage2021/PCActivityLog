using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views.Controls;

/// <summary>
/// 自绘堆叠柱状图 —— 零第三方依赖的轻量图表控件。
/// 每列一个月，列内按下载/应用/浏览/其他四色堆叠；悬停月份标签显示数值明细。
/// 用 OnRender 直接绘制（数据量最多 12 列 × 4 段，性能无压力）。
/// </summary>
public class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(System.Collections.ObjectModel.ObservableCollection<ChartColumn>),
        typeof(BarChart), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, OnColumnsChanged));

    public System.Collections.ObjectModel.ObservableCollection<ChartColumn>? Columns
    {
        get => (System.Collections.ObjectModel.ObservableCollection<ChartColumn>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BarChart chart)
        {
            // 集合变化时触发重绘（订阅配对解绑，防泄漏）
            if (e.OldValue is System.Collections.ObjectModel.ObservableCollection<ChartColumn> old)
                old.CollectionChanged -= chart.OnCollectionChanged;
            if (e.NewValue is System.Collections.ObjectModel.ObservableCollection<ChartColumn> neu)
                neu.CollectionChanged += chart.OnCollectionChanged;
            chart.InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var columns = Columns;
        if (columns is not { Count: > 0 }) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // 文字与辅助色从当前主题取（浅/深各一套）
        var labelBrush = Services.ThemeService.FindBrush("ChartTextBrush");
        if (labelBrush.CanFreeze) labelBrush.Freeze();

        double W = ActualWidth, H = ActualHeight;
        const double labelH = 20;   // 底部月份标签高度
        const double topPad = 12;   // 顶部留白（最高柱的数值）
        double chartH = H - labelH - topPad;
        if (chartH < 20) return;

        double max = columns.Max(c => c.Total);
        if (max <= 0) max = 1;

        double colSpace = W / columns.Count;
        double barW = Math.Min(46, colSpace * 0.55);
        double x = (colSpace - barW) / 2;

        foreach (var col in columns)
        {
            double usedH = 0;
            // 从下往上堆叠
            double yBottom = topPad + chartH;
            foreach (var seg in col.Segments)
            {
                if (seg.Value <= 0) continue;
                double h = chartH * (seg.Value / max);
                var rect = new Rect(x, yBottom - usedH - h, barW, h);
                var brush = seg.Color;
                if (brush.CanFreeze) brush.Freeze();
                dc.DrawRectangle(brush, null, rect);
                usedH += h;
            }

            // 顶部总数标签
            if (col.Total > 0)
            {
                var totalText = MakeText(col.Total.ToString("N0"), typeface, 11, labelBrush, dpi.PixelsPerDip);
                dc.DrawText(totalText, new Point(x + (barW - totalText.Width) / 2, topPad + chartH - usedH - totalText.Height - 2));
            }

            // 底部月份标签
            var label = MakeText(col.Label, typeface, 11, labelBrush, dpi.PixelsPerDip);
            dc.DrawText(label, new Point(x + (barW - label.Width) / 2, topPad + chartH + (labelH - label.Height) / 2));

            x += colSpace;
        }
    }

    private static FormattedText MakeText(string s, Typeface typeface, double size, Brush brush, double pixelsPerDip)
        => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip);
}
