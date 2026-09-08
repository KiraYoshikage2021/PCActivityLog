using Microsoft.UI.Xaml.Controls;
using PCActivityLog.Data;
using PCActivityLog.Services;
using PCActivityLog.ViewModels;

namespace PCActivityLog.Views;

/// <summary>统计页 —— 汇总卡片 + 按月分类柱状图。</summary>
public sealed partial class StatsPage : Page
{
    public StatsViewModel Vm { get; }

    public StatsPage()
    {
        Vm = new StatsViewModel(new StatsRepository(App.Db!));
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Vm.Refresh();
            // 布局完成后重绘图表（此时 ActualWidth/Height 才有效）
            Chart.Redraw();
        };
        Chart.SizeChanged += (_, _) => Chart.Redraw();
    }
}
