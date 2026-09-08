using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PCActivityLog.Data;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>统计页 —— 汇总卡片 + 按月分类柱状图。</summary>
public sealed partial class StatsPage : Page
{
    public StatsViewModel Vm { get; }

    /// <summary>是否已加载过数据（页面被 Frame 缓存，切回来时无需重新聚合）。</summary>
    private bool _initialized;

    /// <summary>图表重绘节流定时器（窗口拖拽缩放时 SizeChanged 会高频触发）。</summary>
    private DispatcherQueueTimer? _redrawTimer;

    public StatsPage()
    {
        Vm = new StatsViewModel(new StatsRepository(App.Db!));
        InitializeComponent();

        // 重绘节流：120ms 内的多次 SizeChanged 合并为一次重绘
        _redrawTimer = DispatcherQueue.CreateTimer();
        _redrawTimer.Interval = TimeSpan.FromMilliseconds(120);
        _redrawTimer.IsRepeating = false;
        _redrawTimer.Tick += (_, _) => { _redrawTimer.Stop(); Chart.Redraw(); };

        Loaded += (_, _) =>
        {
            if (!_initialized)
            {
                _initialized = true;
                Vm.Refresh();
            }
            // 布局完成后重绘图表（此时 ActualWidth/Height 才有效）
            Chart.Redraw();
        };
        Chart.SizeChanged += (_, _) => { _redrawTimer?.Stop(); _redrawTimer?.Start(); };
    }
}
