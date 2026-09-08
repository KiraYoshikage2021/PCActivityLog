using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using PCActivityLog.Data;
using PCActivityLog.Services;

namespace PCActivityLog.ViewModels;

/// <summary>柱状图的一段（一个颜色块）。</summary>
public record ChartSegment(double Value, Brush Color, string Legend);

/// <summary>柱状图的一列（一个月）。</summary>
public record ChartColumn(string Label, ChartSegment[] Segments)
{
    public double Total => Segments.Sum(s => s.Value);
}

/// <summary>统计页 ViewModel（WinUI 版）—— 汇总卡片 + 按月分类图表数据。</summary>
public partial class StatsViewModel : ObservableObject
{
    private readonly StatsRepository _repo;

    [ObservableProperty] private int monthDownloads;
    [ObservableProperty] private string monthDownloadBytesText = "0";
    [ObservableProperty] private int monthInstalls;
    [ObservableProperty] private long totalEvents;

    public ObservableCollection<ChartColumn> Columns { get; } = new();
    public ObservableCollection<ChartSegment> Legend { get; } = new();

    public StatsViewModel(StatsRepository repo) => _repo = repo;

    /// <summary>刷新统计数据（进入统计页/主题切换时调用）。</summary>
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
            TotalEvents = cards.TotalEvents;

            var downloadBrush = ThemeService.FindBrush("ChartDownloadBrush"); // 低饱和+半透明专用色
            var appBrush = ThemeService.FindBrush("ChartAppBrush");
            var otherBrush = ThemeService.FindBrush("ChartOtherBrush");

            var months = _repo.GetMonthly(12);
            Columns.Clear();
            foreach (var m in months)
            {
                Columns.Add(new ChartColumn(m.Month, new[]
                {
                    new ChartSegment(m.Downloads, downloadBrush, "下载"),
                    new ChartSegment(m.Apps,    appBrush, "应用"),
                    new ChartSegment(m.Others,  otherBrush, "其他"),
                }));
            }
            if (Columns.Count == 0)
                Columns.Add(new ChartColumn(DateTime.Now.ToString("yyyy-MM"),
                    new[] { new ChartSegment(0, new SolidColorBrush(Microsoft.UI.Colors.Transparent), "下载") }));

            Legend.Clear();
            Legend.Add(new ChartSegment(0, downloadBrush, "下载"));
            Legend.Add(new ChartSegment(0, appBrush, "应用"));
            Legend.Add(new ChartSegment(0, otherBrush, "其他"));
        }
        catch (Exception ex) { DiagnosticsLog.Error("统计刷新失败", ex); }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):F2} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):F1} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):F0} KB",
        _ => $"{bytes} B",
    };
}
