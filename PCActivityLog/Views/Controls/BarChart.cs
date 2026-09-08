using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;
using Windows.UI;

namespace PCActivityLog.Views.Controls;

/// <summary>
/// 柱状图控件（WinUI 版）—— 用 Canvas + Rectangle 绘制堆叠柱状图。
/// 与 WPF 版的差异：不再用 OnRender(DrawingContext) 自绘，改为构建视觉树
/// （WinUI 的 OnRender 等价物受限，用 Canvas 布局更可靠且支持命中测试）。
/// 每列一个月，按下载/应用/浏览/其他四色堆叠。
/// </summary>
public sealed class BarChart : ContentControl
{
    private readonly Canvas _canvas = new();
    private readonly Grid _host = new();

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(object), typeof(BarChart),
        new PropertyMetadata(null, OnColumnsChanged));

    public object? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public BarChart()
    {
        _host.Children.Add(_canvas);
        Content = _host;
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BarChart chart)
        {
            // 订阅集合变化以重绘（配对解绑防泄漏）
            if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCol)
                oldCol.CollectionChanged -= chart.OnCollectionChanged;
            if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCol)
                newCol.CollectionChanged += chart.OnCollectionChanged;
            chart.Redraw();
        }
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Redraw();

    /// <summary>重建柱状图视觉树。</summary>
    public void Redraw()
    {
        _canvas.Children.Clear();
        if (Columns is not System.Collections.IEnumerable list) return;

        var columns = list.Cast<ChartColumn>().ToList();
        if (columns.Count == 0) return;

        double W = ActualWidth, H = ActualHeight;
        if (W < 20 || H < 20) return;

        const double labelH = 20, topPad = 16;
        double chartH = H - labelH - topPad;
        if (chartH < 20) return;

        double max = columns.Max(c => c.Total);
        if (max <= 0) max = 1;

        double colSpace = W / columns.Count;
        double barW = Math.Min(44, colSpace * 0.52);
        var labelBrush = ThemeService.FindBrush("ChartTextBrush");

        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            double x = i * colSpace + (colSpace - barW) / 2;

            // 先算出每段的高度（含最小高度保护 + 段间距），再自下而上绘制
            var visible = col.Segments.Where(s => s.Value > 0).ToList();
            const double segGap = 2; // 段间距
            var heights = visible.Select(s => Math.Max(3, chartH * (s.Value / max))).ToArray();

            double usedH = 0;
            for (int k = 0; k < visible.Count; k++)
            {
                var seg = visible[k];
                double h = heights[k];

                // 每段都做圆角（圆角块堆叠，现代观感）；
                // 半径取 4px 且不超过段高一半/柱宽一半，避免小段被切变形
                double r = Math.Min(4, Math.Min(barW / 2, h / 2));

                var rect = new Rectangle
                {
                    Width = barW,
                    Height = h,
                    // 渐变：同色系上浅下深
                    Fill = MakeGradient(seg.Color),
                    RadiusX = r,
                    RadiusY = r,
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, topPad + chartH - usedH - h);
                ToolTipService.SetToolTip(rect, $"{col.Label}  {seg.Legend} {seg.Value:N0}");
                _canvas.Children.Add(rect);

                usedH += h + (k < visible.Count - 1 ? segGap : 0);
            }

            // 顶部总数（放在柱顶上方）
            if (col.Total > 0)
            {
                var total = new TextBlock
                {
                    Text = col.Total.ToString("N0"),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = labelBrush,
                };
                total.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(total, x + (barW - total.DesiredSize.Width) / 2);
                Canvas.SetTop(total, topPad + chartH - (usedH - (visible.Count > 0 ? segGap : 0)) - total.DesiredSize.Height - 2);
                _canvas.Children.Add(total);
            }

            // 底部月份标签
            var label = new TextBlock
            {
                Text = col.Label,
                FontSize = 11,
                Foreground = labelBrush,
            };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x + (barW - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, topPad + chartH + (labelH - label.DesiredSize.Height) / 2);
            _canvas.Children.Add(label);
        }
    }

    /// <summary>把纯色转成垂直渐变（上浅下深），让柱子有立体感。</summary>
    private static Brush MakeGradient(Brush baseBrush)
    {
        if (baseBrush is not SolidColorBrush solid) return baseBrush;
        var c = solid.Color;
        var top = Windows.UI.Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + (255 - c.R) * 0.22),
            (byte)Math.Min(255, c.G + (255 - c.G) * 0.22),
            (byte)Math.Min(255, c.B + (255 - c.B) * 0.22));
        var grad = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = top, Offset = 0 },
                new GradientStop { Color = c, Offset = 1 },
            },
        };
        return grad;
    }
}
