using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCActivityLog.Data;

namespace PCActivityLog.ViewModels;

/// <summary>柱状图的一段（一个颜色块）。</summary>
public record ChartSegment(double Value, Brush Color, string Legend);

/// <summary>柱状图的一列（一个月）。</summary>
public record ChartColumn(string Label, ChartSegment[] Segments)
{
    public double Total => Segments.Sum(s => s.Value);
}

/// <summary>统计页 ViewModel —— 汇总卡片 + 按月堆叠柱状图。</summary>
public partial class StatsViewModel : ObservableObject
{
    private readonly StatsRepository _repo;

    [ObservableProperty] private int monthDownloads;
    [ObservableProperty] private string monthDownloadBytesText = "0";
    [ObservableProperty] private int monthInstalls;
    [ObservableProperty] private int monthBrowses;
    [ObservableProperty] private long totalEvents;

    public ObservableCollection<ChartColumn> Columns { get; } = new();
    public ObservableCollection<ChartSegment> Legend { get; } = new();

    public StatsViewModel(StatsRepository repo) => _repo = repo;

    /// <summary>刷新统计数据（切到统计页/主题切换时调用）。</summary>
    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var cards = _repo.GetSummary(monthStart);
            MonthDownloads = cards.MonthDownloads;
            MonthDownloadBytesText = FormatBytes(cards.MonthDownloadBytes);
            MonthInstalls = cards.MonthInstalls;
            MonthBrowses = cards.MonthBrowses;
            TotalEvents = cards.TotalEvents;

            // 四分类颜色从当前主题字典取（浅/深各一套，保证对比度）
            var downloadBrush = Services.ThemeService.FindBrush("DownloadBrush");
            var appBrush = Services.ThemeService.FindBrush("AppBrush");
            var browseBrush = Services.ThemeService.FindBrush("BrowseBrush");
            var otherBrush = Services.ThemeService.FindBrush("OtherBrush");

            // 最近 12 个月堆叠柱状图：下载/应用/浏览/其他
            var months = _repo.GetMonthly(12);
            Columns.Clear();
            foreach (var m in months)
            {
                Columns.Add(new ChartColumn(m.Month, new[]
                {
                    new ChartSegment(m.Downloads, downloadBrush, "下载"),
                    new ChartSegment(m.Apps,    appBrush, "应用"),
                    new ChartSegment(m.Browses, browseBrush, "浏览"),
                    new ChartSegment(m.Others,  otherBrush, "其他"),
                }));
            }
            if (Columns.Count == 0)
                Columns.Add(new ChartColumn(DateTime.Now.ToString("yyyy-MM"),
                    new[] { new ChartSegment(0, Brushes.Transparent, "下载") }));

            Legend.Clear();
            Legend.Add(new ChartSegment(0, downloadBrush, "下载"));
            Legend.Add(new ChartSegment(0, appBrush, "应用"));
            Legend.Add(new ChartSegment(0, browseBrush, "浏览"));
            Legend.Add(new ChartSegment(0, otherBrush, "其他"));
        }
        catch (Exception ex)
        {
            Services.DiagnosticsLog.Error("统计刷新失败", ex);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):F2} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):F1} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):F0} KB",
        _ => $"{bytes} B",
    };
}
